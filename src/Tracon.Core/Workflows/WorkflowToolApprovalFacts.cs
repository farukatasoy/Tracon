using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// What a workflow's pending tool approval records about the call, beside the request's id: the
/// tool, the agent that asked, and a hash of the arguments.
/// </summary>
/// <param name="ToolName">The tool that waits for approval.</param>
/// <param name="AgentName">The agent that called the tool; <see langword="null"/> when the request does not tell.</param>
/// <param name="ArgumentsHash">
/// The hash of the call's arguments (<see cref="ToolApprovalRuleEvaluator.ComputeArgumentsHash"/>), never the
/// arguments; <see langword="null"/> when the call is not a function call.
/// </param>
/// <remarks>
/// <para>
/// The engine writes these three members into the <see cref="RunEventType.WorkflowRequest"/> event's payload
/// when the request is published, and the respond endpoint reads them back to write the audit row and the
/// "do not ask again" rule. The two sides share this type so the member names cannot drift apart. The
/// arguments themselves are not written: the audit trail and the event stream are not protected at rest.
/// </para>
/// <para>
/// The answer is built from the request the checkpoint holds, not from these members: they only name the
/// call for the record.
/// </para>
/// </remarks>
internal sealed record WorkflowToolApprovalFacts(string ToolName, string? AgentName, string? ArgumentsHash)
{
    /// <summary>The payload member that holds <see cref="ToolName"/>.</summary>
    public const string ToolNameMember = "toolName";

    /// <summary>The payload member that holds <see cref="AgentName"/>.</summary>
    public const string AgentNameMember = "agentName";

    /// <summary>The payload member that holds <see cref="ArgumentsHash"/>.</summary>
    public const string ArgumentsHashMember = "toolArgumentsHash";

    /// <summary>The name recorded for a call whose tool name the request does not carry.</summary>
    public const string UnknownTool = "unknown";

    /// <summary>Describes the call a request waits for.</summary>
    /// <param name="request">The request.</param>
    /// <param name="agentName">The agent that asked, when known.</param>
    /// <returns>The facts.</returns>
    public static WorkflowToolApprovalFacts Of(ToolApprovalRequestContent request, string? agentName)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ToolCall is FunctionCallContent call
            ? new WorkflowToolApprovalFacts(
                call.Name,
                agentName,
                ToolApprovalRuleEvaluator.ComputeArgumentsHash(call.Arguments))
            : new WorkflowToolApprovalFacts(request.ToolCall.CallId, agentName, ArgumentsHash: null);
    }

    /// <summary>Reads the facts from a <see cref="RunEventType.WorkflowRequest"/> event's payload.</summary>
    /// <param name="payload">The event's payload.</param>
    /// <returns>
    /// The facts; <see langword="null"/> when the payload does not parse or names no tool. A payload the
    /// writer cut short is one that does not parse: the facts come before the prompt, so a cut costs the
    /// prompt first, but the reader does not guess at a broken document.
    /// </returns>
    public static WorkflowToolApprovalFacts? TryRead(string? payload)
    {
        if (payload is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(ToolNameMember, out var tool) ||
                tool.ValueKind != JsonValueKind.String ||
                tool.GetString() is not { Length: > 0 } toolName)
            {
                return null;
            }

            return new WorkflowToolApprovalFacts(
                toolName,
                ReadString(document.RootElement, AgentNameMember),
                ReadString(document.RootElement, ArgumentsHashMember));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string member)
        => element.TryGetProperty(member, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
