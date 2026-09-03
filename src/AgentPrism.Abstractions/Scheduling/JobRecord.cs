using System.Text.Json;

namespace AgentPrism;

/// <summary>The summary of a queued job. The header of its items (<see cref="JobItemRecord"/>).</summary>
public sealed record JobRecord
{
    /// <summary>The job identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant the job belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// The identifier of the schedule that produced this job.
    /// <see langword="null"/> for manually created (one-off) jobs.
    /// </summary>
    public Guid? ScheduleId { get; init; }

    /// <summary>
    /// The key of the <see cref="IJobHandler"/> that executes this job. See
    /// <see cref="JobHandlerKeys"/>.
    /// </summary>
    /// <remarks>
    /// Both the job's classification and its dispatch identity; there is no
    /// second field. The worker matches it against the registered keys
    /// ordinally, so a job whose key nobody registered fails with
    /// <see cref="JobErrorCodes.UnknownHandlerKey"/> instead of running the
    /// wrong handler.
    /// </remarks>
    public required string HandlerKey { get; init; }

    /// <summary>
    /// The lane this job runs in. See <see cref="JobLanes"/>. A worker only
    /// leases jobs from the lanes it subscribes to
    /// (<c>AgentPrismSchedulingOptions.Lanes</c>).
    /// </summary>
    public string Lane { get; init; } = JobLanes.Default;

    /// <summary>The agent or workflow name to run.</summary>
    public required string TargetName { get; init; }

    /// <summary>The job's current status.</summary>
    public required JobStatus Status { get; init; }

    /// <summary>The input set or parameters.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>The total number of items.</summary>
    public int TotalItems { get; init; }

    /// <summary>The number of items that completed successfully.</summary>
    public int DoneItems { get; init; }

    /// <summary>The number of items that failed.</summary>
    public int FailedItems { get; init; }

    /// <summary>The number of lease attempts. Increments on every <see cref="IJobStore.LeaseAsync"/> call.</summary>
    public int Attempt { get; init; }

    /// <summary>
    /// The maximum number of attempts specific to this job. If
    /// <see langword="null"/>, <c>AgentPrismSchedulingOptions.MaxAttempts</c> applies.
    /// </summary>
    /// <remarks>
    /// Webhook delivery uses a ladder different from the global
    /// setting; this field prevents a single global number from being forced
    /// onto every kind of job.
    /// </remarks>
    public int? MaxAttempts { get; init; }

    /// <summary>The identifier of the worker currently leasing the job. <see langword="null"/> if not leased.</summary>
    public string? LeaseOwner { get; init; }

    /// <summary>The time the current lease expires (UTC). The job may be re-leased once it expires.</summary>
    public DateTimeOffset? LeaseUntil { get; init; }

    /// <summary>The earliest time the job is eligible to run (UTC).</summary>
    public required DateTimeOffset ScheduledFor { get; init; }

    /// <summary>The time the first lease happened (UTC).</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>The completion time (UTC). <see langword="null"/> while the job is in progress.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The failure message. Populated only for <see cref="JobStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
