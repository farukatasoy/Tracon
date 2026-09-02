namespace AgentPrism;

/// <summary>A filter for querying the job list.</summary>
public sealed record JobQuery
{
    /// <summary>Fetches only this tenant's jobs.</summary>
    public string? TenantId { get; init; }

    /// <summary>Fetches only jobs of this kind.</summary>
    public JobKind? Kind { get; init; }

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

/// <summary>The information needed to finalize a job.</summary>
public sealed record JobCompletion
{
    /// <summary>The job identifier.</summary>
    public required Guid JobId { get; init; }

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
/// the <c>agentprism.job.queue.depth</c> observable gauge. Only the
/// <strong>open</strong> statuses are reported — <see cref="JobStatus.Pending"/>,
/// <see cref="JobStatus.Leased"/>, and <see cref="JobStatus.Running"/>. Terminal
/// statuses are counted by <c>agentprism.job.executions</c> instead of by
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
