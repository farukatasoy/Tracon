using System.Text.Json;

namespace AgentPrism;

/// <summary>Request to create/update a schedule.</summary>
/// <remarks>
/// The name comes from the <em>path</em>, not the body — same rationale as
/// <see cref="WorkflowSaveRequest"/>.
/// </remarks>
public sealed record JobScheduleSaveRequest
{
    /// <summary>Kind of job this schedule produces.</summary>
    public required JobKind Kind { get; init; }

    /// <summary>Name of the agent or workflow to run.</summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// Five-field cron expression. If left empty, the schedule can only be
    /// triggered manually (<c>POST .../trigger</c>).
    /// </summary>
    public string? Cron { get; init; }

    /// <summary>Time zone the <c>Cron</c> expression is interpreted in.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>
    /// The lane the jobs this schedule produces run in. See
    /// <c>JobLanes</c>. Left empty, the jobs run in <c>JobLanes.Default</c>
    /// (or whatever <c>AgentPrismSchedulingOptions.LaneByKind</c> maps
    /// <see cref="Kind"/> to).
    /// </summary>
    public string? Lane { get; init; }

    /// <summary>Input set or parameters.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Whether the schedule is enabled.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Request to trigger a schedule immediately.</summary>
public sealed record JobTriggerRequest
{
    /// <summary>
    /// Payload to use for this run. If left empty, the schedule's own payload
    /// is used.
    /// </summary>
    public JsonElement? Payload { get; init; }
}

/// <summary>Detailed view of a single job: record and items together.</summary>
public sealed record JobDetailResponse
{
    /// <summary>Job record.</summary>
    public required JobRecord Job { get; init; }

    /// <summary>The job's items, in sequence order.</summary>
    public required IReadOnlyList<JobItemRecord> Items { get; init; }
}
