using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// One entry of the <see cref="RunEventType.RunAwaitingInput"/> event's <c>Payload</c>,
/// written when the run closed with <see cref="RunStatus.AwaitingApproval"/>.
/// </summary>
internal sealed record PendingToolApprovalEventItem
{
    /// <summary>Gets the <c>ToolApprovalRequestContent.RequestId</c> value.</summary>
    public required string RequestId { get; init; }

    /// <summary>Gets the name of the tool that asks for approval.</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the entity type an <see cref="IToolApprovalPresenter"/> resolved, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityType { get; init; }

    /// <summary>Gets the entity id an <see cref="IToolApprovalPresenter"/> resolved, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityId { get; init; }

    /// <summary>Gets the entity name an <see cref="IToolApprovalPresenter"/> resolved, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EntityName { get; init; }

    /// <summary>Gets the free-form message an <see cref="IToolApprovalPresenter"/> resolved, if any.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }
}
