namespace AgentPrism;

/// <summary>
/// Turns per-user session ownership on, a second boundary drawn UNDER the
/// tenant.
/// </summary>
/// <remarks>
/// <para>
/// Read from the <c>AgentPrism:SessionOwnership</c> configuration section.
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
/// that stamps the owner (<c>AgentPrism.Core</c>), the listing endpoint that
/// narrows the query (<c>AgentPrism.AspNetCore</c>), the SQL stores that carry
/// the column (<c>AgentPrism.PostgreSql</c>, <c>AgentPrism.SqlServer</c>,
/// <c>AgentPrism.Sqlite</c>) and <see cref="ISessionStore"/>'s own contract,
/// whose <see cref="SessionRecord.OwnerId"/> documentation cannot describe the
/// rule without naming this type. One shared type keeps them from drifting
/// apart; the same reason <see cref="AgentPrismMcpSecurityOptions"/> lives
/// here.
/// </para>
/// <para>
/// Ownership is <strong>not</strong> a replacement for
/// <see cref="IRunAuthorizationHandler"/>. The handler answers a question
/// AgentPrism asks; this option lets AgentPrism answer part of it itself, so a
/// consumer no longer has to keep its own session-to-user table just to build
/// a "my conversations" list. With ownership on, a handler that used to reject
/// a whole listing to keep users apart no longer needs to — the listing
/// arrives already narrowed.
/// </para>
/// </remarks>
public sealed class AgentPrismSessionOwnershipOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "AgentPrism:SessionOwnership";

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
    /// forever — AgentPrism cannot invent an owner for a session it did not
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
    /// <see cref="AgentPrismSessionOwnerRequiredException"/>, which the HTTP
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
    /// <c>AgentPrismPolicies.Operator</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Evaluated per request, on the listing endpoint only. A caller who
    /// satisfies it gets today's unfiltered tenant listing — including the
    /// unowned rows written before ownership was turned on, which is the only
    /// way those rows stay reachable. Every other caller gets a listing
    /// narrowed to their own identity.
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
    /// This only ever governs what a LISTING returns. It grants no access to
    /// an individual session: reading, deleting and branching another owner's
    /// session answer <c>404</c> regardless of policy, because a management
    /// role is not a reason to leak one user's conversation to another. The
    /// asymmetry is deliberate — a listing is an operation with no single
    /// identity to leak, and it is the only path by which an unowned legacy
    /// row stays reachable at all; an individual session is a resource whose
    /// content belongs to one user.
    /// </para>
    /// <para>
    /// The default is written as a literal because
    /// <c>AgentPrismPolicies</c> lives in <c>AgentPrism.AspNetCore</c>, which
    /// this package cannot see, while <c>AgentPrism.Core</c> has to read the
    /// two options above. The two spellings are held together by a test rather
    /// than by the compiler; use the constant, not the literal, when you name
    /// this policy in your own code.
    /// </para>
    /// </remarks>
    public string? ManagementPolicy { get; set; } = DefaultManagementPolicy;

    /// <summary>
    /// The literal spelling of <c>AgentPrismPolicies.Operator</c>, repeated
    /// here because this package cannot reference the package that declares
    /// it. Cross-checked by a test; never edit one side alone.
    /// </summary>
    internal const string DefaultManagementPolicy = "AgentPrism.Operator";
}
