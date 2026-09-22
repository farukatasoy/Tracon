using System.Text.Json;

namespace Tracon;

/// <summary>
/// The schedule record defining when and how a job runs.
/// </summary>
/// <remarks>
/// If <c>Cron</c> is left empty, the schedule is triggered only
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

    /// <summary>
    /// The key of the <see cref="IJobHandler"/> the jobs this schedule
    /// produces are dispatched to. See <see cref="JobHandlerKeys"/>.
    /// </summary>
    public required string HandlerKey { get; init; }

    /// <summary>
    /// The lane the jobs this schedule produces run in. See
    /// <see cref="JobLanes"/>. Both a cron-dispatched run and a manual
    /// <c>POST .../trigger</c> inherit this value.
    /// </summary>
    public string Lane { get; init; } = JobLanes.Default;

    /// <summary>The agent or workflow name to run.</summary>
    public required string TargetName { get; init; }

    /// <summary>
    /// The five-field cron expression (<c>minute hour day-of-month month
    /// day-of-week</c>). If <see langword="null"/>, the schedule is triggered only manually.
    /// </summary>
    public string? Cron { get; init; }

    /// <summary>The time zone the <c>Cron</c> expression is interpreted in.</summary>
    public string TimeZone { get; init; } = "UTC";

    /// <summary>The input set or parameters. Interpreted by the job's handler.</summary>
    /// <remarks>
    /// An unset payload is stored as the empty JSON array, never as
    /// <see cref="JsonValueKind.Undefined"/> or JSON <c>null</c>: an undefined
    /// value cannot be written as JSON, and SQL Server rejects a <c>null</c>
    /// literal in a JSON column. Every reader treats the empty array as "no payload".
    /// </remarks>
    public JsonElement Payload
    {
        get;
        init => field = FreeFormJson.OrEmpty(value);
    }

    /// <summary>Whether the schedule is enabled. If disabled, it is not triggered automatically.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The next automatic run time (UTC). <see langword="null"/> if there is no cron.</summary>
    /// <remarks>
    /// The worker picks a due schedule by this value and computes every later
    /// one itself. A schedule written straight to <see cref="IJobScheduleStore"/>
    /// with a cron expression must therefore carry its first run time here: saved
    /// with <see langword="null"/>, it never runs. A time at or before now runs it on
    /// the worker's next pass. The scheduling API sets it for you.
    /// </remarks>
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
