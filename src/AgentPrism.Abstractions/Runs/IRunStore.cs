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
/// </remarks>
public interface IRunStore
{
    /// <summary>Opens a new run record.</summary>
    /// <param name="info">The start information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created record.</returns>
    ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default);

    /// <summary>Appends an event to the run stream.</summary>
    /// <param name="runEvent">The event to append. The caller assigns its sequence number.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the event is written.</returns>
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
    /// Updates the cost of a run. Used only by the maintenance endpoint
    /// (<c>POST /api/stats/recalculate-costs</c>); on the normal path the cost is
    /// written once by <see cref="CompleteRunAsync"/>.
    /// </summary>
    /// <param name="runId">The run id.</param>
    /// <param name="cost">The new cost. May be <see langword="null"/>.</param>
    /// <param name="tenantId">
    /// The EXPECTED tenant of the run. Defence in depth; when <see langword="null"/>
    /// no tenant check is made. Rationale: <see cref="RunEvent.TenantId"/>, K-355.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the record is updated.</returns>
    ValueTask UpdateRunCostAsync(
        Guid runId,
        RunCost? cost,
        string? tenantId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the "still here" mark of in-flight runs in one batch (phase 54).
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
    /// 🚨 <c>[TenantAgnostic]</c>: the ids come from the calling process's OWN
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
    /// <c>Failed</c> and records the reason (phase 54).
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
    /// 🚨 <c>[TenantAgnostic]</c>: this is maintenance work and scans the orphaned
    /// rows of every tenant. Filtering by the ambient tenant would leave the rows of
    /// other tenants <c>Running</c> forever.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<RunRecord>> ClaimOrphanedRunsAsync(
        DateTimeOffset staleBefore,
        int max,
        CancellationToken cancellationToken = default);
}
