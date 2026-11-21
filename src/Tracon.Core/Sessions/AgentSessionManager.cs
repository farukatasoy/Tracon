using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

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
/// output and is treated as <strong>opaque</strong>. Tracon does not
/// interpret its content. The identity is stamped onto the session's state
/// bag, so that <see cref="RunRecordingAgent"/> can write the real session
/// identity to the run record.
/// </para>
/// <para>
/// Microsoft Agent Framework's <c>AgentSessionStore</c> abstraction is wired to
/// this class by delegation.
/// </para>
/// <para>
/// <c>AddTracon()</c> registers the manager as a singleton; resolve it with
/// <c>GetRequiredService&lt;AgentSessionManager&gt;()</c>.
/// </para>
/// </remarks>
public sealed class AgentSessionManager
{
    /// <summary>
    /// The Tracon schema generation this build writes and can read.
    /// </summary>
    /// <remarks>
    /// Advances only when Tracon changes how it structures the stored
    /// row, never when the Microsoft Agent Framework version changes.
    /// Not public: a consumer never needs to compare against it directly —
    /// <see cref="SessionRecord.StateSchemaVersion"/> already tells them
    /// what generation their own row was written with. The value itself lives
    /// in <see cref="StateSchemaGenerations"/>, so the state preflight reads
    /// the same number this writer stamps.
    /// </remarks>
    internal const int CurrentStateSchemaVersion = StateSchemaGenerations.Session;

    /// <summary>
    /// The running process's Microsoft Agent Framework package version,
    /// stamped onto every session this process saves.
    /// </summary>
    private static readonly string CurrentMafVersion = ReadMafVersion();

    private readonly ISessionStore _store;
    private readonly ITenantContext _tenantContext;
    /// <summary>The note appended to an answer that was cut off by a cancellation, so the next turn knows it is partial.</summary>
    internal const string CanceledAnswerNote = "[The response was canceled before it finished.]";

    private readonly ChatHistoryProvider? _chatHistory;
    private readonly TimeProvider _timeProvider;
    private readonly IRunAttributionContext? _attributionContext;
    private readonly IOptionsMonitor<TraconSessionOwnershipOptions>? _ownershipOptions;
    private readonly IReadOnlyList<ISubjectContentEraser> _contentErasers;

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
    /// identity. It holds the agent the session was opened with and a gate: the first
    /// record is written by whichever comes first, the end-of-run save or the first
    /// change a tool makes to the session's workspace (<see cref="EnsureRecordAsync"/>),
    /// and the gate makes that "first" one writer.
    /// </para>
    /// </remarks>
    private readonly ConditionalWeakTable<AgentSession, OpenedSession> _newlyOpenedSessions = new();

    /// <summary>
    /// The record each restored session was read from: which identity, and at
    /// which store generation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sibling of <see cref="_newlyOpenedSessions"/> and carried the same
    /// way, for the same reason: a save has to know what it is replacing.
    /// The entry is mutable so a successful save can advance the generation in
    /// place — a second save within the same request replaces what the FIRST
    /// save wrote, not what the request originally read.
    /// </para>
    /// <para>
    /// The <strong>identity</strong> is part of the entry, not just the generation. A session
    /// can be saved under a DIFFERENT id than it was read from: the
    /// OpenAI-compatible Responses endpoint restores from
    /// <c>previous_response_id</c> and saves under the new response id, and
    /// <c>TraconAgentSessionStore</c> re-stamps the identity right before
    /// saving. Comparing the source record's generation against a different
    /// record would reject a perfectly good write.
    /// </para>
    /// </remarks>
    private readonly ConditionalWeakTable<AgentSession, RestoredRecord> _restoredVersions = new();


    /// <summary>Creates a new session manager.</summary>
    /// <param name="store">The session store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="timeProvider">The time source. The system clock is used if not given.</param>
    /// <param name="attributionContext">
    /// The identity pipeline a session owner is read from. Only consulted while
    /// <see cref="TraconSessionOwnershipOptions.Enabled"/> is on; without it
    /// ownership cannot be resolved and every write behaves as if no identity
    /// were available.
    /// </param>
    /// <param name="ownershipOptions">
    /// The ownership settings. <see langword="null"/> means ownership is off,
    /// which is also the default when the options ARE registered.
    /// </param>
    /// <param name="contentErasers">
    /// The stores that keep a copy of a session's content outside the session store (conversation
    /// memory). A session's copy is erased before the session is deleted.
    /// </param>
    /// <param name="chatHistory">
    /// The chat history provider <see cref="ReadHistoryAsync"/> reads a conversation with; without one that
    /// method cannot be used.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    internal AgentSessionManager(
        ISessionStore store,
        ITenantContext tenantContext,
        TimeProvider? timeProvider = null,
        IRunAttributionContext? attributionContext = null,
        IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions = null,
        IEnumerable<ISubjectContentEraser>? contentErasers = null,
        ChatHistoryProvider? chatHistory = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _store = store;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _attributionContext = attributionContext;
        _ownershipOptions = ownershipOptions;
        _contentErasers = contentErasers is null ? [] : [.. contentErasers];
        _chatHistory = chatHistory;
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
    /// <exception cref="TraconException">The stored state cannot be restored by this agent.</exception>
    /// <exception cref="TraconSessionOwnerRequiredException">
    /// Session ownership is on, this call would OPEN a new session, and no
    /// authenticated identity could be resolved to own it.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Nothing is written to the store here, and the checks that stand in front of the HTTP endpoints (the run
    /// authorization handler, the session owner, the tenant quota) are not made: ask <see cref="RunAdmission"/> for them.
    /// </para>
    /// <para>
    /// What a later turn finds depends on <see cref="SaveSessionAsync"/>. A new session exists only in memory until it is
    /// saved: a turn that runs on it and is never saved leaves a conversation no record names, and the next call opens
    /// a session with an empty history. For a session that already has a record, a SQL history provider writes each turn
    /// to the conversation as the turn ends, so a later turn that is not saved is still in the conversation the next
    /// turn reads; with the in-memory default the history is part of the session's state, and a turn that is not saved
    /// is lost. A turn that is canceled writes nothing; see <see cref="RecordCanceledTurnAsync"/>.
    /// </para>
    /// </remarks>
    public async ValueTask<AgentSession> GetOrCreateSessionAsync(
        AIAgent agent,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var record = await _store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (record is not null)
        {
            return await RestoreSessionAsync(agent, sessionId, record, cancellationToken).ConfigureAwait(false);
        }

        // 🚨 NOTHING is written to the store YET. The chat history
        // provider's (ChatHistoryProvider) conversation identity is only
        // produced once the agent ACTUALLY runs — not here. A concurrent
        // "first request" also starts the same way, with an empty
        // session; the atomic claim happens in SaveSessionAsync, at the
        // moment of the first save (see that method's description).
        // 🚨 Ownership is resolved HERE, before the agent has run, and not
        // only at the save that follows: a rejection after the turn has
        // already reached the model has cost real money and produced an
        // answer nobody may keep. The value is deliberately DISCARDED --
        // the save resolves it again through the same method, from the
        // same source, and this call exists purely to fail early. Both
        // calls have to stay: a session whose identity was stamped by hand
        // never passes through here at all, and the save is the only place
        // that can still keep the invariant for it.
        _ = ClaimOwnerId(sessionId);

        var session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
        _newlyOpenedSessions.Add(session, new OpenedSession(agent));

        AgentSessionIdentity.SetId(session, sessionId);
        return session;
    }

    /// <summary>
    /// Restores the session with the given identity; returns
    /// <see langword="null"/> if no record exists. Never opens a session.
    /// </summary>
    /// <param name="agent">The agent the session belongs to.</param>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The restored session, stamped with its identity, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The lookup Microsoft Agent Framework's <c>AgentSessionStore.GetSessionAsync</c>
    /// contract asks for ("this method never creates a session"). Not public:
    /// a consumer opens sessions through <see cref="GetOrCreateSessionAsync"/>,
    /// which also arms the first-save claim a lookup that returns nothing
    /// must not arm.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    /// <exception cref="TraconException">The stored state cannot be restored by this agent.</exception>
    internal async ValueTask<AgentSession?> GetExistingSessionAsync(
        AIAgent agent,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var record = await _store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);

        return record is null
            ? null
            : await RestoreSessionAsync(agent, sessionId, record, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the messages of a session's conversation, oldest first. The whole conversation is read on each call and the page is cut from it.</summary>
    /// <param name="agent">
    /// The agent the session belongs to. Its type decides how the stored state is read, as it does for
    /// <see cref="GetOrCreateSessionAsync"/>.
    /// </param>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="skip">How many messages to leave out from the start. The index of a message is its position in the conversation.</param>
    /// <param name="take">How many messages to return at most, or <see langword="null"/> for all the rest.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The messages, in order, including the tool calls and tool results the turns produced; empty when the session
    /// has no history yet. <see langword="null"/> when the current tenant has no session with that identity.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The history is read through the registered <c>ChatHistoryProvider</c>, so it is the one the agent itself
    /// sees, and a protected store gives back readable messages. It is the same read the management API
    /// (<c>GET /api/sessions/{id}</c>) answers from.
    /// </para>
    /// <para>
    /// The call checks the tenant, not the owner. Session ownership is a gate in front of the HTTP endpoints; a host
    /// that reads a conversation back to a user asks <see cref="RunAdmission"/> first, as it does before it runs an
    /// agent. Reading does not claim the session, change it or save it. An agent that builds its own chat history
    /// instead of taking the registered provider shows no messages when the history is kept in a SQL store.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="skip"/> or <paramref name="take"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">The manager was created without a chat history provider.</exception>
    /// <exception cref="TraconException">The stored state cannot be restored by this agent.</exception>
    public async ValueTask<IReadOnlyList<ChatMessage>?> ReadHistoryAsync(
        AIAgent agent,
        string sessionId,
        int skip = 0,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);

        if (take is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, "The number of messages to take cannot be negative.");
        }

        var chatHistory = _chatHistory
            ?? throw new InvalidOperationException("This session manager has no chat history provider to read a conversation with.");

        var session = await GetExistingSessionAsync(agent, sessionId, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return null;
        }

        var messages = await ChatHistoryReader.ReadAsync(agent, session, chatHistory, cancellationToken).ConfigureAwait(false);

        return skip == 0 && take is null
            ? messages
            : [.. messages.Skip(skip).Take(take ?? int.MaxValue)];
    }

    /// <summary>Writes the messages of a turn that was canceled before it finished, and saves the session.</summary>
    /// <param name="agent">The agent the session belongs to.</param>
    /// <param name="session">The open session of the canceled turn.</param>
    /// <param name="requestMessages">The messages the turn was asked: the user's message.</param>
    /// <param name="partialAnswer">
    /// The text of the answer that had been produced when the turn was canceled, or <see langword="null"/> when none had.
    /// A note that the answer was cut off is added to it.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token of the write. The turn's own token is canceled by now; pass
    /// <see cref="CancellationToken.None"/> or a token of your own.
    /// </param>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// <para>
    /// A turn writes its messages to the session's history when it ends. A turn that is canceled does not end, so
    /// without this call the next turn answers as if the message had never been sent. Call it from the place that
    /// catches the cancellation, for a turn that is worth keeping.
    /// </para>
    /// <para>
    /// The write goes around the checks a run makes: the content guards have not necessarily screened
    /// <paramref name="requestMessages"/> if the turn was canceled early, and a message that would have been refused
    /// then becomes part of the history the next turns read. Write only a message the model has started to answer, or
    /// screen it yourself. The messages are subject to the same retention and erasure as the rest of the history.
    /// </para>
    /// <para>
    /// The save can conflict with another turn of the same session; the conflict is thrown as it is by
    /// <see cref="SaveSessionAsync"/>, and the messages are already written by then.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/>, <paramref name="session"/> or <paramref name="requestMessages"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The manager was created without a chat history provider.</exception>
    /// <exception cref="TraconSessionConflictException">Another turn changed the session first.</exception>
    public async ValueTask RecordCanceledTurnAsync(
        AIAgent agent,
        AgentSession session,
        IReadOnlyList<ChatMessage> requestMessages,
        string? partialAnswer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(requestMessages);

        var chatHistory = _chatHistory
            ?? throw new InvalidOperationException("This session manager has no chat history provider to write a conversation with.");

        var trimmed = (partialAnswer ?? string.Empty).Trim();

        IReadOnlyList<ChatMessage> response = trimmed.Length > 0
            ? [new ChatMessage(ChatRole.Assistant, trimmed + "\n\n" + CanceledAnswerNote)]
            : [];

        await ChatHistoryReader.AppendAsync(agent, session, chatHistory, requestMessages, response, cancellationToken).ConfigureAwait(false);

        await SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a stored record back into a session and remembers which record
    /// (identity and generation) it was read from.
    /// </summary>
    private async ValueTask<AgentSession> RestoreSessionAsync(
        AIAgent agent,
        string sessionId,
        SessionRecord record,
        CancellationToken cancellationToken)
    {
        if (record.StateSchemaVersion > CurrentStateSchemaVersion)
        {
            throw new TraconException(
                $"Session '{sessionId}' was written with Tracon schema generation {record.StateSchemaVersion}; " +
                $"this Tracon version can read up to generation {CurrentStateSchemaVersion}. " +
                "Update the Tracon packages.");
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
            var recordedMafVersion = record.StateMafVersion ?? "unknown (written before version stamping existed)";

            throw new TraconException(
                $"Session '{sessionId}' could not be restored. The stored state belongs to agent " +
                $"'{record.AgentName}' and cannot be read by agent '{agent.Name ?? agent.Id}'. " +
                $"It was written with Microsoft Agent Framework {recordedMafVersion}; this process is " +
                $"running {CurrentMafVersion}. If the Microsoft Agent Framework version changed, open a new " +
                "session instead, or clear old sessions before upgrading.",
                ex);
        }

        _restoredVersions.AddOrUpdate(session, new RestoredRecord(sessionId, record.Version, record.OwnerId));

        AgentSessionIdentity.SetId(session, sessionId);
        return session;
    }

    /// <summary>Serializes the session and writes it to the store.</summary>
    /// <param name="agent">The agent the session belongs to.</param>
    /// <param name="session">The session to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The identity of the saved session.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The session has no identity because it was not opened with <see cref="GetOrCreateSessionAsync"/>.
    /// </exception>
    /// <exception cref="TraconSessionConflictException">
    /// Another turn changed the session record since this session was read: the first save of a session opened fresh
    /// lost to a concurrent save of the same identity, or a later save found the record's version advanced.
    /// </exception>
    /// <exception cref="TraconSessionOwnerRequiredException">
    /// Session ownership is on, this write would CLAIM ownership of a session,
    /// and no authenticated identity could be resolved to be its owner. See
    /// <see cref="ResolveOwnerId(AgentSession, string)"/>.
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
    /// <see cref="TraconSessionConflictException"/>.
    /// </para>
    /// <para>
    /// EVERY LATER save of that same record is guarded the same way, through
    /// <see cref="ISessionStore.TryUpdateAsync"/>: closing the race for the
    /// first write only left the more common case open — two concurrent turns
    /// on an EXISTING session both reported success and the loser's turn was
    /// silently overwritten. A save that targets a DIFFERENT identity than the
    /// session was read from is a new record, not a replacement, and is still
    /// written unconditionally.
    /// </para>
    /// <para>
    /// The session's OWNER travels through all three of those branches
    /// untouched once it is set: <see cref="ResolveOwnerId(AgentSession, string)"/> claims it on the
    /// first write and carries it verbatim on every later one. The stores
    /// hold the same rule a second time, so an owner survives even a write
    /// this manager never saw.
    /// </para>
    /// <para>
    /// A refused save does not undo the turn. A SQL history provider writes a turn's messages to the conversation as the
    /// turn ends, before this save, so the turn that lost the race is in the conversation the next turn reads, even
    /// though its session state was not saved. Two concurrent first turns of a new session each open their own
    /// conversation, and the loser's is not reachable from any record. Retrying the turn after a conflict sends the user's
    /// message again into a conversation that already holds it.
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
            ?? throw new TraconException(
                "The session has no Tracon identity. Sessions to be saved must be opened with " +
                $"{nameof(GetOrCreateSessionAsync)}.");

        var state = await agent
            .SerializeSessionAsync(session, jsonSerializerOptions: null, cancellationToken)
            .ConfigureAwait(false);

        if (_newlyOpenedSessions.TryGetValue(session, out var opened))
        {
            await opened.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                // Looked at again under the gate: a tool that changed the session's workspace may have written the first
                // record while this save serialized the state, and this save is then an update of it.
                if (_newlyOpenedSessions.TryGetValue(session, out _))
                {
                    await CreateFirstRecordAsync(agent, session, sessionId, state, cancellationToken).ConfigureAwait(false);

                    return sessionId;
                }
            }
            finally
            {
                opened.Gate.Release();
            }
        }

        var record = BuildRecord(agent, session, sessionId, state);

        if (_restoredVersions.TryGetValue(session, out var restored) &&
                 string.Equals(restored.SessionId, sessionId, StringComparison.Ordinal))
        {
            if (!await _store.TryUpdateAsync(record, restored.Version, cancellationToken).ConfigureAwait(false))
            {
                throw new TraconSessionConflictException(
                    $"Session '{sessionId}' was changed by another request while this turn was running. " +
                    "Retry again shortly.")
                {
                    SessionId = sessionId,
                };
            }

            restored.Version++;
        }
        else
        {
            // No generation applies to this write, so an unconditional one is
            // the only honest option — inventing a comparison here would
            // reject a good write. Two ways to land here, both legitimate:
            // the session was never opened by THIS manager (a caller stamped
            // an identity by hand), or it is deliberately being saved under a
            // DIFFERENT identity than it was read from (the Responses
            // endpoint's previous_response_id chaining), which writes a new
            // record rather than replacing the source one.
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return sessionId;
    }

    /// <summary>
    /// Writes the first record of a session that was just opened, if nothing has yet: the session exists in the store from
    /// the first change a run makes to what is kept under its identity.
    /// </summary>
    /// <param name="session">The session, opened with <see cref="GetOrCreateSessionAsync"/>.</param>
    /// <param name="cancellationToken">
    /// Ends the wait for another writer of the first record. The write itself is not cancelled: a record that was created
    /// and then reported as cancelled would be refused as a conflict when the run's own save came after it.
    /// </param>
    /// <returns>A task that completes when the session has a record, or when it already had one.</returns>
    /// <exception cref="TraconSessionConflictException">Another request opened the same session and wrote its record first.</exception>
    /// <exception cref="TraconSessionOwnerRequiredException">
    /// Ownership is on, this write claims it, and no identity resolved while it is required.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A run that keeps files for its session (the session workspace) writes under the session's identity while it runs, but
    /// the record is written when the run ends, and a run that ends in a failure writes none. Files then stay behind a
    /// session that does not exist: the delete, the retention policy and the erasure of a data subject all begin at the
    /// record and never reach them. Called before the first such change, it makes the record exist first; the run's own save
    /// then updates it, as it does a restored session.
    /// </para>
    /// <para>
    /// The record is the state the session has now, and its owner is the one the save would have claimed. A session that was
    /// restored from a record, or whose record is already written, is left as it is.
    /// </para>
    /// </remarks>
    internal async ValueTask EnsureRecordAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_newlyOpenedSessions.TryGetValue(session, out var opened))
        {
            return;
        }

        await opened.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_newlyOpenedSessions.TryGetValue(session, out _))
            {
                return;
            }

            var sessionId = AgentSessionIdentity.GetId(session)
                ?? throw new TraconException(
                    "The session has no Tracon identity. Sessions to be saved must be opened with " +
                    $"{nameof(GetOrCreateSessionAsync)}.");

            var state = await opened.Agent
                .SerializeSessionAsync(session, jsonSerializerOptions: null, CancellationToken.None)
                .ConfigureAwait(false);

            await CreateFirstRecordAsync(opened.Agent, session, sessionId, state, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            opened.Gate.Release();
        }
    }

    /// <summary>Writes the record of a session that has none, with the owner this write claims. Called under the session's gate.</summary>
    private async ValueTask CreateFirstRecordAsync(
        AIAgent agent,
        AgentSession session,
        string sessionId,
        JsonElement state,
        CancellationToken cancellationToken)
    {
        var record = BuildRecord(agent, session, sessionId, state);

        if (!await _store.TryCreateAsync(record, cancellationToken).ConfigureAwait(false))
        {
            throw new TraconSessionConflictException(
                $"Another request also opened session '{sessionId}' at the same time and saved it before us. " +
                "Retry again shortly.")
            {
                SessionId = sessionId,
            };
        }

        _newlyOpenedSessions.Remove(session);

        // The first write lands at generation 1; a second save within this
        // same request must replace THAT, not re-create. The owner it just
        // claimed is remembered with it, so the second save CARRIES it
        // instead of resolving the identity a second time — on a queued
        // run that second save happens in a background worker with no
        // request behind it.
        _restoredVersions.AddOrUpdate(session, new RestoredRecord(sessionId, 1, record.OwnerId));
    }

    private SessionRecord BuildRecord(AIAgent agent, AgentSession session, string sessionId, JsonElement state)
    {
        var now = _timeProvider.GetUtcNow();

        return new SessionRecord
        {
            Id = sessionId,
            AgentName = agent.Name ?? agent.Id,
            State = state,
            StateSchemaVersion = CurrentStateSchemaVersion,
            StateMafVersion = CurrentMafVersion,
            CreatedAt = now,
            UpdatedAt = now,
            TenantId = _tenantContext.TenantId,
            OwnerId = ResolveOwnerId(session, sessionId),
        };
    }

    /// <summary>
    /// Takes the session's current generation for this request before the request acts on what the session says is
    /// pending, so that of two requests that answer the same interruption only one goes on.
    /// </summary>
    /// <param name="agent">The agent the session belongs to.</param>
    /// <param name="session">The session, opened with <see cref="GetOrCreateSessionAsync"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the generation is taken.</returns>
    /// <exception cref="TraconSessionConflictException">Another request wrote the session after this one read it.</exception>
    /// <remarks>
    /// A tool call that waits for an approval, an input or a client result is pending in the history the session holds, and
    /// the session is written only when a run ends. Two requests that read the same generation therefore both find the
    /// call pending and both run it; the conflict that the later save reports comes after the tool has run twice. The claim
    /// is a save of the session as it was read: it changes nothing but the generation, and the request that arrives second
    /// is refused before it does anything. A session that this manager has just created holds nothing that is pending and is
    /// left alone.
    /// </remarks>
    public async ValueTask ClaimSessionAsync(
        AIAgent agent,
        AgentSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(session);

        if (_newlyOpenedSessions.TryGetValue(session, out _))
        {
            return;
        }

        await SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decides which user this write records as the session's owner.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <see langword="null"/> — today's behaviour, an unowned row —
    /// whenever <see cref="TraconSessionOwnershipOptions.Enabled"/> is off.
    /// That is the default, so an application that configures nothing never
    /// reaches the rest of this method.
    /// </para>
    /// <para>
    /// With ownership on there are exactly two cases, and the split is what
    /// keeps an owner from ever being silently replaced or dropped:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// The session was <strong>restored</strong> from a stored row. Its owner
    /// is CARRIED FORWARD verbatim, including a <see langword="null"/> owner on
    /// a row written before ownership existed. This covers every later write
    /// of an existing session, and it covers a session deliberately saved
    /// under a NEW identity (the Responses endpoint's
    /// <c>previous_response_id</c> chain) — a derived session inherits the
    /// source's owner, exactly as a branch does, because deriving is a copy
    /// and not a handover.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// The session is <strong>new to this manager</strong> — freshly opened,
    /// or stamped with an identity by hand. The owner is resolved from the
    /// identity pipeline, once, and this is the ONLY moment ownership is
    /// claimed.
    /// </description>
    /// </item>
    /// </list>
    /// <para>
    /// The identity comes from <see cref="IRunAttributionContext"/> and
    /// NEVER from the request body. A body field naming the owner would let
    /// any client open a session under someone else&apos;s name, which is the
    /// same forgery <c>IRunAttributionContext</c> already refuses for cost
    /// records.
    /// </para>
    /// <para>
    /// <see cref="RunAttributionReader"/> answers <see langword="null"/> for
    /// a faulty implementation and for a value that breaks
    /// <see cref="RunLabels.MaxUserIdLength"/> — it never surfaces a partial
    /// or unvalidated identity. Under ownership that <see langword="null"/>
    /// becomes a REJECTION rather than a NULL column: attribution&apos;s promise
    /// that its own failure cannot stop a run does not extend to a value the
    /// deployment has asked to be an authorization input.
    /// </para>
    /// </remarks>
    /// <param name="session">The session being saved.</param>
    /// <param name="sessionId">The identity the record is written under.</param>
    /// <returns>The owner to write, or <see langword="null"/> for an unowned row.</returns>
    /// <exception cref="TraconSessionOwnerRequiredException">
    /// Ownership is on, this write CLAIMS ownership, no identity resolved, and
    /// <see cref="TraconSessionOwnershipOptions.RequireAuthenticatedOwner"/>
    /// is on.
    /// </exception>
    private string? ResolveOwnerId(AgentSession session, string sessionId)
        => _restoredVersions.TryGetValue(session, out var restored) &&
           _ownershipOptions?.CurrentValue is { Enabled: true }
            ? restored.OwnerId
            : ClaimOwnerId(sessionId);

    /// <summary>
    /// Claims an owner for a session that has no stored one to carry forward.
    /// </summary>
    /// <remarks>
    /// Split out of <see cref="ResolveOwnerId(AgentSession, string)"/> so that
    /// <see cref="GetOrCreateSessionAsync"/> can reach it BEFORE an
    /// <c>AgentSession</c> object exists to key the carry-forward table with -
    /// the whole point of the early call is to refuse before the agent runs.
    /// </remarks>
    /// <param name="sessionId">The identity the record is written under.</param>
    /// <returns>The owner to write, or <see langword="null"/> for an unowned row.</returns>
    /// <exception cref="TraconSessionOwnerRequiredException">
    /// Ownership is on, no identity resolved, and
    /// <see cref="TraconSessionOwnershipOptions.RequireAuthenticatedOwner"/>
    /// is on.
    /// </exception>
    private string? ClaimOwnerId(string sessionId)
    {
        var options = _ownershipOptions?.CurrentValue;

        if (options is not { Enabled: true })
        {
            return null;
        }

        var userId = ResolveClaimingIdentity();

        if (userId is null && options.RequireAuthenticatedOwner)
        {
            throw new TraconSessionOwnerRequiredException(
                $"Session '{sessionId}' cannot be opened: session ownership is on and no authenticated " +
                "identity could be resolved to own it. Bind IRunAttributionContext to the application's " +
                "identity pipeline, or turn Tracon:SessionOwnership:RequireAuthenticatedOwner off to " +
                "allow unowned sessions - an unowned session is invisible to every owner-filtered listing.")
            {
                SessionId = sessionId,
            };
        }

        return userId;
    }

    /// <summary>
    /// Reads the identity that gets to claim a session, giving an OPEN ambient
    /// scope precedence over the registered attribution service.
    /// </summary>
    /// <returns>The claiming identity, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// <para>
    /// The precedence is the point, and it is inverted from what "the
    /// consumer's registration always wins" would suggest. On a background
    /// path — the queued run's worker, a scheduled run — Tracon itself
    /// opens the scope from the DURABLE JOB ENVELOPE, which is the recorded
    /// answer to "who asked for this work". A registered
    /// <see cref="IRunAttributionContext"/> is normally bound to the current
    /// HTTP request and has nothing to read on that thread; an implementation
    /// that answers anyway answers about a DIFFERENT caller than the one whose
    /// session is being written. Measured: with a consumer implementation
    /// registered, the queued run claimed the session for whichever user the
    /// worker's ambient service happened to name.
    /// </para>
    /// <para>
    /// <see cref="AmbientRunAttributionScope.IsActive"/>, not a null check on
    /// the value: a scope opened with no user is the deliberate statement
    /// "this work belongs to nobody", and falling through to the service there
    /// would let the wrong identity back in through the same door.
    /// </para>
    /// <para>
    /// This precedence governs OWNERSHIP only. Attribution's own reader is
    /// untouched, so a run's <c>user_id</c> column keeps resolving exactly as
    /// it did.
    /// </para>
    /// </remarks>
    private string? ResolveClaimingIdentity()
    {
        if (AmbientRunAttributionScope.IsActive)
        {
            // Already validated against RunLabels by Begin, so it needs no
            // second pass through RunAttributionReader.
            return AmbientRunAttributionScope.CurrentUserId;
        }

        var (userId, _) = RunAttributionReader.Read(_attributionContext);

        return userId;
    }

    /// <summary>
    /// Reads the informational version off the Microsoft Agent Framework
    /// assembly that produces <c>SerializeSessionAsync</c> output.
    /// </summary>
    /// <remarks>Same technique as <c>MetaEndpoints.ReadVersion</c> uses for Tracon's own version.</remarks>
    private static string ReadMafVersion() => AssemblyVersionText.Read(typeof(AIAgent).Assembly);

    /// <summary>What a session that was just opened carries until its first record is written.</summary>
    /// <param name="agent">The agent the session was opened with: it serializes the state of the first record.</param>
    private sealed class OpenedSession(AIAgent agent)
    {
        /// <summary>The agent the session was opened with.</summary>
        public AIAgent Agent { get; } = agent;

        /// <summary>
        /// Lets one writer at a time write the first record: the end-of-run save and the first change to the session's workspace.
        /// </summary>
        /// <remarks>No wait handle is ever taken from it, so it holds no operating system resource and needs no disposal.</remarks>
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    /// <summary>The record a restored session was read from.</summary>
    /// <param name="sessionId">The identity the state was read under.</param>
    /// <param name="version">The store generation at that moment.</param>
    /// <param name="ownerId">The owner the source row carried, or <see langword="null"/> when it was unowned.</param>
    private sealed class RestoredRecord(string sessionId, long version, string? ownerId)
    {
        /// <summary>The identity the state was read under.</summary>
        public string SessionId { get; } = sessionId;

        /// <summary>The generation this session's next write must replace.</summary>
        public long Version { get; set; } = version;

        /// <summary>
        /// The owner this session's next write must CARRY FORWARD.
        /// </summary>
        /// <remarks>
        /// The whole reason the owner is remembered here rather than
        /// re-resolved on every save. A single request saves the same session
        /// more than once on real paths — the queued run's approval hook saves
        /// before the run's own save, and the Responses endpoint restores under
        /// one identity and saves under another. Re-resolving would let the
        /// second write land a DIFFERENT owner, or no owner at all on a
        /// background continuation that has no request to read an identity
        /// from, silently dropping the session out of its owner's listing.
        /// Ownership is assigned once, at the first write, and carried from
        /// there.
        /// </remarks>
        public string? OwnerId { get; } = ownerId;
    }

    /// <summary>
    /// Deletes the session, and the copies of its content that are not part of its record: its chat history on SQL storage,
    /// the files of its workspace and the copy that conversation memory keeps.
    /// </summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if a deletion occurred.</returns>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    /// <remarks>
    /// <para>
    /// The copies go FIRST and only for a session that exists: a copy that cannot be erased fails the
    /// call with the session still in place, so a repeat call finds it and finishes the erasure. Erasing after
    /// the delete would leave a copy no repeat call could reach. The attachments of the session are not deleted
    /// here; <c>SessionDeletion</c> is the path that deletes them too.
    /// </para>
    /// <para>
    /// With a SQL provider the chat history is a conversation of its own, and only the state of the session says which
    /// one. It is read from the session's stored state (through the session's agent, or as JSON when that agent is gone or
    /// cannot be compiled) and deleted with its messages before the record goes. A state that does not have the shape the
    /// conversation id is read from leaves the history in place; the call logs it and still deletes the session. With
    /// in-memory storage the history is part of the session's state and goes with the record.
    /// </para>
    /// <para>
    /// A run that is still writing the history of the session when it is deleted writes its conversation again, with that
    /// turn alone in it, and no session names it any more: cancel or wait for the run before the delete. A run that has no
    /// session leaves a conversation of its own, which this call does not reach; the retention period for conversations
    /// does.
    /// </para>
    /// </remarks>
    public ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => DeleteSessionAsync(sessionId, null, cancellationToken);

    /// <summary>Deletes the session, running one more step between the copy and the record.</summary>
    /// <param name="sessionId">The session identity.</param>
    /// <param name="afterContentErased">
    /// A step that must finish before the record goes, run only for a session that exists. It fails the call like a
    /// copy that cannot be erased does: the record stays, so a repeat call finishes.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if a deletion occurred.</returns>
    /// <exception cref="ArgumentException"><paramref name="sessionId"/> is empty.</exception>
    internal async ValueTask<bool> DeleteSessionAsync(
        string sessionId,
        Func<CancellationToken, ValueTask>? afterContentErased,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if ((_contentErasers.Count > 0 || afterContentErased is not null)
            && await _store.GetAsync(sessionId, cancellationToken).ConfigureAwait(false) is not null)
        {
            foreach (var eraser in _contentErasers)
            {
                await eraser.EraseAsync(_tenantContext.TenantId, [sessionId], cancellationToken).ConfigureAwait(false);
            }

            if (afterContentErased is not null)
            {
                await afterContentErased(cancellationToken).ConfigureAwait(false);
            }
        }

        return await _store.DeleteAsync(sessionId, cancellationToken).ConfigureAwait(false);
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
