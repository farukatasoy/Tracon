using System.ComponentModel;
using System.Text.Json;

namespace Tracon;

/// <remarks>
/// The name comes from the <em>path</em>, not the body — same rationale as
/// <see cref="WorkflowSaveRequest"/>.
/// </remarks>
[Description("Request to create/update a schedule.")]
internal sealed record JobScheduleSaveRequest
{
    /// <remarks>
    /// Only a key listed in
    /// <c>TraconSchedulingOptions.HttpSchedulableHandlerKeys</c> is
    /// accepted; left empty, that list means Tracon's own built-in keys.
    /// Making a consumer's key schedulable over HTTP is a deliberate opt-in.
    /// </remarks>
    [Description(
        "Key of the handler the jobs this schedule produces run on. See\n" +
        "`JobHandlerKeys`.")]
    public required string HandlerKey { get; init; }

    [Description("Name of the agent or workflow to run.")]
    public required string TargetName { get; init; }

    [Description(
        "Five-field cron expression. If left empty, the schedule can only be\n" +
        "triggered manually (`POST .../trigger`).")]
    public string? Cron { get; init; }

    [Description("Time zone the `Cron` expression is interpreted in.")]
    public string TimeZone { get; init; } = "UTC";

    [Description(
        "The lane the jobs this schedule produces run in. See\n" +
        "`JobLanes`. Left empty, the jobs run in `JobLanes.Default`\n" +
        "(or whatever `TraconSchedulingOptions.LaneByHandlerKey` maps\n" +
        "HandlerKey to).")]
    public string? Lane { get; init; }

    [Description("Input set or parameters.")]
    public JsonElement Payload { get; init; }

    [Description("Whether the schedule is enabled.")]
    public bool Enabled { get; init; } = true;
}

[Description("Request to trigger a schedule immediately.")]
internal sealed record JobTriggerRequest
{
    [Description(
        "Payload to use for this run. If left empty, the schedule's own payload\n" +
        "is used.")]
    public JsonElement? Payload { get; init; }
}

[Description("Detailed view of a single job: record and items together.")]
internal sealed record JobDetailResponse
{
    [Description("Job record.")]
    public required JobRecord Job { get; init; }

    [Description("The job's items, in sequence order.")]
    public required IReadOnlyList<JobItemRecord> Items { get; init; }
}
