namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>A span store that keeps every batch exactly as the collector wrote it, one per run.</summary>
/// <remarks>
/// The built-in stores key a trace by its W3C trace id, so two runs that share one id read back as one trace. A test
/// that asks which run a span was given to has to look at what the collector wrote, before the store merges it.
/// </remarks>
internal sealed class RecordingTraceStore : ITraceStore
{
    private readonly List<TraceSpanBatch> _batches = [];

    /// <summary>Gets the batches written so far.</summary>
    public IReadOnlyList<TraceSpanBatch> Batches
    {
        get
        {
            lock (_batches)
            {
                return [.. _batches];
            }
        }
    }

    /// <inheritdoc />
    public ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default)
    {
        lock (_batches)
        {
            _batches.Add(batch);
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => new((RunTrace?)null);
}
