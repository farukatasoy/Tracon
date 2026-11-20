namespace Tracon;

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
/// (<c>TraconObservabilityOptions</c>), not the store.
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
/// <para>
/// <strong>A trace belongs to one run.</strong> The store keeps one trace per
/// tenant and run. The W3C trace identifier is the caller's to choose: a gateway
/// that sends a <c>traceparent</c> header gives every run it starts the same
/// identifier, so the identifier names a property of the trace and never its key.
/// </para>
/// </remarks>
public interface ITraceStore
{
    /// <summary>
    /// Writes a run's spans. Can be called multiple times for the same run;
    /// spans are merged by identifier into the run's one trace, whatever W3C
    /// trace identifier each call carries.
    /// </summary>
    /// <param name="batch">The span set to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <exception cref="ArgumentException">
    /// <see cref="TraceSpanBatch.RunId"/> is <see cref="Guid.Empty"/>. A trace belongs to a run, and an empty identifier
    /// names none: every store refuses it before it writes, whether or not the batch holds spans.
    /// </exception>
    ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default);

    /// <summary>Fetches a run's span tree.</summary>
    /// <param name="runId">The run identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The trace; <see langword="null"/> if no record exists.</returns>
    ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>A span set belonging to a single run.</summary>
public sealed record TraceSpanBatch
{
    /// <summary>
    /// The W3C trace identifier. Other runs of the same tenant can carry the same
    /// identifier; the store does not key the trace by it. When one run is written
    /// under two identifiers, the store keeps the identifier of the last write.
    /// </summary>
    public required string TraceId { get; init; }

    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The run these spans belong to. It names the trace; <see cref="Guid.Empty"/> is refused.</summary>
    public required Guid RunId { get; init; }

    /// <summary>The spans to write.</summary>
    public required IReadOnlyList<TraceSpan> Spans { get; init; }
}
