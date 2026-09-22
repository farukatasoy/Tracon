using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
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
/// Microsoft Agent Framework's pre-release <c>AgentSessionStore</c> abstraction
/// (<c>Microsoft.Agents.AI.Hosting</c>) is wired to this class by delegation.
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
    private readonly TimeProvider _timeProvider;
    private readonly IRunAttributionContext? _attributionContext;
    private readonly IOptionsMonitor<TraconSessionOwnershipOptions>? _ownershipOptions;

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

    private static readonly object NewSessionMarker = new();

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
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public AgentSessionManager(
        ISessionStore store,
        ITenantContext tenantContext,
        TimeProvider? timeProvider = null,
        IRunAttributionContext? attributionContext = null,
        IOptionsMonitor<TraconSessionOwnershipOptions>? ownershipOptions = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _store = store;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _attributionContext = attributionContext;
        _ownershipOptions = ownershipOptions;
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

            session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
            _newlyOpenedSessions.Add(session, NewSessionMarker);
        }
        else
        {
            if (record.StateSchemaVersion > CurrentStateSchemaVersion)
            {
                throw new TraconException(
                    $"Session '{sessionId}' was written with Tracon schema generation {record.StateSchemaVersion}; " +
                    $"this Tracon version can read up to generation {CurrentStateSchemaVersion}. " +
                    "Update the Tracon packages.");
            }

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
        }

        if (record is not null)
        {
            _restoredVersions.AddOrUpdate(session, new RestoredRecord(sessionId, record.Version, record.OwnerId));
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
    /// <exception cref="TraconException">
    /// The session has no identity because it was not opened with <see cref="GetOrCreateSessionAsync"/>.
    /// </exception>
    /// <exception cref="TraconSessionConflictException">
    /// This was the FIRST save of a session that <see cref="GetOrCreateSessionAsync"/>
    /// opened FRESH, and a concurrent request saved the same NEW session
    /// identity before we did.
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

        var now = _timeProvider.GetUtcNow();

        var record = new SessionRecord
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

        if (_newlyOpenedSessions.TryGetValue(session, out _))
        {
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
        else if (_restoredVersions.TryGetValue(session, out var restored) &&
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
