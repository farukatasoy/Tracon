using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Manages the lifecycle of named sessions: loads, restores, and saves them
/// through <see cref="ISessionStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class is provider-agnostic. It works the same way with the in-memory
/// store and the PostgreSQL store; it does not know which store is registered.
/// </para>
/// <para>
/// The session state is Microsoft Agent Framework's <c>SerializeSessionAsync</c>
/// output and is treated as <strong>opaque</strong>. AgentPrism does not
/// interpret its content. The identity is stamped onto the session's state
/// bag with <see cref="AgentSessionIdentity"/>, so that <c>RunRecordingAgent</c>
/// can write the real session identity to the run record.
/// </para>
/// <para>
/// Microsoft Agent Framework's pre-release <c>AgentSessionStore</c> abstraction
/// (<c>Microsoft.Agents.AI.Hosting</c>) is wired to this class by delegation.
/// </para>
/// </remarks>
public sealed class AgentSessionManager
{
    private readonly ISessionStore _store;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Marks sessions that <see cref="GetOrCreateSessionAsync"/> found no record
    /// for in the store and opened FRESH (not yet written anywhere).
    /// </summary>
    /// <remarks>
    /// <para>
    /// this marker lets <see cref="SaveSessionAsync"/> answer "is
    /// this the FIRST save of this session" without ever going to the store.
    /// The session object ITSELF is the key; <see cref="ConditionalWeakTable{TKey, TValue}"/>
    /// carries it safely for the duration of a single request without
    /// requiring extra lifecycle plumbing (the entry also drops when the
    /// session is GC'd).
    /// </para>
    /// <para>
    /// The marker does NOT become part of the session's SERIALIZED state — it
    /// is a transient marker tied only to this process's in-memory object
    /// identity.
    /// </para>
    /// </remarks>
    private readonly ConditionalWeakTable<AgentSession, object> _newlyOpenedSessions = new();

    private static readonly object NewSessionMarker = new();

    /// <summary>Creates a new session manager.</summary>
    /// <param name="store">The session store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="timeProvider">The time source. The system clock is used if not given.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public AgentSessionManager(ISessionStore store, ITenantContext tenantContext, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _store = store;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Restores the session with the given identity; opens a new session if
    /// no record exists. In both cases, the returned session is stamped with
    /// its own identity.
    /// </summary>
    /// <param name="agent">The agent the session belongs to.</param>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A session ready for use.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    /// <exception cref="AgentPrismException">The stored state cannot be restored by this agent.</exception>
    public async ValueTask<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var record = await _store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        AgentSession session;

        if (record is null)
        {
            // 🚨 NOTHING is written to the store YET. The chat history
            // provider's (ChatHistoryProvider) conversation identity is only
            // produced once the agent ACTUALLY runs — not here. A concurrent
            // "first request" also starts the same way, with an empty
            // session; the atomic claim happens in SaveSessionAsync, at the
            // moment of the first save (see that method's description,
            // HATA-004).
            session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            _newlyOpenedSessions.Add(session, NewSessionMarker);
        }
        else
        {
            try
            {
                session = await agent
                    .DeserializeSessionAsync(record.State, jsonSerializerOptions: null, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                throw new AgentPrismException(
                    $"Session '{sessionId}' could not be restored. The stored state belongs to agent " +
                    $"'{record.AgentName}' and cannot be read by agent '{agent.Name ?? agent.Id}'. " +
                    "If the Microsoft Agent Framework version changed, older sessions may have become unreadable.",
                    ex);
            }
        }

        AgentSessionIdentity.SetId(session, sessionId);
        return session;
    }

    /// <summary>Serializes the session and writes it to the store.</summary>
    /// <param name="agent">The agent the session belongs to.</param>
    /// <param name="session">The session to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identity of the saved session.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">
    /// The session has no identity because it was not opened with <see cref="GetOrCreateSessionAsync"/>.
    /// </exception>
    /// <exception cref="AgentPrismSessionConflictException">
    /// This was the FIRST save of a session that <see cref="GetOrCreateSessionAsync"/>
    /// opened FRESH, and a concurrent request saved the same NEW session
    /// identity before we did.
    /// </exception>
    /// <remarks>
    /// <para>
    /// If two concurrent first requests arrive for the
    /// same NEW session identity, both start with an EMPTY session in
    /// <see cref="GetOrCreateSessionAsync"/> and run THEIR OWN turn — each
    /// produces its own conversation identity, which is unavoidable (the
    /// conversation identity is only known once the turn runs). The actual
    /// defect was that the second <see cref="ISessionStore.SaveAsync"/> would
    /// <strong>unconditionally</strong> overwrite the first, silently leaving
    /// the loser's conversation unreachable. For this reason, the FIRST save
    /// of a session (see the fresh sessions marked by
    /// <see cref="GetOrCreateSessionAsync"/>) always attempts an atomic write
    /// with <see cref="ISessionStore.TryCreateAsync"/>; if it loses, it does
    /// NOT SILENTLY OVERWRITE, and instead throws an explicit
    /// <see cref="AgentPrismSessionConflictException"/>. Subsequent saves (and
    /// EVERY save of a session that was already found existing) continue,
    /// unchanged, to use the unconditional <see cref="ISessionStore.SaveAsync"/>.
    /// </para>
    /// </remarks>
    public async ValueTask<string> SaveSessionAsync(
        AIAgent agent,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);

        var sessionId = AgentSessionIdentity.GetId(session)
            ?? throw new AgentPrismException(
                "The session has no AgentPrism identity. Sessions to be saved must be opened with " +
                $"{nameof(GetOrCreateSessionAsync)}.");

        var state = await agent
            .SerializeSessionAsync(session, jsonSerializerOptions: null, cancellationToken)
            .ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();

        var record = new SessionRecord
        {
            Id = sessionId,
            AgentName = agent.Name ?? agent.Id,
            State = state,
            CreatedAt = now,
            UpdatedAt = now,
            TenantId = _tenantContext.TenantId,
        };

        if (_newlyOpenedSessions.TryGetValue(session, out _))
        {
            if (!await _store.TryCreateAsync(record, cancellationToken).ConfigureAwait(false))
            {
                throw new AgentPrismSessionConflictException(
                    $"Another request also opened session '{sessionId}' at the same time and saved it before us. " +
                    "Retry again shortly.")
                {
                    SessionId = sessionId,
                };
            }

            _newlyOpenedSessions.Remove(session);
        }
        else
        {
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return sessionId;
    }

    /// <summary>Deletes the session.</summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if a deletion occurred.</returns>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    public ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return _store.DeleteAsync(sessionId, cancellationToken);
    }

    /// <summary>Lists sessions matching a filter.</summary>
    /// <param name="query">The filter. The current tenant is used if none is specified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sessions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is <see langword="null"/>.</exception>
    public ValueTask<IReadOnlyList<SessionRecord>> QuerySessionsAsync(
        SessionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return _store.QueryAsync(
            query.TenantId is null ? query with { TenantId = _tenantContext.TenantId } : query,
            cancellationToken);
    }
}
