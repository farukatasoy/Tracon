using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// A store that keeps spans in process memory.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Limits:</strong> process lifetime, one node, and limited capacity.
/// When <see cref="MaxTraces"/> is exceeded, it removes the trace of the run that
/// was written first. Use <c>Tracon.PostgreSql</c> in production.
/// </para>
/// <para>
/// Reads are limited to the current tenant. Writes take the tenant from the batch,
/// while reads take it from <see cref="ITenantContext"/>. This is the
/// same rule as the SQL implementation.
/// </para>
/// <para>
/// A trace is keyed by its tenant and its run, as in the SQL implementation. The W3C trace
/// identifier is the caller's to choose: a caller that sends a <c>traceparent</c> gives every
/// run it starts the same identifier, and neither two runs nor two tenants may join one trace
/// because of it. A run written under two identifiers keeps one trace, with the identifier of
/// the last write.
/// </para>
/// </remarks>
internal sealed class InMemoryTraceStore : ITraceStore
{
    private readonly ConcurrentDictionary<RunKey, RunTrace> _byRun = new();
    private readonly ConcurrentQueue<RunKey> _insertionOrder = new();
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

        var key = new RunKey(AmbientTenantScope.Normalize(batch.TenantId), batch.RunId);
        var created = false;

        // The delegates close over `created` and set it on every pass: when another writer wins
        // the insert, the update delegate runs last and clears it, so the run is queued once.
        _byRun.AddOrUpdate(
            key,
            _ =>
            {
                created = true;
                return Create(batch);
            },
            (_, existing) =>
            {
                created = false;
                return Merge(existing, batch);
            });

        if (created)
        {
            _insertionOrder.Enqueue(key);
        }

        TrimIfNeeded();

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _byRun.TryGetValue(new RunKey(_tenantContext.TenantId, runId), out var trace)
            ? new ValueTask<RunTrace?>(trace)
            : new ValueTask<RunTrace?>((RunTrace?)null);
    }

    private static RunTrace Create(TraceSpanBatch batch)
    {
        var spans = Sort(batch.Spans);

        return new RunTrace
        {
            Id = TraconId.NewId(),
            TraceId = batch.TraceId,
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
            TraceId = batch.TraceId,
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
        while (_byRun.Count > MaxTraces && _insertionOrder.TryDequeue(out var oldest))
        {
            _byRun.TryRemove(oldest, out _);
        }
    }

    /// <summary>The tenant and the run that together name one trace.</summary>
    /// <param name="TenantId">The tenant the spans were written under, normalized.</param>
    /// <param name="RunId">The run the spans belong to.</param>
    private readonly record struct RunKey(string TenantId, Guid RunId);
}
