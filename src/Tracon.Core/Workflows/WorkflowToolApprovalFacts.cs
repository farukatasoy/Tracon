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

    /// <summary>
    /// The name recorded for a call that is not a function call, so its tool has no name. The approval inbox
    /// records the same name, and no rule is saved for it.
    /// </summary>
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
            : new WorkflowToolApprovalFacts(UnknownTool, agentName, ArgumentsHash: null);
    }

    /// <summary>The payload member that holds the request's form.</summary>
    private const string FormMember = "form";

    /// <summary>The payload member that holds the type of the request's data.</summary>
    private const string RequestTypeMember = "requestType";

    /// <summary>The form of a request that waits for a tool approval, as the payload writes it.</summary>
    private const string ToolApprovalForm = "ToolApproval";

    /// <summary>Reads the facts from a <see cref="RunEventType.WorkflowRequest"/> event's payload.</summary>
    /// <param name="payload">The event's payload.</param>
    /// <returns>The facts; <see langword="null"/> when the payload names no tool (see <see cref="Read"/>).</returns>
    public static WorkflowToolApprovalFacts? TryRead(string? payload) => Read(payload).Facts;

    /// <summary>
    /// Reads a request's payload far enough to tell whether it is a tool approval and, if so, what it names.
    /// </summary>
    /// <param name="payload">The event's payload; it may have been cut at the length limit.</param>
    /// <returns>
    /// Whether the request is a tool approval, and the facts when the tool is named. A tool approval whose
    /// facts are <see langword="null"/> was cut before the tool: nothing can be audited for it, and a caller
    /// refuses the answer rather than apply it unrecorded.
    /// </returns>
    /// <remarks>
    /// The writer cuts a payload at <c>MaxPayloadLength</c> and appends a marker, so the document may end inside a
    /// string. The facts come before the prompt, so a cut costs the prompt first. The scan keeps a member only when
    /// its whole value was read: a cut inside a name leaves the name unread, never half of it - a rule saved
    /// under half an agent name would never match.
    /// </remarks>
    public static (bool IsToolApproval, WorkflowToolApprovalFacts? Facts) Read(string? payload)
    {
        if (payload is not { Length: > 0 })
        {
            return (false, null);
        }

        string? form = null;
        string? requestType = null;
        string? tool = null;
        string? agent = null;
        string? hash = null;

        var reader = new Utf8JsonReader(System.Text.Encoding.UTF8.GetBytes(payload), isFinalBlock: false, state: default);
        string? member = null;

        try
        {
            while (reader.Read())
            {
                if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.PropertyName)
                {
                    member = reader.GetString();

                    continue;
                }

                if (reader.CurrentDepth == 1 && reader.TokenType == JsonTokenType.String)
                {
                    switch (member)
                    {
                        case FormMember:
                            form = reader.GetString();
                            break;
                        case RequestTypeMember:
                            requestType = reader.GetString();
                            break;
                        case ToolNameMember:
                            tool = reader.GetString();
                            break;
                        case AgentNameMember:
                            agent = reader.GetString();
                            break;
                        case ArgumentsHashMember:
                            hash = reader.GetString();
                            break;
                        default:
                            break;
                    }
                }

                member = null;
            }
        }
        catch (JsonException)
        {
            // The cut can land between tokens; what was read so far stands.
        }

        // The request type comes first: a cut that loses the form and the tool still shows what the request is.
        var isToolApproval = string.Equals(form, ToolApprovalForm, StringComparison.Ordinal) ||
                             tool is { Length: > 0 } ||
                             requestType?.Contains(nameof(ToolApprovalRequestContent), StringComparison.Ordinal) == true;

        return (isToolApproval, tool is { Length: > 0 } ? new WorkflowToolApprovalFacts(tool, agent, hash) : null);
    }

}
