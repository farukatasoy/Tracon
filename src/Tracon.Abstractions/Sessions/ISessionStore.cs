using System.Text.Json;

namespace Tracon;

/// <summary>The store for serialized agent sessions.</summary>
/// <remarks>
/// <para>
/// Session state is the serialized form of Microsoft Agent Framework's
/// <c>AgentSession</c> object and is treated as <strong>opaque</strong>. Its
/// content is not interpreted; it is only stored and restored.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins. An implementation must
/// be safe under concurrent calls and must not capture or depend on a
/// scoped service. <see cref="TryCreateAsync"/> and
/// <see cref="TryUpdateAsync"/> specifically must be GENUINELY atomic, not
/// check-then-act — see those members' own remarks. Between them they cover
/// the whole lifetime of a session: the first write and every later one.
/// </para>
/// </remarks>
public interface ISessionStore
{
    /// <summary>Saves the session. Overwrites an existing record with the same identifier.</summary>
    /// <param name="record">The session to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    ValueTask SaveAsync(SessionRecord record, CancellationToken cancellationToken = default);

    /// <summary>Creates a new session record only if it does NOT already exist.</summary>
    /// <remarks>
    /// <para>
    /// When two concurrent calls arrive with the same
    /// <see cref="SessionRecord.Id"/>, ONLY one must return
    /// <see langword="true"/>; the loser must get <see langword="false"/> and
    /// read the winner's record with <see cref="GetAsync"/>.
    /// </para>
    /// <para>
    /// The default implementation is NOT ATOMIC (check-then-create) — it
    /// exists only so old stores that have not yet overridden this method
    /// keep compiling. The real stores (<c>SqlSessionStore</c>,
    /// <c>InMemorySessionStore</c>) override this method with a GENUINELY
    /// atomic implementation. Without atomicity, two concurrent
    /// first requests to the same NEW session, unaware of each other,
    /// generate two different conversation identifiers; the second
    /// <see cref="SaveAsync"/> unconditionally overwrites the first, and the
    /// loser's messages become silently unreachable.
    /// </para>
    /// </remarks>
    /// <param name="record">The session to create.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if created; <see langword="false"/> if a record with the same identifier already exists.
    /// </returns>
    async ValueTask<bool> TryCreateAsync(SessionRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (await GetAsync(record.Id, cancellationToken).ConfigureAwait(false) is not null)
        {
            return false;
        }

        await SaveAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Replaces an EXISTING session record, but only if it has not changed
    /// since it was read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sibling of <see cref="TryCreateAsync"/>, and the reason both
    /// exist: <see cref="TryCreateAsync"/> makes the FIRST write of a session
    /// safe against a concurrent first write; this method makes EVERY LATER
    /// write safe against a concurrent later write. <see cref="SaveAsync"/>
    /// overwrites unconditionally and cannot answer either question.
    /// </para>
    /// <para>
    /// The write succeeds only when the stored record's
    /// <see cref="SessionRecord.Version"/> still equals
    /// <paramref name="expectedVersion"/> — the value the caller read. On
    /// success the stored version is incremented; the caller's own
    /// <paramref name="record"/> is not mutated.
    /// </para>
    /// <para>
    /// The default implementation is NOT ATOMIC (read-then-write). It exists
    /// only so a store written before this member keep compiling; the real
    /// stores (<c>SqlSessionStore</c>, <c>InMemorySessionStore</c>) override
    /// it with a genuinely atomic compare-and-swap. Without atomicity two
    /// concurrent turns on the SAME EXISTING session both report success and
    /// the loser's turn is silently overwritten — the defect this member was
    /// added to close.
    /// </para>
    /// </remarks>
    /// <param name="record">The new state of the session.</param>
    /// <param name="expectedVersion">
    /// The <see cref="SessionRecord.Version"/> the caller read. A record read
    /// through <see cref="GetAsync"/> or <see cref="QueryAsync"/> carries it.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if the record was replaced;
    /// <see langword="false"/> if it no longer exists or another writer
    /// changed it first.
    /// </returns>
    async ValueTask<bool> TryUpdateAsync(
        SessionRecord record,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var stored = await GetAsync(record.Id, cancellationToken).ConfigureAwait(false);

        if (stored is null || stored.Version != expectedVersion)
        {
            return false;
        }

        await SaveAsync(record with { Version = expectedVersion + 1 }, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Fetches the session.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session; <see langword="null"/> if it does not exist.</returns>
    ValueTask<SessionRecord?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the tenant that owns a session identifier, WITHOUT applying
    /// the ambient tenant filter read from <see cref="ITenantContext"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="GetAsync"/> is filtered by the ambient tenant; because of
    /// this, it can never answer "does this identifier belong to ANOTHER
    /// tenant" — the caller is already inside their own tenant's context, and
    /// another tenant's record is NEVER VISIBLE from that context, so the
    /// result is always <see langword="null"/>. This is exactly
    /// why the OpenAI-compatible endpoints' cross-tenant ownership check was
    /// dead code — the rejection branch never fired, the identifier was
    /// silently treated as "never used" and a new session was opened.
    /// </para>
    /// <para>
    /// The default implementation calls <see cref="GetAsync"/> — so it
    /// CARRIES THE BUG ABOVE and can never correctly answer the cross-tenant
    /// question. This exists only so old/custom stores that have not yet
    /// overridden this method keep compiling. The real stores
    /// (<c>SqlSessionStore</c>, <c>InMemorySessionStore</c>) override this
    /// method with a genuinely correct implementation that is INDEPENDENT of the tenant.
    /// </para>
    /// </remarks>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The identifier of the owning tenant if the identifier has been used;
    /// <see langword="null"/> if the identifier has never been used.
    /// </returns>
    async ValueTask<string?> GetOwnerTenantIdAsync(string sessionId, CancellationToken cancellationToken = default)
        => (await GetAsync(sessionId, cancellationToken).ConfigureAwait(false))?.TenantId;

    /// <summary>Deletes the session.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true"/> if the delete happened.</returns>
    ValueTask<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Lists sessions by filter. The most recently updated is returned first.</summary>
    /// <param name="query">The filter.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sessions.</returns>
    ValueTask<IReadOnlyList<SessionRecord>> QueryAsync(SessionQuery query, CancellationToken cancellationToken = default);
}

/// <summary>A stored session.</summary>
public sealed record SessionRecord
{
    /// <summary>The session identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The agent the session belongs to.</summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// The serialized session state. Microsoft Agent Framework's
    /// <c>SerializeSessionAsync</c> output, treated as opaque.
    /// </summary>
    public required JsonElement State { get; init; }

    /// <summary>The creation time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The last-updated time (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The tenant identifier.</summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// The user the session belongs to, or <see langword="null"/> when the
    /// session is unowned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a SECOND, NARROWER boundary drawn UNDER the tenant, not a
    /// replacement for it: <see cref="TenantId"/> still answers "whose data is
    /// this", and this answers "which user inside that tenant". Do not confuse
    /// it with <see cref="ISessionStore.GetOwnerTenantIdAsync"/>, whose "owner"
    /// is the owning TENANT.
    /// </para>
    /// <para>
    /// The value is an <strong>opaque string</strong> with the same meaning
    /// <see cref="IRunAttributionContext.UserId"/> carries, bounded by
    /// <see cref="RunLabels.MaxUserIdLength"/>. Tracon neither resolves nor
    /// validates it; the consumer decides what it identifies. It is
    /// <strong>never read from a request body</strong> — a body field naming
    /// the owner would let any client write a session under someone else's
    /// name.
    /// </para>
    /// <para>
    /// <see langword="null"/> means the session is unowned: every row written
    /// before ownership tracking existed, and every row written by an
    /// application that leaves
    /// <see cref="TraconSessionOwnershipOptions.Enabled"/> off. An unowned
    /// row is never returned by an owner-filtered
    /// <see cref="SessionQuery.OwnerId"/> query; it stays visible to a
    /// management listing that applies no owner filter.
    /// </para>
    /// <para>
    /// <strong>Assigned once.</strong> A store must not let a later write clear
    /// an owner that is already set: the identity that opened a session keeps
    /// it, and "set → unset" is never a legitimate transition here. Writing a
    /// DIFFERENT owner onto an owned row is equally not a transition Tracon
    /// performs — a derived session (a branch, a Responses chain) carries the
    /// SOURCE session's owner, not the caller's.
    /// </para>
    /// </remarks>
    public string? OwnerId { get; init; }

    /// <summary>The Tracon schema generation that wrote <c>State</c>.</summary>
    /// <remarks>
    /// <para>
    /// Every row carries a value; Tracon has stamped this generation on
    /// every session since the very first release, so there is no
    /// "unstamped" era for this field the way there is for
    /// <c>StateMafVersion</c>. It advances only when Tracon
    /// changes how it structures the stored row, never when the Microsoft
    /// Agent Framework version changes.
    /// </para>
    /// <para>
    /// A record built to be saved does not need to set this — the store
    /// stamps the current generation regardless of what the caller provides.
    /// </para>
    /// </remarks>
    public int StateSchemaVersion { get; init; } = 1;

    /// <summary>
    /// The Microsoft Agent Framework package version that produced
    /// <c>State</c>.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> means the row was written before this field
    /// existed. Tracon does not promise that a session written by one
    /// Microsoft Agent Framework version can be restored by a different one;
    /// this value lets a failed restore report exactly which version wrote
    /// the state instead of guessing.
    /// </remarks>
    public string? StateMafVersion { get; init; }

    /// <summary>
    /// The record's write generation, used for optimistic concurrency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A record read from a store carries the stored value; a record built to
    /// be written carries whatever the caller sets, and stores ignore it on
    /// <see cref="ISessionStore.SaveAsync"/> and
    /// <see cref="ISessionStore.TryCreateAsync"/> — only
    /// <see cref="ISessionStore.TryUpdateAsync"/> reads it, through its own
    /// <c>expectedVersion</c> parameter.
    /// </para>
    /// <para>
    /// The first stored version is 1. A row written before this field existed
    /// reads back as 1 as well, so a session that survives the upgrade takes
    /// part in concurrency control from its next write onward.
    /// </para>
    /// </remarks>
    public long Version { get; init; }
}

/// <summary>A filter for querying the session list.</summary>
public sealed record SessionQuery
{
    /// <summary>Fetches only this agent's sessions.</summary>
    public string? AgentName { get; init; }

    /// <summary>Fetches only this tenant's sessions.</summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Fetches only this owner's sessions. <see langword="null"/> applies no
    /// owner filter at all and lists the whole tenant, which is what a
    /// management listing wants.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The filter is applied BEFORE <see cref="Skip"/> and
    /// <see cref="Take"/>.</strong> A store that filters the page it already
    /// fetched returns short pages, empty pages, and — worst — lets a caller
    /// infer how many sessions OTHER users hold from the gaps. Paging runs over
    /// the already-narrowed set, so the paging contract stays intact and no
    /// count leaks.
    /// </para>
    /// <para>
    /// A row whose <see cref="SessionRecord.OwnerId"/> is <see langword="null"/>
    /// is NOT matched by any owner filter. Unowned rows are not "everyone's";
    /// they are nobody's.
    /// </para>
    /// </remarks>
    public string? OwnerId { get; init; }

    /// <summary>The number of records to skip.</summary>
    public int Skip { get; init; }

    /// <summary>The maximum number of records to fetch.</summary>
    public int Take { get; init; } = 50;
}
