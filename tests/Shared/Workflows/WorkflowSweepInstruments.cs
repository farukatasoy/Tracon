using System.Collections.Concurrent;
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

/// <summary>
/// The runner's clock. Time is the system's; a timer is not. A short timer (the pause before the closing write is
/// asked again is one) fires at once, so no run waits for real time; a long one (the run's deadline) never fires
/// and stays armed until its owner disposes it, which is what <see cref="Armed"/> counts: timers made and neither
/// fired nor disposed.
/// </summary>
internal sealed class TimerLedgerClock : TimeProvider
{
    private static readonly TimeSpan ShortTimer = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<LedgerTimer, string> _live = new();

    /// <summary>Gets how many timers were made and not disposed.</summary>
    public int Armed => _live.Count;

    /// <summary>Gets where the timers that are not disposed were made.</summary>
    public string Origins => string.Join(" || ", _live.Values.Select(static stack => string.Join(" < ", stack
        .Split('\n')
        .Select(static line => line.Trim())
        .Where(static line => line.Contains("Tracon", StringComparison.Ordinal) && !line.Contains("TimerLedgerClock", StringComparison.Ordinal))
        .Take(5))));

    /// <inheritdoc />
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new LedgerTimer(this, callback, state);

        _live[timer] = Environment.StackTrace;
        timer.Change(dueTime, period);

        return timer;
    }

    private sealed class LedgerTimer(TimerLedgerClock owner, TimerCallback callback, object? state) : ITimer
    {
        private int _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (dueTime >= TimeSpan.Zero && dueTime <= ShortTimer)
            {
                // A one-shot timer that has fired is no longer armed, whether or not its owner has disposed it yet.
                _ = Task.Run(
                    () =>
                    {
                        owner._live.TryRemove(this, out _);
                        callback(state);
                    },
                    CancellationToken.None);
            }

            return true;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner._live.TryRemove(this, out _);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();

            return default;
        }
    }
}
