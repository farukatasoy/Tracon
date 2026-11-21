using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

/// <summary>
/// Resolves the internal chat-history conversation id <c>SqlChatHistoryProvider</c>
/// attaches to a session, for data subject export/erasure.
/// </summary>
/// <remarks>
/// <para>
/// A session's internal conversation id is NOT a queryable SQL column — the
/// <c>conversations</c>/<c>conversation_items</c> tables carry no <c>session_id</c>
/// at all. It lives inside the session's own state bag instead, under
/// <see cref="TraconSessionStateKeys.ChatHistory"/>, because
/// <c>SqlChatHistoryProvider</c>'s single instance is shared across every session
/// (Microsoft Agent Framework's explicit constraint) and cannot keep it as a field.
/// Finding it therefore needs the session restored through its OWN agent — the
/// same mechanism <see cref="ConversationBranchService"/> already uses for
/// branching — or, when that agent is no longer registered, its stored state read as
/// JSON; a SQL query cannot do either.
/// </para>
/// <para>
/// Without this step, <see cref="DataSubjectScope.SessionIds"/> alone can erase a
/// session's row but never its chat history: the resolver a consumer writes knows
/// only their own session ids, never Tracon's internal conversation id, and
/// has no supported way to discover it. This type closes that gap so a data
/// subject's ordinary agent-session chat history — not just the separate
/// OpenAI-compatible Conversations API — is actually reachable.
/// </para>
/// <para>
/// A session whose agent is no longer registered is read without it: the conversation id is
/// plain JSON in the stored state, under the same key, so the agent that wrote it is not
/// needed to find it. Only a state that does not have that shape (an engine that wrote it
/// another way, a value that is not JSON the way this reads it) leaves the id unknown.
/// A session whose state is unreadable, with or without its agent (a version mismatch, for
/// example), is skipped, logged and COUNTED rather than thrown: one unresolvable session must
/// not block export/erasure of every other session in the same request, and a skip nobody can
/// see would let the caller read a <c>200</c> as "everything is gone". An agent the catalog
/// refuses to compile is neither: that failure propagates, because an erasure that skipped the
/// session would delete the rest of the subject's rows and leave its conversation behind.
/// </para>
/// <para>
/// A session that does not exist in the ambient tenant is NOT counted. A session id of
/// another tenant answers <see langword="null"/> exactly as an id that does not exist
/// anywhere does, and counting it would tell the caller that the id exists elsewhere.
/// </para>
/// </remarks>
internal sealed class SessionConversationResolver
{
    private readonly ISessionStore _sessions;
    private readonly IAgentCatalog _catalog;
    private readonly ILogger<SessionConversationResolver> _logger;

    /// <summary>Creates a new resolver.</summary>
    /// <param name="sessions">The session store.</param>
    /// <param name="catalog">The agent catalog, needed to restore a session through its own agent.</param>
    /// <param name="logger">The logger for an unresolvable session.</param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public SessionConversationResolver(
        ISessionStore sessions,
        IAgentCatalog catalog,
        ILogger<SessionConversationResolver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(catalog);

        _sessions = sessions;
        _catalog = catalog;
        _logger = logger ?? NullLogger<SessionConversationResolver>.Instance;
    }

    /// <summary>Resolves the internal conversation id of every session that has one.</summary>
    /// <param name="sessionIds">The session ids to look up.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The resolved conversation ids, and how many sessions could not be looked up. The ids are
    /// fewer than <paramref name="sessionIds"/> when a session does not exist, has no
    /// conversation yet, or could not be looked up.
    /// </returns>
    /// <exception cref="TraconException">
    /// The catalog refuses to compile the agent of a session. Nothing is returned: the caller
    /// must not act on a partial answer.
    /// </exception>
    public ValueTask<SessionConversationLookup> ResolveAsync(
        IReadOnlyList<string> sessionIds,
        CancellationToken cancellationToken = default)
        => ResolveCoreAsync(sessionIds, readStoredStateWhenAgentIsRefused: false, cancellationToken);

    /// <summary>
    /// Resolves the internal conversation id of every session that has one, for a caller that deletes the sessions and
    /// must not be stopped by an agent the catalog refuses to compile.
    /// </summary>
    /// <param name="sessionIds">The session ids to look up.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The resolved conversation ids, and how many sessions could not be looked up. The ids are
    /// fewer than <paramref name="sessionIds"/> when a session does not exist, has no
    /// conversation yet, or could not be looked up.
    /// </returns>
    /// <remarks>
    /// The same lookup as <see cref="ResolveAsync"/>, with one difference: a session whose agent the catalog refuses to compile
    /// (a provider key with no value, an egress policy that no longer allows the provider) is read from its stored state, as a
    /// session whose agent is not registered is. A data subject request fails in that case, because an erasure that skipped
    /// the session would delete the rest and leave its conversation behind. Deleting a session or expiring it by its retention
    /// period must not fail for the same reason: the session could then never be deleted until the configuration is repaired,
    /// and one such session would hold every other expired session of its batch.
    /// </remarks>
    public ValueTask<SessionConversationLookup> ResolveForDeletionAsync(
        IReadOnlyList<string> sessionIds,
        CancellationToken cancellationToken = default)
        => ResolveCoreAsync(sessionIds, readStoredStateWhenAgentIsRefused: true, cancellationToken);

    private async ValueTask<SessionConversationLookup> ResolveCoreAsync(
        IReadOnlyList<string> sessionIds,
        bool readStoredStateWhenAgentIsRefused,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (sessionIds.Count == 0)
        {
            return new SessionConversationLookup([], 0);
        }

        var found = new List<Guid>(sessionIds.Count);
        var unresolved = 0;

        // A resolver that names a session twice still has one session to look up, and the count is of sessions.
        foreach (var sessionId in sessionIds.Distinct(StringComparer.Ordinal))
        {
            var record = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (record is null)
            {
                continue;
            }

            AIAgent? agent;

            try
            {
                agent = await _catalog.ResolveAsync(record.AgentName, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (TraconException ex) when (readStoredStateWhenAgentIsRefused)
            {
                _logger.LogWarning(
                    ex,
                    "Session '{SessionId}' names agent '{AgentName}', which the catalog cannot compile; its internal " +
                    "conversation is read from the stored state instead.",
                    sessionId,
                    record.AgentName);

                agent = null;
            }

            if (agent is null)
            {
                // The agent is gone, the state it wrote is not: the id is read from the stored JSON.
                if (TryReadStoredConversation(record.State, out var stored))
                {
                    if (stored is { } storedId)
                    {
                        found.Add(storedId);
                    }

                    continue;
                }

                _logger.LogWarning(
                    "Session '{SessionId}' names agent '{AgentName}', which is no longer registered, and its stored state " +
                    "does not have the shape this reads the conversation from; its internal conversation could not be " +
                    "resolved for export/erasure.",
                    sessionId,
                    record.AgentName);

                unresolved++;

                continue;
            }

            AgentSession session;

            try
            {
                session = await agent
                    .DeserializeSessionAsync(record.State, jsonSerializerOptions: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                _logger.LogWarning(
                    ex,
                    "Session '{SessionId}' could not be restored; its internal conversation could not be " +
                    "resolved for export/erasure.",
                    sessionId);

                unresolved++;

                continue;
            }

            if (session.StateBag.TryGetValue<ChatHistoryState>(
                    TraconSessionStateKeys.ChatHistory,
                    out var state,
                    TraconCoreJsonContext.Default.Options) &&
                state is { ConversationId: { } conversationId } &&
                conversationId != Guid.Empty)
            {
                found.Add(conversationId);
            }
        }

        return new SessionConversationLookup(found, unresolved);
    }

    /// <summary>
    /// Reads the conversation id of a session from its stored state, without the agent that wrote it.
    /// </summary>
    /// <param name="state">The stored state of the session.</param>
    /// <param name="conversationId">
    /// The conversation id; <see langword="null"/> when the state is readable and the session has no chat history yet.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the state has the shape the agent writes it in (an object with a <c>stateBag</c> object),
    /// so a missing chat history means there is none; <see langword="false"/> when it does not, and nothing can be said
    /// about the session's conversation.
    /// </returns>
    /// <remarks>
    /// The shape is the one <c>AgentSession.StateBag</c> serializes to and the chat history provider writes into
    /// (<see cref="TraconSessionStateKeys.ChatHistory"/> → <c>conversationId</c>). It belongs to the agent framework, not to
    /// Tracon: a framework version that writes it differently turns this into <see langword="false"/>, which is the count the
    /// session had before it was read here.
    /// </remarks>
    private static bool TryReadStoredConversation(JsonElement state, out Guid? conversationId)
    {
        conversationId = null;

        if (state.ValueKind != JsonValueKind.Object
            || !TryGetProperty(state, "stateBag", out var bag)
            || bag.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        // The key is a state-bag key, compared as written; the framework's own property names are not case-sensitive here.
        if (!bag.TryGetProperty(TraconSessionStateKeys.ChatHistory, out var history))
        {
            return true;
        }

        if (history.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetProperty(history, "conversationId", out var id))
        {
            return true;
        }

        if (id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var parsed))
        {
            return false;
        }

        conversationId = parsed == Guid.Empty ? null : parsed;

        return true;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;

                return true;
            }
        }

        value = default;

        return false;
    }
}

/// <summary>What looking up the internal conversations of a set of sessions found.</summary>
/// <param name="ConversationIds">The conversation ids that were found.</param>
/// <param name="Unresolved">
/// How many sessions have a conversation this lookup could not find, because the session state could not be restored
/// through its agent or, with the agent not registered, did not have the shape the id is read from. An upper bound:
/// without a readable state, whether the session had a conversation at all cannot be known.
/// </param>
internal readonly record struct SessionConversationLookup(IReadOnlyList<Guid> ConversationIds, int Unresolved);
