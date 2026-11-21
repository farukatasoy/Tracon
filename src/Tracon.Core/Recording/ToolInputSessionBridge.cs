using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Hands a suspended tool call to Microsoft Agent Framework as a pending request, so its function-invocation
/// loop runs the call again on the turn that answers it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why it exists.</strong> The loop does run a <c>ToolApprovalResponseContent</c>'s call without a
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
/// It is written when the run CLOSES, not when the tool suspends. The binding client removes the session's
/// whole pending set after an inner call that began with one, and an approval-required MCP server (the default)
/// always begins with one: a write made from inside the tool call would be erased on the very turn it was made.
/// </para>
/// <para>
/// The key and the shape (<c>List&lt;ToolApprovalRequestContent&gt;</c>) belong to the framework and are not part
/// of its public surface. <c>McpElicitationTests</c> runs the whole turn against a real server and is what fails
/// when an upgrade of the framework moves them.
/// </para>
/// </remarks>
internal static class ToolInputSessionBridge
{
    /// <summary>The session state key the framework's approval binding keeps its pending requests under.</summary>
    internal const string PendingApprovalRequestsKey = "_pendingApprovalRequests";

    /// <summary>The session state key Tracon keeps the calls that were made and never got a result under.</summary>
    internal const string SkippedCallsKey = "Tracon.ToolInput.SkippedCalls";

    /// <summary>What a call that was made and never answered tells the model.</summary>
    internal const string SkippedCallResult = "The call was not answered.";

    /// <summary>Records the suspended calls as pending requests of the session.</summary>
    /// <param name="session">The run's session; a run without one cannot be answered and records nothing.</param>
    /// <param name="pending">The requests that suspended a call of the run.</param>
    /// <param name="skippedCalls">
    /// The calls of the run that never ran because the loop stopped at the suspended one; the next run of the
    /// session gives each a result (<see cref="CloseSkippedCalls"/>).
    /// </param>
    public static void Record(AgentSession? session, IReadOnlyList<PendingToolInput> pending, IReadOnlyList<string> skippedCalls)
    {
        if (session is null || pending.Count == 0)
        {
            return;
        }

        RecordUnansweredCalls(session, skippedCalls);

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

    /// <summary>
    /// Leaves the calls a finished run made and never got a result for with the session, so the next run closes them.
    /// </summary>
    /// <param name="session">The run's session; a run without one records nothing.</param>
    /// <param name="calls">The identities of the calls with no result.</param>
    /// <remarks>
    /// Calls to a client-side tool end every turn that asks for one this way: the model's answer holds the call and
    /// the caller runs it. The caller answers with the result, or does not, and types a new message instead; a
    /// provider refuses a history that holds a call without a result in either case, so the next run settles it
    /// (<see cref="CloseSkippedCalls"/>). The calls are merged with the ones an earlier run left.
    /// </remarks>
    public static void RecordUnansweredCalls(AgentSession? session, IReadOnlyList<string> calls)
    {
        if (session is null || calls.Count == 0)
        {
            return;
        }

        var known = session.StateBag.TryGetValue<List<string>>(SkippedCallsKey, out var earlier, TraconCoreJsonContext.Default.Options) &&
                    earlier is not null
            ? earlier
            : [];

        known.AddRange(calls.Where(id => !known.Contains(id, StringComparer.Ordinal)));
        session.StateBag.SetValue(SkippedCallsKey, known, TraconCoreJsonContext.Default.Options);
    }

    /// <summary>
    /// Takes the calls an earlier run left without a result and builds the message that gives each one,
    /// except the ones this run answers.
    /// </summary>
    /// <param name="session">The run's session.</param>
    /// <param name="incoming">The messages the run starts with.</param>
    /// <returns>The tool message to put before the run's own messages; <see langword="null"/> when no call is waiting.</returns>
    /// <remarks>
    /// <para>
    /// Every run of the session does this, whatever started it: the user may answer the form, ignore it and type a
    /// message, send the result of a client-side tool, or approve a queued call first. A provider refuses a history
    /// that holds a call without a result in every one of those cases. The marker is taken, not copied: a run that
    /// fails does not save the session, so the marker stays with the stored session and the next run closes the calls.
    /// </para>
    /// <para>
    /// A call the incoming messages carry a result for is answered by this run and is left alone: closing it would give
    /// it a second result, and the provider refuses that as well. Nothing is marked as answered ahead of the caller: a
    /// result counts only when it is in the messages the run was given.
    /// </para>
    /// </remarks>
    public static ChatMessage? CloseSkippedCalls(AgentSession? session, IReadOnlyList<ChatMessage> incoming)
    {
        if (session is null ||
            !session.StateBag.TryGetValue<List<string>>(SkippedCallsKey, out var skipped, TraconCoreJsonContext.Default.Options) ||
            skipped is not { Count: > 0 })
        {
            return null;
        }

        session.StateBag.TryRemoveValue(SkippedCallsKey);

        var answered = new HashSet<string>(
            incoming.SelectMany(static message => message.Contents).OfType<FunctionResultContent>().Select(static result => result.CallId),
            StringComparer.Ordinal);

        var open = skipped.Where(id => !answered.Contains(id)).Distinct(StringComparer.Ordinal).ToList();

        return open.Count == 0
            ? null
            : new ChatMessage(ChatRole.Tool, [.. open.Select(static id => (AIContent)new FunctionResultContent(id, SkippedCallResult))]);
    }
}
