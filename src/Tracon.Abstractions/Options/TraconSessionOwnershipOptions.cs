namespace Tracon;

/// <summary>
/// Turns per-user session ownership on, a second boundary drawn UNDER the
/// tenant.
/// </summary>
/// <remarks>
/// <para>
/// Read from the <c>Tracon:SessionOwnership</c> configuration section.
/// </para>
/// <para>
/// <strong>Default off, and off means nothing changes.</strong> An application
/// that registers nothing keeps exactly its current behaviour: no owner is
/// written, no listing is filtered, and <c>sessions.owner_id</c> stays NULL.
/// The whole feature is inert until a deployment asks for it.
/// </para>
/// <para>
/// This type lives in the abstractions package because the ownership rule is
/// enforced in four places that do not see each other — the session manager
/// that stamps the owner (<c>Tracon.Core</c>), the listing endpoint that
/// narrows the query (<c>Tracon.AspNetCore</c>), the SQL stores that carry
/// the column (<c>Tracon.PostgreSql</c>, <c>Tracon.SqlServer</c>,
/// <c>Tracon.Sqlite</c>) and <see cref="ISessionStore"/>'s own contract,
/// whose <see cref="SessionRecord.OwnerId"/> documentation cannot describe the
/// rule without naming this type. One shared type keeps them from drifting
/// apart; the same reason <see cref="TraconMcpSecurityOptions"/> lives
/// here.
/// </para>
/// <para>
/// Ownership is <strong>not</strong> a replacement for
/// <see cref="IRunAuthorizationHandler"/>. The handler answers a question
/// Tracon asks; this option lets Tracon answer part of it itself, so a
/// consumer no longer has to keep its own session-to-user table just to build
/// a "my conversations" list. With ownership on, a handler that used to reject
/// a whole listing to keep users apart no longer needs to — the listing
/// arrives already narrowed.
/// </para>
/// </remarks>
public sealed class TraconSessionOwnershipOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "Tracon:SessionOwnership";

    /// <summary>
    /// Gets or sets whether a session records the user it belongs to. Default
    /// <see langword="false"/>: nothing changes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With this on, a session opened through the HTTP surface is stamped with
    /// the identity <see cref="IRunAttributionContext"/> resolves, and
    /// <c>GET /api/sessions</c> returns only the caller's own sessions unless
    /// the caller satisfies <see cref="ManagementPolicy"/>.
    /// </para>
    /// <para>
    /// Rows written while it was off keep a <see langword="null"/> owner
    /// forever — Tracon cannot invent an owner for a session it did not
    /// watch being opened. Those rows disappear from owner-filtered listings
    /// and stay in management listings. Turning the option on is therefore
    /// safe for existing data, but it is not retroactive.
    /// </para>
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether a session write is REJECTED when no authenticated
    /// identity can be resolved to own it. Default <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only consulted while <see cref="Enabled"/> is on. The rejection is
    /// <see cref="TraconSessionOwnerRequiredException"/>, which the HTTP
    /// surface answers with <c>403</c>.
    /// </para>
    /// <para>
    /// Turning this off is a deliberate weakening, not a convenience: an
    /// unowned session is invisible to every owner-filtered listing, so the
    /// caller writes a conversation they can never list again. It exists for
    /// the mixed deployment that runs owned and unowned traffic side by side
    /// through the same endpoints during a migration.
    /// </para>
    /// </remarks>
    public bool RequireAuthenticatedOwner { get; set; } = true;

    /// <summary>
    /// Gets or sets the authorization policy whose holders see the WHOLE
    /// tenant's sessions instead of only their own. Default
    /// <c>TraconPolicies.Operator</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Evaluated per request. A caller who satisfies it gets today's
    /// unfiltered tenant listing — including the unowned rows written before
    /// ownership was turned on, which is the only way those rows stay
    /// discoverable. Every other caller gets a listing narrowed to their own
    /// identity.
    /// </para>
    /// <para>
    /// <strong>Fail-closed.</strong> If the policy is not registered in the
    /// application's authorization configuration, or evaluating it throws, the
    /// caller is treated as an ordinary user and the listing is narrowed. A
    /// missing policy must not hand out an unfiltered listing — that is the
    /// one failure mode this whole option exists to prevent. Set it to
    /// <see langword="null"/> or an empty string to state deliberately that
    /// NO caller gets an unfiltered listing over HTTP.
    /// </para>
    /// <para>
    /// It grants no access to another user's session: reading, deleting and
    /// branching a session that is OWNED by somebody else answers <c>404</c>
    /// regardless of policy, because a management role is not a reason to leak
    /// one user's conversation to another. The asymmetry is deliberate — a
    /// listing is an operation with no single identity to leak; an owned
    /// session is a resource whose content belongs to one user.
    /// </para>
    /// <para>
    /// The one individual access it does govern is
    /// <see cref="RefuseUnownedSessions"/>: an UNOWNED row belongs to nobody,
    /// so there is no user whose conversation could leak, and refusing it to
    /// the same caller who can already see it in the management listing would
    /// leave support looking at a row it cannot open. That exemption covers
    /// reading a session, never continuing one — see that property.
    /// </para>
    /// <para>
    /// The default is written as a literal because
    /// <c>TraconPolicies</c> lives in <c>Tracon.AspNetCore</c>, which
    /// this package cannot see, while <c>Tracon.Core</c> has to read the
    /// two options above. The two spellings are held together by a test rather
    /// than by the compiler; use the constant, not the literal, when you name
    /// this policy in your own code.
    /// </para>
    /// </remarks>
    public string? ManagementPolicy { get; set; } = DefaultManagementPolicy;

    /// <summary>
    /// Gets or sets whether access to an EXISTING session row that carries no
    /// owner is REFUSED. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only consulted while <see cref="Enabled"/> is on; on its own this
    /// setting does nothing at all.
    /// </para>
    /// <para>
    /// <strong>What it changes.</strong> Rows written before ownership was
    /// turned on carry a <see langword="null"/> owner forever, and ownership is
    /// not retroactive. By default those rows keep the tenant-wide
    /// reachability they had the day before the flip: they vanish from every
    /// owner-filtered LISTING but are still readable one by one by anybody in
    /// the tenant. Turn this on and they are refused instead — read, delete,
    /// branch and voice answer the endpoint's own "session not found", and a
    /// run that names one is refused with <c>403</c>. Listing behaviour does
    /// not change; it already excluded them.
    /// </para>
    /// <para>
    /// <strong>What it deliberately does NOT change.</strong> A session that
    /// does not exist yet is untouched: the first turn still opens it and
    /// claims it, which is how every owned session is born. "Not created yet"
    /// and "created without an owner" are different rows and get different
    /// answers.
    /// </para>
    /// <para>
    /// A caller who satisfies <see cref="ManagementPolicy"/> still reaches an
    /// unowned row through the session resource endpoints, so support and
    /// audit keep the access they had — the same reason those rows stay in the
    /// management listing. That exemption covers reading a session, not
    /// continuing one: a run that names an unowned session is refused for every
    /// caller, because starting a run appends to the conversation as somebody
    /// else.
    /// </para>
    /// <para>
    /// Default off because turning it on strands every conversation that was
    /// live at the moment ownership was enabled. Turn it on once those
    /// conversations no longer matter, or in a deployment that enabled
    /// ownership from its first day and therefore has no unowned rows at all.
    /// </para>
    /// </remarks>
    public bool RefuseUnownedSessions { get; set; }


    /// <summary>
    /// The literal spelling of <c>TraconPolicies.Operator</c>, repeated
    /// here because this package cannot reference the package that declares
    /// it. Cross-checked by a test; never edit one side alone.
    /// </summary>
    internal const string DefaultManagementPolicy = "Tracon.Operator";
}
