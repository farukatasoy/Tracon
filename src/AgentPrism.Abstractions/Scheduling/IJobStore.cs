namespace AgentPrism;

/// <summary>
/// The store for queued jobs and their items.
/// </summary>
/// <remarks>
/// <para>
/// The PostgreSql implementation leases with <c>FOR UPDATE SKIP LOCKED</c>:
/// even if multiple workers connect to the same database, a job is picked up
/// by only one worker. The in-memory implementation provides the same
/// contract with a lock and a timestamp.
/// </para>
/// <para>
/// <strong>Important:</strong> <see cref="ReportItemAsync"/> and state
/// transitions must be idempotent — the same item may be reported twice if
/// the lease expires and the job is re-leased.
/// </para>
/// </remarks>
public interface IJobStore
{
    /// <summary>
    /// Enqueues a new job and creates its items.
    /// </summary>
    /// <param name="job">
    /// The job record. <see cref="JobRecord.Status"/> is ignored; the store
    /// always starts it with <see cref="JobStatus.Pending"/>.
    /// </param>
    /// <param name="items">The job's input list. Sequence numbers are assigned by list order.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created job record.</returns>
    ValueTask<JobRecord> EnqueueAsync(
        JobRecord job,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases the oldest job ready to run. Returns <see langword="null"/> if
    /// no such job exists.
    /// </summary>
    /// <param name="owner">The leasing worker's identifier.</param>
    /// <param name="leaseDuration">The lease's validity duration.</param>
    /// <param name="lanes">
    /// Restricts leasing to these lanes (see <see cref="JobLanes"/>).
    /// <see langword="null"/> or empty applies no filter — a job from any
    /// lane may be leased, the same behavior as before lanes existed.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The leased job; <see langword="null"/> if none exists.</returns>
    ValueTask<JobRecord?> LeaseAsync(
        string owner,
        TimeSpan leaseDuration,
        IReadOnlyList<string>? lanes,
        CancellationToken cancellationToken = default);

    /// <summary>Extends an in-progress job's lease. Does not change the job's status.</summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="owner">The identifier of the worker holding the lease. The operation is ignored if it does not match.</param>
    /// <param name="leaseDuration">The new lease duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask RenewLeaseAsync(
        Guid jobId,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a leased (<see cref="JobStatus.Leased"/>) job to
    /// <see cref="JobStatus.Running"/>. The worker calls this right after
    /// obtaining the lease, before starting execution.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="owner">The identifier of the worker holding the lease. The operation is ignored if it does not match.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the transition happened.</returns>
    ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default);

    /// <summary>Finalizes a job.</summary>
    /// <param name="completion">The finalization information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a job to the <see cref="JobStatus.Pending"/> state; releases
    /// its lease. <see cref="JobRecord.Attempt"/> does not change here since
    /// it is already incremented at lease time.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="errorMessage">The most recent attempt's error.</param>
    /// <param name="retryAfter">
    /// The time to wait before the next attempt. If <see langword="null"/> or
    /// zero, the job may be re-leased immediately (the old behavior).
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// <para>
    /// <paramref name="retryAfter"/> pushes the <see cref="JobRecord.ScheduledFor"/>
    /// field forward; since the lease query already applies
    /// <c>scheduled_for &lt;= now</c>, backoff needs no additional mechanism.
    /// </para>
    /// <para>
    /// Webhook delivery builds its 1 min / 5 min / 30 min / 2 hr /
    /// 6 hr ladder with this parameter. No second queue or second lease
    /// mechanism is written.
    /// </para>
    /// </remarks>
    ValueTask ReleaseForRetryAsync(
        Guid jobId,
        string errorMessage,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to cancel a job. Only a job in the <see cref="JobStatus.Pending"/>,
    /// <see cref="JobStatus.Leased"/>, or <see cref="JobStatus.Running"/>
    /// state can be cancelled.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the cancellation happened.</returns>
    ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Fetches a single job record.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The record; <see langword="null"/> if it does not exist or belongs to another tenant.</returns>
    ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Lists jobs by filter. The newest record is returned first.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The records.</returns>
    ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default);

    /// <summary>Lists a job's items, by sequence number.</summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The items.</returns>
    ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports an item's processing result and updates the job's
    /// <see cref="JobRecord.DoneItems"/>/<see cref="JobRecord.FailedItems"/> counters.
    /// </summary>
    /// <param name="item">The item result.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default);
}
