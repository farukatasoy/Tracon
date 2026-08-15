using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>The JSON body of a webhook request.</summary>
/// <remarks>
/// <para>
/// 🚨 This payload carries only a <strong>summary</strong> (K-161):
/// identifier, status, agent, tokens, cost. Message content and model
/// responses <strong>never</strong> sit here. A recipient that needs content
/// calls <c>GET {prefix}/api/runs/{id}</c>.
/// </para>
/// <para>
/// The reason has two layers: (1) the payload goes to an external system and
/// must not carry sensitive data; (2) the same text is also stored in the
/// <c>webhook_deliveries.payload</c> column — if it carried content, the
/// database backup would carry sensitive data too.
/// </para>
/// </remarks>
public sealed record WebhookEventPayload
{
    /// <summary>The event name. Filled in by the publisher.</summary>
    public string? Event { get; init; }

    /// <summary>The delivery identifier. Filled in by the publisher.</summary>
    public string? DeliveryId { get; init; }

    /// <summary>The tenant identifier. Filled in by the publisher.</summary>
    public string? TenantId { get; init; }

    /// <summary>The moment the event occurred (UTC). Filled in by the publisher.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>The run summary. Populated on <c>run.*</c> events.</summary>
    public WebhookRunSummary? Run { get; init; }

    /// <summary>The job summary. Populated on <c>job.*</c> events.</summary>
    public WebhookJobSummary? Job { get; init; }

    /// <summary>The approval summary. Populated on <c>approval.pending</c> and <c>workflow.request.pending</c> events.</summary>
    public WebhookApprovalSummary? Approval { get; init; }

    /// <summary>The quota summary. Populated on the <c>quota.threshold</c> event.</summary>
    public WebhookQuotaSummary? Quota { get; init; }

    /// <summary>The score window summary. Populated on the <c>run.score.low</c> event.</summary>
    public WebhookScoreSummary? Score { get; init; }
}

/// <summary>A run's webhook summary.</summary>
public sealed record WebhookRunSummary
{
    /// <summary>The run identifier.</summary>
    public required string RunId { get; init; }

    /// <summary>The tree root's identifier. Equal to itself for the root run.</summary>
    public string? RootRunId { get; init; }

    /// <summary>The session identifier.</summary>
    public string? SessionId { get; init; }

    /// <summary>The name of the agent that ran.</summary>
    public string? AgentName { get; init; }

    /// <summary>The identifier of the model used.</summary>
    public string? ModelId { get; init; }

    /// <summary>The final status.</summary>
    public string? Status { get; init; }

    /// <summary>The duration (milliseconds).</summary>
    public long? DurationMs { get; init; }

    /// <summary>The input tokens.</summary>
    public long? InputTokens { get; init; }

    /// <summary>The output tokens.</summary>
    public long? OutputTokens { get; init; }

    /// <summary>The total amount. <see langword="null"/> if pricing is undefined — not zero.</summary>
    public decimal? Cost { get; init; }

    /// <summary>The amount's currency.</summary>
    public string? Currency { get; init; }

    /// <summary>The error message. Populated on the <c>run.failed</c> event.</summary>
    public string? Error { get; init; }
}

/// <summary>A job's webhook summary.</summary>
public sealed record WebhookJobSummary
{
    /// <summary>The job identifier.</summary>
    public required string JobId { get; init; }

    /// <summary>The job's kind.</summary>
    public string? Kind { get; init; }

    /// <summary>The agent or workflow name to run.</summary>
    public string? TargetName { get; init; }

    /// <summary>The final status.</summary>
    public string? Status { get; init; }

    /// <summary>The total number of items.</summary>
    public int TotalItems { get; init; }

    /// <summary>The number of completed items.</summary>
    public int DoneItems { get; init; }

    /// <summary>The number of failed items.</summary>
    public int FailedItems { get; init; }

    /// <summary>The error message.</summary>
    public string? Error { get; init; }
}

/// <summary>A pending approval or human-input request's webhook summary.</summary>
public sealed record WebhookApprovalSummary
{
    /// <summary>The approval or request identifier.</summary>
    public required string RequestId { get; init; }

    /// <summary>The associated run identifier.</summary>
    public string? RunId { get; init; }

    /// <summary>The name of the tool awaiting approval. Populated for tool approval.</summary>
    public string? ToolName { get; init; }

    /// <summary>The name of the associated workflow. Populated for a human-input request.</summary>
    public string? WorkflowName { get; init; }

    /// <summary>The request's user-facing text.</summary>
    public string? Prompt { get; init; }
}

/// <summary>An exceeded quota threshold's webhook summary.</summary>
public sealed record WebhookQuotaSummary
{
    /// <summary>The exceeded metric.</summary>
    public required QuotaMetric Metric { get; init; }

    /// <summary>The agent the rule is bound to. <see langword="null"/> for tenant-wide.</summary>
    public string? AgentName { get; init; }

    /// <summary>The counter's interval.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>The exceeded threshold percentage: 80 or 100.</summary>
    public required int ThresholdPercent { get; init; }

    /// <summary>The defined limit.</summary>
    public required decimal Limit { get; init; }

    /// <summary>The current consumption.</summary>
    public required decimal Used { get; init; }

    /// <summary>The time the counter resets (UTC).</summary>
    public DateTimeOffset? ResetsAt { get; init; }
}

/// <summary>A score window threshold's webhook summary (Phase 49).</summary>
public sealed record WebhookScoreSummary
{
    /// <summary>The window's average score (0-100).</summary>
    public required double AverageScore { get; init; }

    /// <summary>The number of samples (scored runs) in the window.</summary>
    public required long SampleCount { get; init; }

    /// <summary>The exceeded low-score threshold.</summary>
    public required int Threshold { get; init; }

    /// <summary>The window's start (UTC).</summary>
    public DateTimeOffset? WindowStart { get; init; }

    /// <summary>The window's end (UTC).</summary>
    public DateTimeOffset? WindowEnd { get; init; }
}

/// <summary>
/// The source-generated JSON context for <see cref="WebhookEventPayload"/> and its subtypes.
/// </summary>
/// <remarks>
/// Required for AOT compatibility: the webhook body does not use
/// reflection-based serialization.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebhookEventPayload))]
public partial class WebhookEventPayloadJsonContext : JsonSerializerContext;
