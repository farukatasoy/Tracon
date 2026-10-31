namespace Tracon;

/// <summary>A filter for querying the job list.</summary>
public sealed record JobQuery
{
    /// <summary>Fetches only this tenant's jobs.</summary>
    public string? TenantId { get; init; }

    /// <summary>Fetches only jobs dispatched to this handler key. See <see cref="JobHandlerKeys"/>.</summary>
    public string? HandlerKey { get; init; }

    /// <summary>Fetches only jobs in this lane. See <see cref="JobLanes"/>.</summary>
    public string? Lane { get; init; }

    /// <summary>Fetches only jobs in this status.</summary>
    public JobStatus? Status { get; init; }

    /// <summary>Fetches only jobs produced by this schedule.</summary>
    public Guid? ScheduleId { get; init; }

    /// <summary>The number of records to skip.</summary>
    public int Skip { get; init; }

    /// <summary>The maximum number of records to fetch.</summary>
    public int Take { get; init; } = 50;
}

/// <summary>The lease one worker holds on one attempt of a job.</summary>
/// <remarks>
/// <para>
/// A write that only the lease holder may make (<see cref="IJobStore.RenewLeaseAsync"/>,
/// <see cref="IJobStore.MarkRunningAsync"/>, <see cref="IJobStore.CompleteAsync"/> and
/// <see cref="IJobStore.ReleaseForRetryAsync"/>) carries this value, and a store applies it only
/// while the job's lease is exactly this one.
/// </para>
/// <para>
/// The attempt number is part of the lease because the owner identifier is not enough: a worker
/// that leases a job again keeps the same <see cref="Owner"/>, and the attempt it had before must not
/// be able to change the attempt that replaced it. <see cref="JobRecord.Attempt"/> grows by one
/// each time the job is leased, so an older attempt always carries a smaller number.
/// </para>
/// </remarks>
public sealed record JobLease
{
    /// <summary>The job identifier.</summary>
    public required Guid JobId { get; init; }

    /// <summary>The identifier of the worker holding the lease.</summary>
    /// <exception cref="ArgumentException">The value is <see langword="null"/>, empty or white space.</exception>
    public required string Owner
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            field = value;
        }
    }

    /// <summary>The attempt this lease was taken for: <see cref="JobRecord.Attempt"/> as <see cref="IJobStore.LeaseAsync"/> returned it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public required int Attempt
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    }

    /// <summary>Takes the lease from the record <see cref="IJobStore.LeaseAsync"/> returned.</summary>
    /// <param name="leased">The leased job record.</param>
    /// <returns>The lease the record describes.</returns>
    /// <exception cref="ArgumentException">The record carries no lease owner or no attempt: it is not a leased record.</exception>
    public static JobLease Of(JobRecord leased)
    {
        ArgumentNullException.ThrowIfNull(leased);

        return new JobLease
        {
            JobId = leased.Id,
            Owner = leased.LeaseOwner ?? throw new ArgumentException("The record is not leased: it carries no lease owner.", nameof(leased)),
            Attempt = leased.Attempt,
        };
    }
}

/// <summary>The information needed to finalize a job.</summary>
/// <remarks>The job is named by the <see cref="JobLease"/> passed to <see cref="IJobStore.CompleteAsync"/>.</remarks>
public sealed record JobCompletion
{
    /// <summary>The final status.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>The completion time (UTC).</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>The failure message. Populated only for <see cref="JobStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>The result of a processed job item. Reported with <see cref="IJobStore.ReportItemAsync"/>.</summary>
public sealed record JobItemResult
{
    /// <summary>The identifier of the job it belongs to.</summary>
    public required Guid JobId { get; init; }

    /// <summary>The item's sequence number.</summary>
    public required int Seq { get; init; }

    /// <summary>The processing result.</summary>
    public required JobItemStatus Status { get; init; }

    /// <summary>The identifier of the run record created. <see langword="null"/> if not applicable.</summary>
    public Guid? RunId { get; init; }

    /// <summary>The failure message. Populated only for <see cref="JobItemStatus.Failed"/>.</summary>
    public string? Error { get; init; }
}

/// <summary>The number of open jobs in one lane/status pair.</summary>
/// <remarks>
/// <para>
/// Returned by <see cref="IJobStore.GetQueueDepthAsync"/> and published through
/// the <c>tracon.job.queue.depth</c> observable gauge. Only the
/// <strong>open</strong> statuses are reported — <see cref="JobStatus.Pending"/>,
/// <see cref="JobStatus.Leased"/>, and <see cref="JobStatus.Running"/>. Terminal
/// statuses are counted by <c>tracon.job.executions</c> instead of by
/// scanning the table, which ties the cost of this query to the amount of
/// <em>outstanding</em> work rather than to the size of the queue's history.
/// </para>
/// <para>
/// A pair with no open jobs is not reported; the absence of a row means zero.
/// </para>
/// </remarks>
public sealed record JobQueueDepth
{
    /// <summary>The lane the jobs belong to. See <see cref="JobLanes"/>.</summary>
    public required string Lane { get; init; }

    /// <summary>The open status the jobs are in.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>The number of jobs in this lane and status.</summary>
    public required long Count { get; init; }
}
