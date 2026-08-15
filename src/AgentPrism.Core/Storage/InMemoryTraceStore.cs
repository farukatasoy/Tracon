using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// A store that keeps spans in process memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Limits:</strong> process lifetime, one node, and limited capacity.
/// When <see cref="MaxTraces"/> is exceeded, it removes the oldest trace.
/// Use <c>AgentPrism.PostgreSql</c> in production.
/// </para>
/// <para>
/// Reads are limited to the current tenant. Writes take the tenant from the batch,
/// while reads take it from <see cref="ITenantContext"/> (Phase 41). This is the
/// same rule as the SQL implementation.
/// </para>
/// </remarks>
public sealed class InMemoryTraceStore : ITraceStore
{
    private readonly ConcurrentDictionary<string, RunTrace> _byTraceId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _traceIdByRun = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new in-memory span store.</summary>
    /// <param name="tenantContext">
    /// The current tenant context. If omitted, the store behaves as single tenant.
    /// </param>
    public InMemoryTraceStore(ITenantContext? tenantContext = null)
        => _tenantContext = tenantContext ?? FixedTenantContext.Default;

    /// <summary>The maximum number of traces to keep in memory.</summary>
    public int MaxTraces { get; init; } = 500;

    /// <inheritdoc />
    public ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Spans.Count == 0)
        {
            return default;
        }

        _byTraceId.AddOrUpdate(
            batch.TraceId,
            static (traceId, state) => Create(traceId, state),
            static (traceId, existing, state) => Merge(existing, state),
            batch);

        if (batch.RunId is { } runId)
        {
            _traceIdByRun[runId] = batch.TraceId;
        }

        _insertionOrder.Enqueue(batch.TraceId);
        TrimIfNeeded();

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (_traceIdByRun.TryGetValue(runId, out var traceId)
            && _byTraceId.TryGetValue(traceId, out var trace)
            && string.Equals(trace.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            return new ValueTask<RunTrace?>(trace);
        }

        return new ValueTask<RunTrace?>((RunTrace?)null);
    }

    private static RunTrace Create(string traceId, TraceSpanBatch batch)
    {
        var spans = Sort(batch.Spans);

        return new RunTrace
        {
            Id = AgentPrismId.NewId(),
            TraceId = traceId,
            RunId = batch.RunId,
            TenantId = batch.TenantId,
            StartedAt = spans[0].StartedAt,
            EndedAt = LatestEnd(spans),
            Spans = spans,
        };
    }

    private static RunTrace Merge(RunTrace existing, TraceSpanBatch batch)
    {
        // The same span can be written twice because identifiers are derived. Merge
        // by identifier to remove the duplicate.
        var byId = existing.Spans.ToDictionary(static span => span.Id);

        foreach (var span in batch.Spans)
        {
            byId[span.Id] = span;
        }

        var spans = Sort([.. byId.Values]);

        return existing with
        {
            RunId = batch.RunId ?? existing.RunId,
            StartedAt = spans[0].StartedAt,
            EndedAt = LatestEnd(spans),
            Spans = spans,
        };
    }

    private static TraceSpan[] Sort(IReadOnlyList<TraceSpan> spans)
    {
        var sorted = spans.ToArray();
        Array.Sort(sorted, static (left, right) => left.StartedAt.CompareTo(right.StartedAt));
        return sorted;
    }

    private static DateTimeOffset? LatestEnd(IReadOnlyList<TraceSpan> spans)
    {
        DateTimeOffset? latest = null;

        foreach (var span in spans)
        {
            if (span.EndedAt is { } ended && (latest is null || ended > latest))
            {
                latest = ended;
            }
        }

        return latest;
    }

    private void TrimIfNeeded()
    {
        while (_byTraceId.Count > MaxTraces && _insertionOrder.TryDequeue(out var oldest))
        {
            if (!_byTraceId.TryRemove(oldest, out var removed))
            {
                continue;
            }

            if (removed.RunId is { } runId)
            {
                _traceIdByRun.TryRemove(runId, out _);
            }
        }
    }
}
