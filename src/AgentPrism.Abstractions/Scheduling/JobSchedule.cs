using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// The schedule record defining when and how a job runs.
/// </summary>
/// <remarks>
/// If <see cref="Cron"/> is left empty, the schedule is triggered only
/// manually (<c>POST .../trigger</c>); no automatic next-run time is computed.
/// </remarks>
public sealed record JobSchedule
{
    /// <summary>The schedule identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>The tenant the schedule belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>The schedule name, unique within the tenant.</summary>
    public required string Name { get; init; }

    /// <summary>The kind of job this schedule produces.</summary>
    public required JobKind Kind { get; init; }

    /// <summary>The agent or workflow name to run.</summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// The five-field cron expression (<c>minute hour day-of-month month
    /// day-of-week</c>). If <see langword="null"/>, the schedule is triggered only manually.
    /// </summary>
    public string? Cron { get; init; }

    /// <summary>The time zone the <see cref="Cron"/> expression is interpreted in.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>The input set or parameters. Interpreted according to the job kind.</summary>
    public JsonElement Payload { get; init; }

    /// <summary>Whether the schedule is enabled. If disabled, it is not triggered automatically.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The next automatic run time (UTC). <see langword="null"/> if there is no cron.</summary>
    public DateTimeOffset? NextRunAt { get; init; }

    /// <summary>The last run time (UTC). <see langword="null"/> if it never ran.</summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>The identifier of the user/service that created the schedule.</summary>
    public string? CreatedBy { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
