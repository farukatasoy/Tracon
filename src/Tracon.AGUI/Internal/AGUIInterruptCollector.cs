using System.Text.Json;
using AGUI.Abstractions;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Turns what a Tracon run waits for — tool approvals and requests for user input — into AG-UI interrupts.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the stream is rewritten instead of mapped.</strong> The AG-UI event converter builds the interrupts of
/// <c>RUN_FINISHED</c> from the content it is fed, and it reads each interrupt's text the moment the content passes. Tracon
/// knows how to present an approval (the entity it acts on, the text the tool gave) only when the run is about to end, so a
/// <see cref="ToolApprovalRequestContent"/> cannot be handed over as it arrives. It is replaced by the tool call it asks
/// about — the front end sees the call — and the interrupt is handed over after the last update, as an
/// <see cref="InterruptRequestContent"/> the converter writes as it is.
/// </para>
/// <para>
/// The interrupt of an approval has the <c>tool_call</c> reason, the one the protocol defines for "the agent wants to call
/// a tool and waits for a human", and it asks for <c>{ approved, reason?, remember?, rememberArgumentsOnly? }</c>; a front
/// end that answers <c>{ approved }</c> alone, as the reference implementation's is written to, is understood. The interrupt
/// of an MCP server's request has the <c>input_required</c> reason and the server's own flat schema.
/// </para>
/// </remarks>
internal sealed class AGUIInterruptCollector
{
    private static readonly JsonElement ApprovalSchema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {
            "approved": { "type": "boolean" },
            "reason": { "type": "string" },
            "remember": { "type": "boolean" },
            "rememberArgumentsOnly": { "type": "boolean" }
          },
          "required": ["approved"]
        }
        """).RootElement.Clone();

    private readonly List<ToolApprovalRequestContent> _requests = [];
    private IReadOnlyList<PendingApprovalAnnouncement> _approvals = [];
    private IReadOnlyList<PendingToolInputAnnouncement> _inputs = [];

    /// <summary>Receives the presentation of the approvals the run waits for, right before the run ends.</summary>
    /// <param name="announcements">The approvals.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public ValueTask OnApprovalsAsync(IReadOnlyList<PendingApprovalAnnouncement> announcements, CancellationToken cancellationToken)
    {
        _approvals = announcements;

        return ValueTask.CompletedTask;
    }

    /// <summary>Receives the requests for user input the run waits for, right before the run ends.</summary>
    /// <param name="announcements">The requests.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A completed task.</returns>
    public ValueTask OnInputsAsync(IReadOnlyList<PendingToolInputAnnouncement> announcements, CancellationToken cancellationToken)
    {
        _inputs = announcements;

        return ValueTask.CompletedTask;
    }

    /// <summary>Rewrites an update before the AG-UI converter sees it.</summary>
    /// <param name="update">The update.</param>
    /// <returns>The update itself when nothing in it changes, otherwise a copy that does.</returns>
    /// <remarks>
    /// <para>
    /// The update is NEVER changed in place. The agent builds the session's history from the very same update objects it
    /// hands to the caller, so an edit here would be written into the session: the approval request would be missing from the
    /// history that has to answer it, and the suspended call's recorded request would read as a plain sentence. The copy is
    /// shallow; only its contents list is new.
    /// </para>
    /// <para>
    /// A tool approval request becomes the tool call it is about, and is remembered for the interrupt.
    /// </para>
    /// <para>
    /// The result of a suspended tool call is the request Tracon recorded for the session (its replay identity and
    /// fingerprint), not an answer for the model or the front end. It is replaced by a plain statement; the request itself
    /// travels as the interrupt.
    /// </para>
    /// </remarks>
    public ChatResponseUpdate Adapt(ChatResponseUpdate update)
    {
        if (!update.Contents.Any(static content => content is ToolApprovalRequestContent ||
                                                    (content is FunctionResultContent result && ToolInputResolver.ReadPayload(result.Result) is not null)))
        {
            return update;
        }

        var contents = new List<AIContent>(update.Contents.Count);

        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case ToolApprovalRequestContent request:
                    if (!_requests.Exists(known => string.Equals(known.RequestId, request.RequestId, StringComparison.Ordinal)))
                    {
                        _requests.Add(request);
                    }

                    contents.Add(request.ToolCall);

                    break;

                case FunctionResultContent result when ToolInputResolver.ReadPayload(result.Result) is not null:
                    contents.Add(new FunctionResultContent(result.CallId, "The tool is waiting for the user's input."));

                    break;

                default:
                    contents.Add(content);

                    break;
            }
        }

        var adapted = update.Clone();
        adapted.Contents = contents;

        return adapted;
    }

    /// <summary>
    /// Builds the update that carries the interrupts, after the last update of the run.
    /// </summary>
    /// <param name="runId">The Tracon run, which names the message.</param>
    /// <returns>The update; <see langword="null"/> when the run waits for nothing.</returns>
    public ChatResponseUpdate? BuildInterrupts(Guid runId)
    {
        var interrupts = new List<AIContent>();
        var announced = _approvals.ToDictionary(static approval => approval.RequestId, StringComparer.Ordinal);

        foreach (var request in _requests)
        {
            announced.TryGetValue(request.RequestId, out var announcement);
            interrupts.Add(ApprovalInterrupt(request.RequestId, announcement, request.ToolCall));
            announced.Remove(request.RequestId);
        }

        // An approval a child agent raised is announced without having passed this stream.
        foreach (var announcement in announced.Values)
        {
            interrupts.Add(ApprovalInterrupt(announcement.RequestId, announcement, toolCall: null));
        }

        foreach (var input in _inputs)
        {
            interrupts.Add(new InterruptRequestContent(input.RequestId)
            {
                Reason = InterruptReasons.InputRequired,
                Message = input.Message,
                ResponseSchema = input.RequestedSchema.ValueKind == JsonValueKind.Undefined ? null : input.RequestedSchema,
                Metadata = JsonSerializer.SerializeToElement(new { toolName = input.ToolName, serverName = input.ServerName }),
            });
        }

        return interrupts.Count == 0
            ? null
            : new ChatResponseUpdate(ChatRole.Assistant, interrupts)
            {
                MessageId = $"interrupt-{runId:N}",
            };
    }

    private static InterruptRequestContent ApprovalInterrupt(
        string requestId,
        PendingApprovalAnnouncement? announcement,
        ToolCallContent? toolCall)
    {
        var toolName = announcement?.ToolName ?? (toolCall as FunctionCallContent)?.Name ?? toolCall?.CallId ?? "unknown";

        return new InterruptRequestContent(requestId)
        {
            Reason = InterruptReasons.ToolCall,
            Message = announcement?.Message ?? $"Approval required for tool call: {toolName}",
            ToolCallId = toolCall?.CallId,
            ResponseSchema = ApprovalSchema,
            Metadata = announcement is { EntityType: not null } or { EntityId: not null } or { EntityName: not null }
                ? JsonSerializer.SerializeToElement(new
                {
                    toolName,
                    entityType = announcement.EntityType,
                    entityId = announcement.EntityId,
                    entityName = announcement.EntityName,
                })
                : JsonSerializer.SerializeToElement(new { toolName }),
        };
    }
}
