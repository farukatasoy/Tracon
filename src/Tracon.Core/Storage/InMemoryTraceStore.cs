using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// A store that keeps spans in process memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Limits:</strong> process lifetime, one node, and limited capacity.
/// When <see cref="MaxTraces"/> is exceeded, it removes the oldest trace.
/// Use <c>Tracon.PostgreSql</c> in production.
/// </para>
/// <para>
/// Reads are limited to the current tenant. Writes take the tenant from the batch,
/// while reads take it from <see cref="ITenantContext"/>. This is the
/// same rule as the SQL implementation.
/// </para>
/// <para>
/// A trace is keyed by its tenant and its trace identifier, as in the SQL
/// implementation: a caller that sends a W3C <c>traceparent</c> gives every run it starts the
/// same trace identifier, and two tenants' spans must not join one trace.
/// </para>
/// </remarks>
internal sealed class InMemoryTraceStore : ITraceStore
{
    private readonly ConcurrentDictionary<TraceKey, RunTrace> _byTrace = new();
    private readonly ConcurrentDictionary<Guid, TraceKey> _traceByRun = new();
    private readonly ConcurrentQueue<TraceKey> _insertionOrder = new();
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new in-memory span store.</summary>
    /// <param name="tenantContext">
    /// The current tenant context. If omitted, the store behaves as single tenant.
    /// </param>
    public InMemoryTraceStore(ITenantContext? tenantContext = null)
        => _tenantContext = CanonicalTenantContext.Wrap(tenantContext ?? FixedTenantContext.Default);

    /// <summary>The maximum number of traces to keep in memory.</summary>
    public int MaxTraces { get; init; } = 500;

    /// <inheritdoc />
    public ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();

        if (batch.Spans.Count == 0)
        {
            return default;
        }

        var key = new TraceKey(AmbientTenantScope.Normalize(batch.TenantId), batch.TraceId);

        _byTrace.AddOrUpdate(
            key,
            static (created, state) => Create(created.TraceId, state),
            static (_, existing, state) => Merge(existing, state),
            batch);

        if (batch.RunId is { } runId)
        {
            _traceByRun[runId] = key;
        }

        _insertionOrder.Enqueue(key);
        TrimIfNeeded();

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_traceByRun.TryGetValue(runId, out var key)
            && string.Equals(key.TenantId, _tenantContext.TenantId, StringComparison.Ordinal)
            && _byTrace.TryGetValue(key, out var trace))
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
            Id = TraconId.NewId(),
            TraceId = traceId,
            RunId = batch.RunId,
            TenantId = AmbientTenantScope.Normalize(batch.TenantId),
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
        while (_byTrace.Count > MaxTraces && _insertionOrder.TryDequeue(out var oldest))
        {
            if (!_byTrace.TryRemove(oldest, out var removed))
            {
                continue;
            }

            if (removed.RunId is { } runId)
            {
                _traceByRun.TryRemove(runId, out _);
            }
        }
    }

    /// <summary>The tenant and the W3C trace identifier that together name one trace.</summary>
    /// <param name="TenantId">The tenant the spans were written under, normalized.</param>
    /// <param name="TraceId">The W3C trace identifier.</param>
    private readonly record struct TraceKey(string TenantId, string TraceId);
}
