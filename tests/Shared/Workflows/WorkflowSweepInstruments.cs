using System.Diagnostics;

namespace Tracon.Tests.Workflows;

/// <summary>An event sink that takes every event and keeps none; the sweep counts the calls made to it.</summary>
internal sealed class NullSink : IRunEventSink
{
    /// <inheritdoc />
    public ValueTask OnEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default) => default;
}

/// <summary>
/// Counts the activities of the Tracon source that started and did not stop, among those of one trace: the test
/// opens a root activity of its own and the run's spans become its children. Other tests of the process start
/// activities on the same source at the same time; a count of the whole source would see theirs.
/// </summary>
internal sealed class ActivityCounter : IDisposable
{
    private const string SourceName = "Tracon.Tests.WorkflowCancellationSweep";

    private static readonly ActivitySource Source = new(SourceName);

    private readonly ActivityListener _listener;
    private readonly Activity _root;
    private int _open;

    /// <summary>Starts listening and opens the trace root.</summary>
    public ActivityCounter()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = static source => string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal)
                || string.Equals(source.Name, SourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = OnStarted,
            ActivityStopped = OnStopped,
        };

        ActivitySource.AddActivityListener(_listener);

        _root = Source.StartActivity("sweep", ActivityKind.Internal, parentContext: default)
            ?? throw new InvalidOperationException("The test activity source has no listener.");
    }

    /// <summary>Gets how many spans of the trace were started and not stopped.</summary>
    public int Open => Volatile.Read(ref _open);

    /// <inheritdoc />
    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }

    private bool IsRunSpan(Activity activity)
        => _root is not null && !ReferenceEquals(activity, _root) && activity.TraceId == _root.TraceId;

    private void OnStarted(Activity activity)
    {
        if (IsRunSpan(activity))
        {
            Interlocked.Increment(ref _open);
        }
    }

    private void OnStopped(Activity activity)
    {
        if (IsRunSpan(activity))
        {
            Interlocked.Decrement(ref _open);
        }
    }
}
