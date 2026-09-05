namespace AgentPrism;

/// <summary>
/// Decides whether a caller may start a run or read/write a session.
/// </summary>
/// <remarks>
/// <para>
/// AgentPrism draws ownership at the TENANT level; it never learns which user
/// inside a tenant a session or a run belongs to. Without this handler, every
/// <c>Operator</c> in a tenant can start a run as, read, and delete every
/// other user's session in the same tenant. This interface does not teach
/// AgentPrism that boundary — it asks the consumer to enforce their own.
/// </para>
/// <para>
/// The default implementation (<c>AllowAllRunAuthorizationHandler</c>,
/// registered with <c>TryAdd</c>) allows every call, so an installation that
/// registers nothing keeps today's behavior exactly. A consumer replaces the
/// registration to enforce their own rule.
/// </para>
/// <para>
/// If this handler throws, the call is <strong>denied</strong> (fail-closed).
/// A gate that fails open on an exception is not a gate. The same rule
/// <see cref="IToolAuthorizationHandler"/> follows.
/// </para>
/// <para>
/// The implementation must be a <strong>singleton</strong>, for the same
/// reason as <see cref="IRunAttributionContext"/>: singleton services take a
/// dependency on it, and a scoped registration would be a captive dependency.
/// Resolve per-request state through <c>IHttpContextAccessor</c>.
/// </para>
/// <para>
/// <strong>Tenant mode: expected tenant.</strong> Every request carries its own
/// <c>TenantId</c>, resolved by the caller before the handler is invoked; the
/// handler never reads <see cref="ITenantContext"/> itself. This is deliberate:
/// a queued run's authorization is checked before the tenant's ambient scope
/// (<c>AmbientTenantScope</c>) is necessarily active.
/// </para>
/// <para>
/// <strong>Delivery guarantee: no delivery guarantee applies.</strong> This is a
/// synchronous decision, not a retried or queued operation — each call is made
/// once, on the request thread, and its result is not persisted or replayed.
/// </para>
/// </remarks>
public interface IRunAuthorizationHandler
{
    /// <summary>Decides whether the run described by <paramref name="request"/> may start.</summary>
    /// <param name="request">The run being authorized.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision.</returns>
    ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Decides whether the session access described by <paramref name="request"/> may proceed.</summary>
    /// <param name="request">The session access being authorized.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision.</returns>
    ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes one run awaiting an authorization decision.</summary>
public sealed record RunAuthorizationRequest
{
    /// <summary>Gets the tenant the run would belong to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the name of the agent (or workflow) the run would start, or
    /// <see langword="null"/> when the request is not about starting one.
    /// </summary>
    /// <remarks>
    /// Always populated for <see cref="RunAccess.Start"/>. For every other
    /// <see cref="RunAccess"/> value it carries the agent name of the run the
    /// resource belongs to when that is known, and <see langword="null"/> when
    /// it is not (a list, or an attachment uploaded before any run existed).
    /// </remarks>
    public string? AgentName { get; init; }

    /// <summary>
    /// Gets the session the run would continue, or <see langword="null"/> for
    /// a sessionless run.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the calling user, or <see langword="null"/> when unknown.</summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets the run the request is about, or <see langword="null"/> when there
    /// is no single run.
    /// </summary>
    /// <remarks>
    /// Populated for every resource access (reading a run, canceling it,
    /// scoring it, its attachments and its approvals) and for a replay, where
    /// it identifies the <em>source</em> run whose recorded input is about to
    /// run again. It is <see langword="null"/> for a plain
    /// <see cref="RunAccess.Start"/> — the run does not exist yet — and for
    /// list operations.
    /// </remarks>
    public Guid? RunId { get; init; }

    /// <summary>Gets the kind of access being requested.</summary>
    public required RunAccess Access { get; init; }
}

/// <summary>Describes one session access awaiting an authorization decision.</summary>
public sealed record SessionAuthorizationRequest
{
    /// <summary>Gets the tenant the session belongs to.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Gets the identity of the session being accessed, or <see langword="null"/>
    /// for <see cref="SessionAccess.List"/> — a list has no single session identity.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the calling user, or <see langword="null"/> when unknown.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the kind of access being requested.</summary>
    public required SessionAccess Access { get; init; }
}

/// <summary>The kind of access a <see cref="RunAuthorizationRequest"/> asks about.</summary>
/// <remarks>
/// The numeric values are <strong>not</strong> a persistence contract. A
/// <see cref="RunAuthorizationRequest"/> is decided synchronously on the
/// request thread and its result is never stored or replayed, so no row, blob,
/// or message anywhere holds one of these numbers. Members may therefore be
/// renumbered or removed like any other public enum member, under the ordinary
/// source-compatibility rules and nothing stricter.
/// </remarks>
public enum RunAccess
{
    /// <summary>Starting a new run, including replaying a recorded one.</summary>
    Start = 0,

    /// <summary>
    /// Reading a run: its summary, its tree, its event stream, its recorded
    /// input, its span tree, its tool calls, and the scores written for it.
    /// </summary>
    Read = 1,

    /// <summary>Requesting cancellation of a run.</summary>
    Cancel = 2,

    /// <summary>Writing or deleting a score for a run.</summary>
    /// <remarks>
    /// Deliberately separate from <see cref="Cancel"/>: scoring a run does not
    /// stop it, and binding both to one decision would force a consumer to
    /// choose a permission that is either too wide or too narrow. Reading
    /// scores is <see cref="Read"/>.
    /// </remarks>
    Feedback = 3,

    /// <summary>Uploading, downloading, listing, or deleting an attachment.</summary>
    Attachment = 4,

    /// <summary>Listing, reading, or deciding an approval request.</summary>
    Approval = 5,
}

/// <summary>The kind of access a <see cref="SessionAuthorizationRequest"/> asks about.</summary>
/// <remarks>
/// As with <see cref="RunAccess"/>, the numeric values are <strong>not</strong>
/// a persistence contract: the decision is synchronous and is never stored or
/// replayed.
/// </remarks>
public enum SessionAccess
{
    /// <summary>Reading a session's metadata and chat history.</summary>
    Read = 0,

    /// <summary>Listing sessions.</summary>
    List = 1,

    /// <summary>Deleting a session.</summary>
    Delete = 2,

    /// <summary>Branching a session's conversation into a new session.</summary>
    Branch = 3,

    /// <summary>Opening a real-time voice conversation over the session.</summary>
    /// <remarks>
    /// A voice session that does not exist yet is <strong>not</strong> an
    /// error: the first turn opens it. The handler is still asked, so a
    /// consumer decides for itself whether a caller may open a new
    /// conversation under that id.
    /// </remarks>
    Voice = 4,
}

/// <summary>The outcome of an authorization decision.</summary>
public sealed record RunAuthorizationResult
{
    /// <summary>Gets whether the call may proceed.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// Gets the text returned to the caller as the denial reason. <see langword="null"/> when allowed.
    /// </summary>
    /// <remarks>
    /// This text reaches the HTTP response's <c>ProblemDetails.Detail</c> field, so it carries no secret.
    /// </remarks>
    public string? Reason { get; init; }

    /// <summary>Creates an allowing result.</summary>
    /// <returns>A result with <see cref="IsAllowed"/> set to <see langword="true"/>.</returns>
    public static RunAuthorizationResult Allow() => new() { IsAllowed = true };

    /// <summary>Creates a denying result.</summary>
    /// <param name="reason">The text returned to the caller as the denial reason.</param>
    /// <returns>A result with <see cref="IsAllowed"/> set to <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is empty or whitespace.</exception>
    public static RunAuthorizationResult Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new RunAuthorizationResult { IsAllowed = false, Reason = reason };
    }
}
