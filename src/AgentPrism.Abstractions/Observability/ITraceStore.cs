namespace AgentPrism;

/// <summary>
/// The store for persisted spans.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An error from this store does not stop the run.</strong>
/// Observability must not break functionality; the caller
/// (<c>RunTraceCollector</c>) catches and logs errors.
/// </para>
/// <para>
/// The write path is <em>sampled</em>. Writing every span would bottleneck the
/// database at high volume; the sampling decision belongs to the caller
/// (<c>AgentPrismObservabilityOptions</c>), not the store.
/// </para>
/// <para>
/// <strong>Tenant behavior is not uniform across this interface's methods.</strong>
/// <see cref="WriteSpansAsync"/> is EXPECTED tenant: it reads
/// <see cref="TraceSpanBatch.TenantId"/>, never the ambient tenant, for the
/// same reason <see cref="IRunStore.AppendEventAsync"/> does — the writing
/// thread may not belong to the trace's own tenant.
/// <see cref="GetTraceByRunAsync"/> is AMBIENT tenant: it takes no explicit
/// tenant parameter and filters by the current <c>ITenantContext</c>
/// internally.
/// </para>
/// </remarks>
public interface ITraceStore
{
    /// <summary>
    /// Writes a trace's spans. Can be called multiple times for the same
    /// trace; spans are merged by identifier.
    /// </summary>
    /// <param name="batch">The span set to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default);

    /// <summary>Fetches a run's span tree.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The trace; <see langword="null"/> if no record exists.</returns>
    ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>A span set belonging to a single trace.</summary>
public sealed record TraceSpanBatch
{
    /// <summary>The W3C trace identifier.</summary>
    public required string TraceId { get; init; }

    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The associated run.</summary>
    public Guid? RunId { get; init; }

    /// <summary>The spans to write.</summary>
    public required IReadOnlyList<TraceSpan> Spans { get; init; }
}
