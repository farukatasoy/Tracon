namespace Tracon;

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
/// <para>
/// <strong>A write that needs the lease is fenced by it.</strong>
/// <see cref="RenewLeaseAsync"/>, <see cref="MarkRunningAsync"/>,
/// <see cref="CompleteAsync"/> and <see cref="ReleaseForRetryAsync"/> take the
/// <see cref="JobLease"/> the worker was given and change the job only while the job's
/// lease is exactly that one: the same <see cref="JobLease.JobId"/>, the same
/// <see cref="JobLease.Owner"/> and the same <see cref="JobLease.Attempt"/>, in the
/// <see cref="JobStatus.Leased"/> or <see cref="JobStatus.Running"/> status. They return
/// <see langword="false"/>, and leave the job as it is, when it is not. This is how a
/// worker that stalled past its lease (a long pause, a network cut) is kept from closing or
/// requeuing the attempt of the worker that took the job over. The check and the write are
/// one atomic step. A store that checks first and writes after leaves the gap open.
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
    /// <returns>
    /// The created job record, as the store holds it: the same record a <see cref="GetAsync"/> call reports an instant later.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Resolve the stored lane with <see cref="JobLanes.Resolve"/>, passing the
    /// configured <c>TraconSchedulingOptions.LaneByHandlerKey</c> map. That map is
    /// applied here and nowhere else, so a store that skips the call silently
    /// ignores it.
    /// </para>
    /// <para>
    /// <strong>A job id that is already stored is refused:</strong> the call throws,
    /// and the stored job and its items do not change. A queued run's job carries the
    /// run's id, and two callers can write the same job at the same time (a repeated
    /// answer that writes the job a crash left missing); the refusal is what keeps the
    /// work from running twice. A store that replaced the job would reset a leased
    /// job to <see cref="JobStatus.Pending"/>.
    /// </para>
    /// </remarks>
    ValueTask<JobRecord> EnqueueAsync(
        JobRecord job,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Leases the oldest job ready to run. Returns <see langword="null"/> if
    /// no such job exists.
    /// </summary>
    /// <param name="request">
    /// The lease request: the worker, the lease duration, the lanes
    /// (<see cref="JobLeaseRequest.Lanes"/>, <see langword="null"/> or empty applies no
    /// filter) and an optional per-tenant ceiling (<see cref="JobLeaseRequest.TenantCap"/>).
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The leased job; <see langword="null"/> if none exists.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="JobLeaseRequest.LeaseDuration"/> is outside one millisecond to <c>2147483647000</c> milliseconds (a little
    /// over 68 years). The call checks it before anything else and leases nothing, so no attempt is spent.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A job is ready when it is pending and due, or when its lease is over. Both are decided on the store's clock, and the
    /// lease is taken for <see cref="JobLeaseRequest.LeaseDuration"/> from it: the PostgreSQL and SQL Server stores read the
    /// database server's clock inside the statement, so the clocks of the nodes that share the database do not enter the
    /// decision and a node whose clock is ahead cannot take a job another node is running. The in-memory store and SQLite
    /// are used by one process and read the clock they were given (<see cref="TimeProvider"/>).
    /// </para>
    /// <para>
    /// With <see cref="JobLeaseRequest.TenantCap"/> set, a ready job whose handler key is in
    /// the cap's keys is skipped while its tenant already holds the cap's number of live
    /// leases on those keys; the oldest job that is still eligible is leased instead. A job
    /// with an expired lease is not counted, and it can be leased again. With no cap the
    /// oldest ready job is leased, whichever tenant owns it.
    /// </para>
    /// <para>
    /// The count is read, not locked: workers that lease at the same moment can take the
    /// tenant past the ceiling by up to their own number (see <see cref="JobTenantCap"/>).
    /// </para>
    /// </remarks>
    ValueTask<JobRecord?> LeaseAsync(
        JobLeaseRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Extends an in-progress job's lease. Does not change the job's status.</summary>
    /// <param name="lease">The lease to extend.</param>
    /// <param name="leaseDuration">
    /// The new lease duration: from one millisecond to <c>2147483647000</c> milliseconds (a little over 68 years).
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="leaseDuration"/> is outside that range. The check comes before anything else, and the lease stays as
    /// it was.
    /// </exception>
    /// <returns>
    /// <see langword="true"/> if the lease was extended; <see langword="false"/> if the job's
    /// lease is no longer <paramref name="lease"/> (another worker leased the job, or it was
    /// cancelled or finished), in which case nothing changed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A worker that gets <see langword="false"/> has lost the job and must stop working on it.
    /// A cancelled job answers <see langword="false"/> too, because cancelling clears the lease.
    /// </para>
    /// <para>
    /// The new end is <paramref name="leaseDuration"/> from the store's clock, not from the
    /// caller's: the PostgreSQL and SQL Server stores read the database server's clock in the
    /// statement, the same clock that decides whether a lease is over.
    /// </para>
    /// </remarks>
    ValueTask<bool> RenewLeaseAsync(
        JobLease lease,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a leased (<see cref="JobStatus.Leased"/>) job to
    /// <see cref="JobStatus.Running"/>. The worker calls this right after
    /// obtaining the lease, before starting execution.
    /// </summary>
    /// <param name="lease">The lease the worker holds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the transition happened; <see langword="false"/> if the job's
    /// lease is no longer <paramref name="lease"/> or the job is not <see cref="JobStatus.Leased"/>.
    /// </returns>
    ValueTask<bool> MarkRunningAsync(JobLease lease, CancellationToken cancellationToken = default);

    /// <summary>Finalizes a job.</summary>
    /// <param name="lease">The lease the worker holds.</param>
    /// <param name="completion">The finalization information.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the job was finalized; <see langword="false"/> if the job's
    /// lease is no longer <paramref name="lease"/>, in which case nothing changed.
    /// </returns>
    /// <remarks>
    /// Only an open job (<see cref="JobStatus.Leased"/> or <see cref="JobStatus.Running"/>)
    /// that <paramref name="lease"/> still holds is finalized; the call leaves any other job
    /// unchanged, one that already reached a terminal status included. A cancelled job stays
    /// <see cref="JobStatus.Cancelled"/> even when its handler throws afterwards, and the call
    /// answers <see langword="false"/> for it.
    /// </remarks>
    ValueTask<bool> CompleteAsync(JobLease lease, JobCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a job to the <see cref="JobStatus.Pending"/> state; releases
    /// its lease. <see cref="JobRecord.Attempt"/> does not change here since
    /// it is already incremented at lease time.
    /// </summary>
    /// <param name="lease">The lease the worker holds.</param>
    /// <param name="errorMessage">The most recent attempt's error.</param>
    /// <param name="retryAfter">
    /// The time to wait before the next attempt, up to <c>2147483647000</c> milliseconds (a little over 68 years). If
    /// <see langword="null"/> or zero, the job may be re-leased immediately (the old behavior). A negative delay is not
    /// "at once": it is refused.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="retryAfter"/> is negative or longer than that. The check comes before anything else, and the job
    /// stays leased.
    /// </exception>
    /// <returns>
    /// <see langword="true"/> if the job went back to the queue; <see langword="false"/> if the
    /// job's lease is no longer <paramref name="lease"/>, in which case nothing changed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <paramref name="retryAfter"/> pushes the <see cref="JobRecord.ScheduledFor"/>
    /// field forward; since the lease query already applies
    /// <c>scheduled_for &lt;= now</c>, backoff needs no additional mechanism. The delay
    /// is measured from the same clock that query reads (on PostgreSQL and SQL Server the
    /// database server's, in whole milliseconds), so the node that releases the job and
    /// the node that leases it again cannot disagree about when the delay is over.
    /// </para>
    /// <para>
    /// Webhook delivery builds its 1 min / 5 min / 30 min / 2 hr /
    /// 6 hr ladder with this parameter. No second queue or second lease
    /// mechanism is written.
    /// </para>
    /// <para>
    /// Like <see cref="CompleteAsync"/>, the call leaves a job in a terminal
    /// status unchanged: a cancelled job never returns to
    /// <see cref="JobStatus.Pending"/>.
    /// </para>
    /// </remarks>
    ValueTask<bool> ReleaseForRetryAsync(
        JobLease lease,
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
    /// <remarks>
    /// <para>
    /// Only an item that is still <see cref="JobItemStatus.Pending"/> is changed. A
    /// report for an item that already finished changes neither the item nor the
    /// counters, so an item re-reported after a lease takeover is counted once.
    /// </para>
    /// <para>
    /// A report whose status is <see cref="JobItemStatus.Pending"/> is a marker: it
    /// records <see cref="JobItemResult.RunId"/> on the item, leaves the item
    /// pending, and counts nothing. A later report that finishes the item is
    /// counted as usual.
    /// </para>
    /// </remarks>
    ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the jobs still outstanding, grouped by lane and status.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// One record per lane/status pair that has at least one open job. A pair
    /// with no open jobs is omitted rather than reported as zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Only the open statuses are counted — <see cref="JobStatus.Pending"/>,
    /// <see cref="JobStatus.Leased"/>, and <see cref="JobStatus.Running"/>. The
    /// terminal ones are already counted by the
    /// <c>tracon.job.executions</c> counter as each job finishes; counting
    /// them here would make the query's cost grow with the queue's whole
    /// history instead of with the work that is actually outstanding.
    /// </para>
    /// <para>
    /// This is an aggregate query, deliberately placed on this interface rather
    /// than split into a separate one — the same layout
    /// <see cref="IRunStore.GetStatisticsAsync"/> already uses. It takes
    /// <strong>no tenant argument</strong>: queue depth is an operator signal
    /// about the worker pool, which leases across every tenant
    /// (<see cref="LeaseAsync"/>), so there is no tenant boundary to apply here.
    /// </para>
    /// <para>
    /// The SQL implementations answer this from the <c>jobs_claim_idx</c>
    /// index, whose partial condition covers exactly these three statuses.
    /// </para>
    /// </remarks>
    ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default);
}
