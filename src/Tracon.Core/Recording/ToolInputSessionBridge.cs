using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Hands a suspended tool call to Microsoft Agent Framework as a pending request, so its function-invocation
/// loop runs the call again on the turn that answers it.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Measured, Phase 235.</strong> The loop does run a <c>ToolApprovalResponseContent</c>'s call without a
/// request in the messages, but <c>ApprovalResponseBindingChatClient</c> sits above it and drops every response
/// that does not match a request the framework recorded in the session itself: a caller must not be able to
/// authorize a call by sending its own. A response for a call Tracon replays is exactly such a response, so the
/// plan's first mechanism (a synthetic approval response) was silently ignored and the tool never ran again.
/// </para>
/// <para>
/// The way through is the framework's own contract: "a host that cannot record approval requests server-side
/// cannot resume an approval". Tracon records the replay as a pending request, server-side, in the place the
/// framework reads it from. The caller still cannot authorize anything: it can only answer a request id Tracon
/// minted, for a call that is the suspended call with its own arguments.
/// </para>
/// <para>
/// 🚨 It is written when the run CLOSES, not when the tool suspends. The binding client removes the session's
/// whole pending set after an inner call that began with one, and an approval-required MCP server (the default)
/// always begins with one: a write made from inside the tool call would be erased on the very turn it was made.
/// </para>
/// <para>
/// The key and the shape (<c>List&lt;ToolApprovalRequestContent&gt;</c>) belong to the framework and are not part
/// of its public surface. <c>McpElicitationTests</c> runs the whole turn against a real server and is what fails
/// when an upgrade moves them; the upgrade procedure (<c>maf-api-kesfi</c>) names it.
/// </para>
/// </remarks>
internal static class ToolInputSessionBridge
{
    /// <summary>The session state key the framework's approval binding keeps its pending requests under.</summary>
    internal const string PendingApprovalRequestsKey = "_pendingApprovalRequests";

    /// <summary>Records the suspended calls as pending requests of the session.</summary>
    /// <param name="session">The run's session; a run without one cannot be answered and records nothing.</param>
    /// <param name="pending">The requests that suspended a call of the run.</param>
    public static void Record(AgentSession? session, IReadOnlyList<PendingToolInput> pending)
    {
        if (session is null || pending.Count == 0)
        {
            return;
        }

        var options = AIJsonUtilities.DefaultOptions;

        var requests = session.StateBag.TryGetValue<List<ToolApprovalRequestContent>>(PendingApprovalRequestsKey, out var existing, options) &&
                       existing is not null
            ? existing
            : [];

        foreach (var input in pending)
        {
            requests.Add(new ToolApprovalRequestContent(input.RequestId, input.ReplayCall));
        }

        session.StateBag.SetValue(PendingApprovalRequestsKey, requests, options);
    }
}
