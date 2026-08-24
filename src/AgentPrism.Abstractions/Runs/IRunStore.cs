namespace AgentPrism;

/// <summary>
/// Store for run records and the event stream.
/// </summary>
/// <remarks>
/// <para>
/// Events are <em>append-only</em>. An implementation must never update or delete
/// an event; it only adds them through <see cref="AppendEventAsync"/>.
/// </para>
/// <para>
/// <strong>Important:</strong> a failure in this store never interrupts the run.
/// Observability must not break functionality. The caller
/// (<c>RunEventWriter</c>) catches and logs the failure.
/// </para>
/// <para>
/// <strong>Tenant behavior</strong> is not uniform across this interface's
/// methods; each falls into one of three modes:
/// </para>
/// <list type="table">
/// <listheader><term>Mode</term><description>Methods and rule</description></listheader>
/// <item>
/// <term>Expected tenant</term>
/// <description>
/// <see cref="AppendEventAsync"/>, <see cref="CompleteRunAsync"/>,
/// <see cref="UpdateRunCostAsync"/>. The tenant carried by the call is
/// compared against the record's own tenant; a mismatch fails the write (or,
/// for <see cref="UpdateRunCostAsync"/>, silently affects no row). The
/// ambient tenant is <strong>not</strong> consulted — a workflow or job
/// queue writes on behalf of a run whose tenant may differ from whatever is
/// ambient on the calling thread. A <see langword="null"/> tenant on the
/// call performs no check.
/// </description>
/// </item>
/// <item>
/// <term>Ambient tenant</term>
/// <description>
/// <see cref="GetRunAsync"/>, <see cref="QueryRunsAsync"/>,
/// <see cref="ReadEventsAsync"/>, <see cref="GetStatisticsAsync"/>,
/// <see cref="GetToolUsageAsync"/>, <see cref="GetExperimentResultsAsync"/>,
/// <see cref="GetTimeSeriesAsync"/>, <see cref="ListToolInvocationsAsync"/>,
/// <see cref="RecordToolInvocationAsync"/>. Filtered by the current
/// <c>ITenantContext</c> unless the query object itself carries an explicit
/// tenant override.
/// </description>
/// </item>
/// <item>
/// <term>Tenant-independent</term>
/// <description>
/// <see cref="TouchHeartbeatAsync"/>, <see cref="ClaimOrphanedRunsAsync"/>.
/// Maintenance work that scans every tenant's rows; see each method's own
/// remarks.
/// </description>
/// </item>
/// </list>
/// <para>
/// <strong>Thread safety.</strong> An implementation is registered as a
/// <em>singleton</em> and must be safe under concurrent calls from
/// unrelated runs. It must not capture or depend on a scoped service — the
/// same instance answers every run's calls for the lifetime of the process.
/// </para>
/// <para>
/// <strong>Null / not-found semantics</strong> differ by method and are
/// documented individually; as a summary:
/// </para>
/// <list type="table">
/// <listheader><term>Method</term><description>An id that does not exist</description></listheader>
/// <item><term><see cref="GetRunAsync"/></term><description><see langword="null"/></description></item>
/// <item><term><see cref="ReadEventsAsync"/></term><description>An empty sequence — never throws</description></item>
/// <item><term><see cref="AppendEventAsync"/></term><description>Throws <see cref="AgentPrismException"/></description></item>
/// <item><term><see cref="CompleteRunAsync"/></term><description>Throws <see cref="AgentPrismException"/></description></item>
/// </list>
/// </remarks>
public interface IRunStore
{
    /// <summary>Opens a new run record, or updates it if one already exists.</summary>
    /// <param name="info">The start information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The effective record as it now stands, persisted. <see cref="RunRecord.UserId"/>
    /// and <see cref="RunRecord.Labels"/> reflect this call's own COALESCE
    /// against the previous row (see <strong>Idempotency</strong> below), not
    /// necessarily the values this call was given.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Idempotency.</strong> This method is an UPSERT: calling it a
    /// second time with a <see cref="RunStartInfo.RunId"/> that already
    /// exists does not open a second row, it updates the existing one. This
    /// exists because a queued run is written twice — a placeholder row is
    /// written first (typically <see cref="RunStatus.Queued"/>), then
    /// rewritten once a worker actually starts it (typically
    /// <see cref="RunStatus.Running"/>).
    /// </para>
    /// <para>
    /// Every field is overwritten by the second call <strong>except</strong>
    /// <see cref="RunStartInfo.UserId"/> and <see cref="RunStartInfo.Labels"/>,
    /// which are COALESCED: a <see langword="null"/> value on the second call
    /// leaves the previous value in place, a non-null value replaces it. The
    /// reason is attribution: the first write (typically inside the HTTP
    /// request, where the caller's identity is known) may know the user; the
    /// second write (typically a background worker, which has no request to
    /// read identity from) usually does not, and must not erase what the
    /// first write recorded.
    /// </para>
    /// <para>
    /// A conforming implementation must apply this same COALESCE rule to the
    /// value it returns — a caller that reads the return value instead of
    /// re-querying the record must see the same attribution a
    /// <see cref="GetRunAsync"/> call would.
    /// </para>
    /// </remarks>
    ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default);

    /// <summary>Appends an event to the run stream.</summary>
    /// <param name="runEvent">The event to append. The caller assigns its sequence number.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Tenant behavior — EXPECTED tenant.</strong> When
    /// <see cref="RunEvent.TenantId"/> is set, the write is filtered by it:
    /// the event is written only if the run's own tenant matches. The
    /// <em>ambient</em> tenant (<c>ITenantContext</c>) is deliberately
    /// <strong>not</strong> consulted for this method — a workflow or a
    /// background job queue legitimately writes on behalf of a run whose
    /// tenant differs from whichever tenant happens to be ambient on the
    /// calling thread. A <see langword="null"/> <see cref="RunEvent.TenantId"/>
    /// performs no tenant check at all.
    /// </para>
    /// <para>
    /// <strong>Null / not-found semantics.</strong> Unlike
    /// <see cref="ReadEventsAsync"/> (which returns an empty sequence for an
    /// unknown run), this method throws <see cref="AgentPrismException"/>
    /// when <see cref="RunEvent.RunId"/> does not identify an existing run,
    /// or when it exists but does not belong to the expected tenant.
    /// </para>
    /// <para>
    /// <strong>Event order.</strong> <see cref="RunEvent.Sequence"/> is
    /// assigned by the <em>caller</em>, not by this method — the store only
    /// writes it. The event stream is append-only: an implementation must
    /// never update or delete a written event. A second call carrying a
    /// <see cref="RunEvent.Sequence"/> value already used by this run is a
    /// caller error, not a legitimate retry, and must be rejected with
    /// <see cref="AgentPrismException"/> rather than silently accepted or
    /// silently ignored — an implementation must not let a duplicate
    /// sequence number pass through as a raw driver exception either.
    /// </para>
    /// </remarks>
    ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default);

    /// <summary>Closes the run and updates its summary.</summary>
    /// <param name="completion">The completion information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the record is updated.</returns>
    ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>Gets a run record.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> when it does not exist.</returns>
    ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Lists runs that match a filter, newest first.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching records.</returns>
    ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default);

    /// <summary>Summarizes runs.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Counts, token totals and the per-agent breakdown.</returns>
    /// <remarks>
    /// The summary is computed <strong>inside the store</strong>. Fetching records
    /// and aggregating them in memory would cover only a paged subset and give the
    /// wrong answer.
    /// </remarks>
    ValueTask<RunStatistics> GetStatisticsAsync(
        RunStatisticsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the events of a run in sequence order. Live streaming and historical
    /// replay take the same path.
    /// </summary>
    /// <param name="runId">The run id.</param>
    /// <param name="fromSequence">Reading starts at this sequence number, inclusive.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered event stream.</returns>
    IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        CancellationToken cancellationToken = default);

    /// <summary>Records a settled tool call.</summary>
    /// <param name="invocation">The call summary.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the record is written.</returns>
    /// <remarks>
    /// Kept apart from the event stream because durations and per-tool totals must
    /// be queryable without scanning the whole event stream.
    /// </remarks>
    ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the tool calls of a run in chronological order.</summary>
    /// <param name="runId">The run id.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The call records.</returns>
    ValueTask<IReadOnlyList<ToolInvocationRecord>> ListToolInvocationsAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>Summarizes usage per tool.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Call counts, failure rates and average durations.</returns>
    /// <remarks>
    /// Same reason as <see cref="GetStatisticsAsync"/>: the summary is computed
    /// <strong>inside the store</strong>.
    /// </remarks>
    ValueTask<IReadOnlyList<ToolUsage>> GetToolUsageAsync(
        ToolUsageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Summarizes an experiment per variant: count, failure rate, tokens, duration.
    /// </summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Per-variant results. A variant that took no traffic is absent.</returns>
    /// <remarks>
    /// The experiment breakdown is available only through this query;
    /// <c>experiment_id</c> is not emitted as a metric tag, for cardinality reasons.
    /// </remarks>
    ValueTask<IReadOnlyList<ExperimentVariantResult>> GetExperimentResultsAsync(
        ExperimentResultsQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces the per-bucket time series of runs, failures, tokens and cost.
    /// </summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The ordered bucket list. Empty buckets are returned too, with zero counts.</returns>
    /// <exception cref="AgentPrismException">
    /// The requested range exceeds <see cref="RunTimeSeriesBucketing.MaxBuckets"/>.
    /// </exception>
    ValueTask<IReadOnlyList<TimeSeriesPoint>> GetTimeSeriesAsync(
        RunTimeSeriesQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the cost of a run. Used only by the maintenance endpoint (<c>POST
    /// /api/stats/recalculate-costs</c>); on the normal path the cost is written once
    /// by <see cref="CompleteRunAsync"/>.
    /// </summary>
    /// <param name="runId">
    /// The run id.
    /// </param>
    /// <param name="cost">
    /// The new cost. May be <see langword="null"/>.
    /// </param>
    /// <param name="tenantId">
    /// The EXPECTED tenant of the run. Defence in depth; when <see langword="null"/> no
    /// tenant check is made. <c>TenantId</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token.
    /// </param>
    /// <returns>
    /// A task that completes when the record is updated.
    /// </returns>
    ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the "still here" mark of in-flight runs in one batch.
    /// </summary>
    /// <param name="runIds">The ids of the runs to mark.</param>
    /// <param name="at">The mark time (UTC).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the marks are written.</returns>
    /// <remarks>
    /// <para>
    /// Only <c>Running</c> rows are affected; an id that does not exist or is in
    /// another status is skipped silently — this is a maintenance signal and must
    /// not interrupt a run.
    /// </para>
    /// <para>
    /// <c>[TenantAgnostic]</c>: the ids come from the calling process's OWN
    /// <c>IRunCancellationRegistry</c> and are therefore already limited to the runs
    /// that process actually executes. A tenant filter would also need one query per
    /// tenant, which defeats the point of a heartbeat: a cheap signal that stays off
    /// the hot path.
    /// </para>
    /// </remarks>
    ValueTask TouchHeartbeatAsync(
        IReadOnlyCollection<Guid> runIds,
        DateTimeOffset at,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes <c>Running</c> rows that have not sent a heartbeat for a long time as
    /// <c>Failed</c> and records the reason.
    /// </summary>
    /// <param name="staleBefore">
    /// <c>Running</c> rows whose last heartbeat is older than this — or that never
    /// sent one — count as orphaned (UTC).
    /// </param>
    /// <param name="max">The maximum number of rows to close in this round.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records of the closed runs.</returns>
    /// <remarks>
    /// <para>
    /// Closing also writes a <see cref="RunEventType.RunFailed"/> event to the
    /// stream — the process that was writing the run is gone, so this method emits
    /// the event itself. The sequence number is one past the current maximum.
    /// </para>
    /// <para>
    /// Only <c>Running</c> rows are affected; <c>Queued</c> rows are owned by the
    /// job queue and this method DOES NOT TOUCH them.
    /// </para>
    /// <para>
    /// <c>[TenantAgnostic]</c>: this is maintenance work and scans the orphaned
    /// rows of every tenant. Filtering by the ambient tenant would leave the rows of
    /// other tenants <c>Running</c> forever.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
        DateTimeOffset staleBefore,
        int max,
        CancellationToken cancellationToken = default);
}
