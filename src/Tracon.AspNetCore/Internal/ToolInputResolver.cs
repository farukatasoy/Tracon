using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Matches the answers a caller sends against the tool input requests (MCP elicitation) that suspended a
/// tool call of the session, and turns each answer into the input of a run that calls the tool again.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ToolApprovalResolver"/>'s and <see cref="ClientToolResultResolver"/>'s sibling. The suspended
/// call lives in the session history as a call whose result is the <c>input_required</c> payload the tool
/// wrapper returned; no table holds it. The answer is <strong>the next turn's input</strong>.
/// </para>
/// <para>
/// An answer to a request nobody is waiting for is a <strong>hard error</strong>, not skipped as an approval
/// is: running a tool again under a wrong identity repeats whatever the tool does. The same reason makes the
/// matching a separate step from the build: the endpoint matches before the stream starts, so a mistake is a
/// <c>400</c>/<c>409</c> the caller can read, and matching again when the message is built has no side effect.
/// </para>
/// <para>
/// A pending request is found in the history by its payload, but the payload is only text a server's tool could
/// also have returned; the worst a forged one can do is have the session's own user re-run a call the model
/// already made. Whether a request WAS raised by a run is decided from the run's ledger, not from here.
/// </para>
/// </remarks>
internal static class ToolInputResolver
{
    /// <summary>The audit action of an answer to a tool input request.</summary>
    public const string AuditAction = "tool_input.answer";

    /// <summary>Matches answers against the pending requests of the session history.</summary>
    /// <param name="responses">The answers coming from the caller.</param>
    /// <param name="agent">The resolved agent.</param>
    /// <param name="session">The open session.</param>
    /// <param name="chatHistory">The chat history provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The match outcome.</returns>
    public static async ValueTask<ToolInputMatch> MatchAsync(
        IReadOnlyList<ToolInputResponse> responses,
        AIAgent agent,
        AgentSession session,
        ChatHistoryProvider chatHistory,
        CancellationToken cancellationToken)
    {
        if (responses.Count == 0)
        {
            return ToolInputMatch.Empty;
        }

        var history = await ChatHistoryReader
            .ReadAsync(agent, session, chatHistory, cancellationToken)
            .ConfigureAwait(false);

        var (pending, answered) = Collect(history);
        var matched = new List<(ToolInputResponse Response, PendingInputRecord Pending)>(responses.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var response in responses)
        {
            if (!seen.Add(response.RequestId))
            {
                return ToolInputMatch.Rejected(ToolInputMatchKind.DuplicateRequestId, response.RequestId, detail: null);
            }

            if (!pending.TryGetValue(response.RequestId, out var request))
            {
                return answered.Contains(response.RequestId)
                    ? ToolInputMatch.Rejected(ToolInputMatchKind.AlreadyAnswered, response.RequestId, detail: null)
                    : ToolInputMatch.Rejected(ToolInputMatchKind.UnknownRequestId, response.RequestId, detail: null);
            }

            if (response.Action != ToolInputAction.Accept && response.Content is { Count: > 0 })
            {
                return ToolInputMatch.Rejected(
                    ToolInputMatchKind.InvalidContent,
                    response.RequestId,
                    "'content' is allowed only with the 'Accept' action.");
            }

            if (response.Action == ToolInputAction.Accept &&
                ToolInputSchemaValidator.Validate(request.Schema, response.Content) is { } problem)
            {
                return ToolInputMatch.Rejected(ToolInputMatchKind.InvalidContent, response.RequestId, problem);
            }

            matched.Add((response, request));
        }

        return ToolInputMatch.Success(matched);
    }

    /// <summary>
    /// Builds the messages that call the suspended tools again and the answers the calls will read, and
    /// records an audit entry for each answer BEFORE anything is applied.
    /// </summary>
    /// <param name="matched">A successful <see cref="MatchAsync"/> outcome's matches.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="auditLog">The audit log.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric set that counts a failed audit write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The continuation; empty when <paramref name="matched"/> is empty.</returns>
    /// <exception cref="TraconException">An audit entry could not be written; no answer is applied.</exception>
    /// <remarks>
    /// <para>
    /// The audit entry names the request, the tool, the action and the NAMES of the fields: never a value.
    /// A value can be personal data, and the audit log is kept far longer than a session.
    /// It is written like an approval decision's: an answer that cannot be recorded is not
    /// carried out, because it releases what a server asked for into a tool call.
    /// </para>
    /// <para>
    /// The call is made again through the function-invocation loop's own path for an approved tool call: a
    /// <c>ToolApprovalResponseContent</c> answering the request Tracon recorded with the session when the run
    /// closed (<c>ToolInputSessionBridge</c>). That path runs the call through the tool's whole wrapper chain and
    /// writes the call and its result into the history. The answer is NOT an approval of anything: the call was
    /// approved, or exempt, when it first ran, and the audit entry says what it is.
    /// </para>
    /// </remarks>
    public static async ValueTask<ToolInputContinuation> BuildContinuationAsync(
        IReadOnlyList<(ToolInputResponse Response, PendingInputRecord Pending)> matched,
        ITenantContext tenantContext,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger logger,
        TraconMetrics? metrics,
        CancellationToken cancellationToken)
    {
        if (matched.Count == 0)
        {
            return ToolInputContinuation.None;
        }

        var actor = actorResolver.Resolve();
        var responses = new List<AIContent>(matched.Count);
        var answers = new Dictionary<string, IReadOnlyList<ToolInputAnswer>>(matched.Count, StringComparer.Ordinal);

        foreach (var (response, pending) in matched)
        {
            await AuditRecorder.WriteOrThrowAsync(
                auditLog,
                actor,
                logger,
                metrics,
                tenantContext.TenantId,
                action: AuditAction,
                entity: $"tool:{pending.ToolName}",
                before: null,
                after: JsonSerializer.Serialize(new
                {
                    requestId = pending.RequestId,
                    server = pending.ServerName,
                    action = response.Action.ToString(),
                    fields = response.Content?.Keys.Order(StringComparer.Ordinal).ToArray() ?? [],
                }),
                refusal: $"The answer to the input request of tool '{pending.ToolName}' was not applied",
                timeProvider: null,
                cancellationToken).ConfigureAwait(false);

            var replayId = pending.ReplayCallId;
            var arguments = pending.Call.Arguments is null
                ? null
                : new Dictionary<string, object?>(pending.Call.Arguments, StringComparer.Ordinal);

            var replay = new FunctionCallContent(replayId, pending.Call.Name, arguments);

            responses.Add(new ToolApprovalResponseContent(pending.RequestId, approved: true, replay));

            answers[replayId] =
            [
                new ToolInputAnswer(
                    pending.Fingerprint,
                    response.Action switch
                    {
                        ToolInputAction.Accept => ToolInputActions.Accept,
                        ToolInputAction.Decline => ToolInputActions.Decline,
                        _ => ToolInputActions.Cancel,
                    },
                    response.Action == ToolInputAction.Accept ? response.Content : null),
            ];
        }

        return new ToolInputContinuation([new ChatMessage(ChatRole.User, responses)], answers);
    }

    /// <summary>Reads a request the tool wrapper wrote as a call's result, or reports that the result is not one.</summary>
    /// <param name="result">The tool result as the history holds it.</param>
    /// <returns>The fields of the payload; <see langword="null"/> when <paramref name="result"/> is not a suspended call's result.</returns>
    internal static JsonElement? ReadPayload(object? result)
    {
        JsonElement element;

        switch (result)
        {
            case string text:
                try
                {
                    using var document = JsonDocument.Parse(text);
                    element = document.RootElement.Clone();
                }
                catch (JsonException)
                {
                    return null;
                }

                break;

            case JsonElement { ValueKind: JsonValueKind.String } wrapped:
                return ReadPayload(wrapped.GetString());

            case JsonElement { ValueKind: JsonValueKind.Object } json:
                element = json;
                break;

            default:
                return null;
        }

        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty("status", out var status) &&
               status.ValueKind == JsonValueKind.String &&
               string.Equals(status.GetString(), ToolInputPayload.Status, StringComparison.Ordinal)
            ? element
            : null;
    }

    /// <summary>Collects the unanswered requests and the answered request ids of the session history.</summary>
    private static (Dictionary<string, PendingInputRecord> Pending, HashSet<string> Answered) Collect(
        IReadOnlyList<ChatMessage> history)
    {
        var calls = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);
        var results = new Dictionary<string, FunctionResultContent>(StringComparer.Ordinal);
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
                        results[result.CallId] = result;
                        break;

                    case ToolApprovalResponseContent response:
                        answered.Add(response.RequestId);
                        break;

                    default:
                        break;
                }
            }
        }

        var pending = new Dictionary<string, PendingInputRecord>(StringComparer.Ordinal);

        foreach (var (callId, result) in results)
        {
            if (!calls.TryGetValue(callId, out var call) || ReadPayload(result.Result) is not { } payload)
            {
                continue;
            }

            if (Text(payload, "requestId") is not { Length: > 0 } requestId ||
                Text(payload, "replayCallId") is not { Length: > 0 } replayCallId ||
                Text(payload, "fingerprint") is not { Length: > 0 } fingerprint)
            {
                continue;
            }

            pending[requestId] = new PendingInputRecord(
                requestId,
                replayCallId,
                call,
                Text(payload, "tool") ?? call.Name,
                Text(payload, "server") ?? string.Empty,
                fingerprint,
                payload.TryGetProperty("requestedSchema", out var schema) ? schema.Clone() : default);
        }

        foreach (var requestId in answered)
        {
            pending.Remove(requestId);
        }

        return (pending, answered);
    }

    private static string? Text(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>A request that suspended a tool call, as the session history holds it.</summary>
/// <param name="RequestId">The identity Tracon gave the request.</param>
/// <param name="ReplayCallId">The call identity the tool call runs again under; the answers are keyed by it.</param>
/// <param name="Call">The call that was suspended.</param>
/// <param name="ToolName">The qualified tool name.</param>
/// <param name="ServerName">The MCP server that asked.</param>
/// <param name="Fingerprint">The digest of the message and the schema the server sent.</param>
/// <param name="Schema">The flat schema of the fields the server asked for.</param>
internal sealed record PendingInputRecord(
    string RequestId,
    string ReplayCallId,
    FunctionCallContent Call,
    string ToolName,
    string ServerName,
    string Fingerprint,
    JsonElement Schema);

/// <summary>The outcome of <see cref="ToolInputResolver.MatchAsync"/>.</summary>
internal sealed class ToolInputMatch
{
    private ToolInputMatch(
        ToolInputMatchKind kind,
        IReadOnlyList<(ToolInputResponse Response, PendingInputRecord Pending)> matched,
        string? requestId,
        string? detail)
    {
        Kind = kind;
        Matched = matched;
        RequestId = requestId;
        Detail = detail;
    }

    /// <summary>No answers were sent; nothing to match.</summary>
    public static ToolInputMatch Empty { get; } = new(ToolInputMatchKind.Success, [], null, null);

    /// <summary>Gets the outcome kind.</summary>
    public ToolInputMatchKind Kind { get; }

    /// <summary>Gets every answer paired with the request it answers; filled only for <see cref="ToolInputMatchKind.Success"/>.</summary>
    public IReadOnlyList<(ToolInputResponse Response, PendingInputRecord Pending)> Matched { get; }

    /// <summary>Gets the offending request identifier; filled only for a failure kind.</summary>
    public string? RequestId { get; }

    /// <summary>Gets what is wrong with the values; filled only for <see cref="ToolInputMatchKind.InvalidContent"/>.</summary>
    public string? Detail { get; }

    /// <summary>Creates a successful outcome.</summary>
    public static ToolInputMatch Success(IReadOnlyList<(ToolInputResponse Response, PendingInputRecord Pending)> matched)
        => new(ToolInputMatchKind.Success, matched, null, null);

    /// <summary>Creates a failed outcome.</summary>
    public static ToolInputMatch Rejected(ToolInputMatchKind kind, string requestId, string? detail)
        => new(kind, [], requestId, detail);
}

/// <summary>The kind of a <see cref="ToolInputMatch"/> outcome.</summary>
internal enum ToolInputMatchKind
{
    /// <summary>Every answer matched a pending request.</summary>
    Success,

    /// <summary>An answer's <c>requestId</c> matches no request of the session.</summary>
    UnknownRequestId,

    /// <summary>An answer's <c>requestId</c> matches a request that was already answered.</summary>
    AlreadyAnswered,

    /// <summary>The same <c>requestId</c> was answered twice in one request.</summary>
    DuplicateRequestId,

    /// <summary>An answer's values do not fit the fields the server asked for.</summary>
    InvalidContent,
}

/// <summary>What answering the pending requests adds to a run.</summary>
/// <param name="Messages">The messages that call the suspended tools again, in order.</param>
/// <param name="Answers">The answers the replayed calls read, by the call identity of the replay.</param>
internal sealed record ToolInputContinuation(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyDictionary<string, IReadOnlyList<ToolInputAnswer>> Answers)
{
    /// <summary>The continuation that adds nothing.</summary>
    public static ToolInputContinuation None { get; } =
        new([], new Dictionary<string, IReadOnlyList<ToolInputAnswer>>(StringComparer.Ordinal));
}
