using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism;

/// <summary>
/// Resolves the internal chat-history conversation id <c>SqlChatHistoryProvider</c>
/// attaches to a session, for data subject export/erasure (phase 64).
/// </summary>
/// <remarks>
/// <para>
/// A session's internal conversation id is NOT a queryable SQL column — the
/// <c>conversations</c>/<c>conversation_items</c> tables carry no <c>session_id</c>
/// at all. It lives inside the session's own state bag instead, under
/// <see cref="AgentPrismSessionStateKeys.ChatHistory"/>, because
/// <c>SqlChatHistoryProvider</c>'s single instance is shared across every session
/// (Microsoft Agent Framework's explicit constraint) and cannot keep it as a field.
/// Finding it therefore needs the session restored through its OWN agent — the
/// same mechanism <see cref="ConversationBranchService"/> already uses for
/// branching — not a SQL query.
/// </para>
/// <para>
/// Without this step, <see cref="DataSubjectScope.SessionIds"/> alone can erase a
/// session's row but never its chat history: the resolver a consumer writes knows
/// only their own session ids, never AgentPrism's internal conversation id, and
/// has no supported way to discover it. This type closes that gap so a data
/// subject's ordinary agent-session chat history — not just the separate
/// OpenAI-compatible Conversations API — is actually reachable.
/// </para>
/// <para>
/// A session whose agent can no longer be resolved, or whose state cannot be
/// deserialized (a version mismatch, for example), is skipped and logged rather
/// than thrown: one unresolvable session must not block export/erasure of every
/// other session in the same request.
/// </para>
/// </remarks>
public sealed class SessionConversationResolver
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
    /// The resolved conversation ids. Shorter than <paramref name="sessionIds"/> when a
    /// session does not exist, has no conversation yet, or could not be restored.
    /// </returns>
    public async ValueTask<IReadOnlyList<Guid>> ResolveAsync(
        IReadOnlyList<string> sessionIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        if (sessionIds.Count == 0)
        {
            return [];
        }

        var found = new List<Guid>(sessionIds.Count);

        foreach (var sessionId in sessionIds)
        {
            var record = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

            if (record is null)
            {
                continue;
            }

            var agent = await _catalog.ResolveAsync(record.AgentName, cancellationToken).ConfigureAwait(false);

            if (agent is null)
            {
                _logger.LogWarning(
                    "Session '{SessionId}' names agent '{AgentName}', which is no longer registered; " +
                    "its internal conversation could not be resolved for export/erasure.",
                    sessionId,
                    record.AgentName);

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

                continue;
            }

            if (session.StateBag.TryGetValue<ChatHistoryState>(
                    AgentPrismSessionStateKeys.ChatHistory,
                    out var state,
                    AgentPrismCoreJsonContext.Default.Options) &&
                state is { ConversationId: { } conversationId } &&
                conversationId != Guid.Empty)
            {
                found.Add(conversationId);
            }
        }

        return found;
    }
}
