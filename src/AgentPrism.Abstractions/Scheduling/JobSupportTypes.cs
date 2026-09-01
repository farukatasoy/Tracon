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
