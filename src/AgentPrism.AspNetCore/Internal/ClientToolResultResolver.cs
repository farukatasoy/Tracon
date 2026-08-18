using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Matches client-side tool results from the UI against pending
/// <c>FunctionCallContent</c> calls in the session history, and converts
/// them into the response content Microsoft Agent Framework expects
/// (Phase 61).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToolApprovalResolver"/>'s sibling, with one deliberate
/// difference: an unmatched approval decision is silently skipped (a
/// decision arriving late does no harm), but an unmatched tool result is a
/// <strong>hard error</strong> — the model is blocked waiting on that call,
/// so a caller mistake (wrong <c>callId</c>, answering twice) must be
/// reported, not swallowed.
/// </para>
/// <para>
/// 🚨 By the time the default streaming run reaches <c>BuildMessagesAsync</c>,
/// the SSE response headers have already been sent (<c>SseWriter.StartAsync</c>
/// runs first) and a <c>ProblemDetails</c> response is no longer possible —
/// the same constraint decision K-324 documents for content guard blocks.
/// This is why <see cref="MatchAsync"/> is called <strong>twice</strong>:
/// once from <c>AgentEndpoints.RunAsync</c>, before the stream starts, purely
/// to validate and return <c>400</c>/<c>409</c> if needed, and again from
/// <c>BuildMessagesAsync</c> to actually build the message. Matching does
/// not audit or mutate anything, so calling it twice is side-effect free.
/// </para>
/// </remarks>
internal static class ClientToolResultResolver
{
    /// <summary>
    /// The maximum length of <see cref="ClientToolResult.Result"/> or
    /// <see cref="ClientToolResult.ErrorMessage"/>, in characters.
    /// </summary>
    /// <remarks>
    /// A client-side tool can return arbitrary browser content (DOM text, a
    /// large API response); without a bound, one oversized result would
    /// consume a large share of the model's context window on every
    /// subsequent turn, since it stays in session history. 64K characters
    /// comfortably fits a page of extracted text while still bounding the
    /// worst case.
    /// </remarks>
    public const int MaxResultLength = 65_536;

    /// <summary>
    /// Matches results against pending calls in the session history.
    /// </summary>
    /// <param name="results">The results coming from the UI.</param>
    /// <param name="agent">The resolved agent.</param>
    /// <param name="session">The open session.</param>
    /// <param name="chatHistory">The chat history provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The match outcome.</returns>
    public static async ValueTask<ClientToolResultMatch> MatchAsync(
        IReadOnlyList<ClientToolResult> results,
        AIAgent agent,
        AgentSession session,
        ChatHistoryProvider chatHistory,
        CancellationToken cancellationToken)
    {
        if (results.Count == 0)
        {
            return ClientToolResultMatch.Empty;
        }

        var history = await ChatHistoryReader
            .ReadAsync(agent, session, chatHistory, cancellationToken)
            .ConfigureAwait(false);

        var (pending, answered) = CollectCalls(history);
        var matched = new List<(ClientToolResult Result, FunctionCallContent Call)>(results.Count);

        foreach (var result in results)
        {
            if (!pending.Remove(result.CallId, out var call))
            {
                return answered.Contains(result.CallId)
                    ? ClientToolResultMatch.AlreadyAnswered(result.CallId)
                    : ClientToolResultMatch.UnknownCallId(result.CallId);
            }

            matched.Add((result, call));
        }

        return ClientToolResultMatch.Success(matched);
    }

    /// <summary>
    /// Builds the message carrying the tool results and records an audit
    /// entry for each one.
    /// </summary>
    /// <param name="matched">A successful <see cref="MatchAsync"/> outcome's matches.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The message to send; <see langword="null"/> if <paramref name="matched"/> is empty.</returns>
    /// <remarks>
    /// A failed audit write does <strong>not</strong> block the result from
    /// being applied — unlike an approval decision (K-089), this is data,
    /// not a security decision. <see cref="AuditRecorder"/> already logs and
    /// swallows the write error.
    /// </remarks>
    public static async ValueTask<ChatMessage?> BuildResponseMessageAsync(
        IReadOnlyList<(ClientToolResult Result, FunctionCallContent Call)> matched,
        ITenantContext tenantContext,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (matched.Count == 0)
        {
            return null;
        }

        var contents = new List<AIContent>(matched.Count);

        foreach (var (result, call) in matched)
        {
            var text = result.ErrorMessage is { Length: > 0 } error
                ? $"Error: {error}"
                : result.Result ?? string.Empty;

            contents.Add(new FunctionResultContent(result.CallId, text));

            await AuditRecorder.WriteAsync(
                auditLog,
                actorResolver,
                logger,
                tenantContext.TenantId,
                action: "client_tool.result",
                entity: $"tool:{call.Name}",
                before: null,
                after: JsonSerializer.Serialize(new { callId = result.CallId, isError = result.ErrorMessage is not null }),
                cancellationToken).ConfigureAwait(false);
        }

        return new ChatMessage(ChatRole.Tool, contents);
    }

    /// <summary>
    /// Collects pending (unanswered) and already-answered
    /// <c>FunctionCallContent</c> calls from the session history.
    /// </summary>
    /// <remarks>
    /// A server-side tool call never appears here without its result — the
    /// server invokes and answers it within the same turn. Only a
    /// client-side (declaration-only) call can be left pending, so no
    /// separate check against the tool registry's <c>RunsOnClient</c> flag
    /// is needed.
    /// </remarks>
    private static (Dictionary<string, FunctionCallContent> Pending, HashSet<string> Answered) CollectCalls(
        IReadOnlyList<ChatMessage> history)
    {
        var calls = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);
        var answered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in history)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case FunctionCallContent call:
                        calls[call.CallId] = call;
                        break;

                    case FunctionResultContent result:
                        answered.Add(result.CallId);
                        break;

                    default:
                        break;
                }
            }
        }

        var pending = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);

        foreach (var (callId, call) in calls)
        {
            if (!answered.Contains(callId))
            {
                pending[callId] = call;
            }
        }

        return (pending, answered);
    }
}

/// <summary>The outcome of <see cref="ClientToolResultResolver.MatchAsync"/>.</summary>
internal sealed class ClientToolResultMatch
{
    private ClientToolResultMatch(
        ClientToolResultMatchKind kind,
        IReadOnlyList<(ClientToolResult Result, FunctionCallContent Call)> matched,
        string? callId)
    {
        Kind = kind;
        Matched = matched;
        CallId = callId;
    }

    /// <summary>No results were sent; nothing to match.</summary>
    public static ClientToolResultMatch Empty { get; } = new(ClientToolResultMatchKind.Success, [], null);

    /// <summary>The outcome kind.</summary>
    public ClientToolResultMatchKind Kind { get; }

    /// <summary>Every result paired with the call it answers. Populated only when <see cref="Kind"/> is <see cref="ClientToolResultMatchKind.Success"/>.</summary>
    public IReadOnlyList<(ClientToolResult Result, FunctionCallContent Call)> Matched { get; }

    /// <summary>The offending <c>callId</c>. Populated only for a failure kind.</summary>
    public string? CallId { get; }

    public static ClientToolResultMatch Success(IReadOnlyList<(ClientToolResult Result, FunctionCallContent Call)> matched)
        => new(ClientToolResultMatchKind.Success, matched, null);

    public static ClientToolResultMatch UnknownCallId(string callId)
        => new(ClientToolResultMatchKind.UnknownCallId, [], callId);

    public static ClientToolResultMatch AlreadyAnswered(string callId)
        => new(ClientToolResultMatchKind.AlreadyAnswered, [], callId);
}

/// <summary>The kind of a <see cref="ClientToolResultMatch"/> outcome.</summary>
internal enum ClientToolResultMatchKind
{
    /// <summary>Every result matched a pending call.</summary>
    Success,

    /// <summary>A result's <c>callId</c> does not match any pending or answered call.</summary>
    UnknownCallId,

    /// <summary>A result's <c>callId</c> matches a call that already has a result.</summary>
    AlreadyAnswered,
}
