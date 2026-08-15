namespace AgentPrism;

/// <summary>
/// A single execution record and summary of an <see cref="EvalSuite"/>.
/// </summary>
/// <remarks>
/// <see cref="AgentVersion"/> and <see cref="ModelId"/> are critical: regression
/// tracking compares the same suite's result across different versions. Both
/// are filled in when the run actually starts (on the transition to
/// <see cref="EvalRunStatus.Running"/>) — not at trigger time, because the
/// agent definition can change while the job waits in the queue.
/// </remarks>
public sealed record EvalRun
{
    /// <summary>The run identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>The tenant the run belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The identifier of the suite being measured.</summary>
    public required Guid SuiteId { get; init; }

    /// <summary>
    /// The identifier of the job record responsible for executing this run. The
    /// run is executed through the job queue (<see cref="IJobStore"/>).
    /// </summary>
    public Guid? JobId { get; init; }

    /// <summary>The agent definition version being measured. <see langword="null"/> before the run starts.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>The model identifier being measured. <see langword="null"/> before the run starts.</summary>
    public string? ModelId { get; init; }

    /// <summary>The current status of the run.</summary>
    public required EvalRunStatus Status { get; init; }

    /// <summary>The total number of cases.</summary>
    public int Total { get; init; }

    /// <summary>The number of cases that passed.</summary>
    public int Passed { get; init; }

    /// <summary>The number of remaining (failed) cases.</summary>
    public int Failed { get; init; }

    /// <summary>The total input token count.</summary>
    public long? InputTokens { get; init; }

    /// <summary>The total output token count.</summary>
    public long? OutputTokens { get; init; }

    /// <summary>The start time (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>The completion time (UTC). <see langword="null"/> while the run is in progress.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
}
