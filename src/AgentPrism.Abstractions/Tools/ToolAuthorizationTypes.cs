namespace AgentPrism;

/// <summary>
/// Decides whether a tenant/user may call a specific tool.
/// </summary>
/// <remarks>
/// <para>
/// This is separate from approval (<see cref="ToolDescriptor.RequiresApproval"/>).
/// Approval asks a human "is this call okay this time"; this interface asks
/// the installation's own policy "can this caller call this tool at all".
/// Both can apply to the same tool; when they do, authorization runs first —
/// asking a human to approve a call the caller could never make anyway is
/// backwards.
/// </para>
/// <para>
/// The default implementation (<c>AllowAllToolAuthorizationHandler</c>,
/// registered with <c>TryAdd</c>) allows every call, so an installation that
/// registers nothing keeps today's behavior exactly. A consumer replaces the
/// registration to enforce its own rule.
/// </para>
/// <para>
/// 🚨 If this handler throws, the call is <strong>denied</strong> (fail-closed).
/// A gate that fails open on an exception is not a gate.
/// </para>
/// </remarks>
public interface IToolAuthorizationHandler
{
    /// <summary>Decides whether the call described by <paramref name="request"/> may proceed.</summary>
    /// <param name="request">The call being authorized.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision.</returns>
    ValueTask<ToolAuthorizationResult> AuthorizeAsync(
        ToolAuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes one tool call awaiting an authorization decision.</summary>
public sealed record ToolAuthorizationRequest
{
    /// <summary>Gets the tool name.</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the tool's effect class.</summary>
    public required ToolEffect Effect { get; init; }

    /// <summary>
    /// Gets the permission name the tool declared, or <see langword="null"/>
    /// when the tool declared none.
    /// </summary>
    /// <remarks>
    /// This is an <strong>opaque string</strong>. AgentPrism does not resolve,
    /// validate, or store its meaning anywhere but the audit trail — only the
    /// handler interprets it.
    /// </remarks>
    public string? RequiredPermission { get; init; }

    /// <summary>Gets the calling run's tenant.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the calling run's user, or <see langword="null"/> when unknown.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the identity of the run making the call.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the name of the agent making the call.</summary>
    public required string AgentName { get; init; }
}

/// <summary>The outcome of an authorization decision.</summary>
public sealed record ToolAuthorizationResult
{
    /// <summary>Gets whether the call may proceed.</summary>
    public required bool IsAllowed { get; init; }

    /// <summary>
    /// Gets the text returned to the model as the tool's result when the call
    /// is denied. <see langword="null"/> when allowed.
    /// </summary>
    /// <remarks>
    /// This text is sent to the model, not shown to the user directly — the
    /// same rule K-232 applies to every other model-facing string. It carries
    /// no secret.
    /// </remarks>
    public string? Reason { get; init; }

    /// <summary>Creates an allowing result.</summary>
    /// <returns>A result with <see cref="IsAllowed"/> set to <see langword="true"/>.</returns>
    public static ToolAuthorizationResult Allow() => new() { IsAllowed = true };

    /// <summary>Creates a denying result.</summary>
    /// <param name="reason">The text returned to the model as the tool's result.</param>
    /// <returns>A result with <see cref="IsAllowed"/> set to <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="reason"/> is empty or whitespace.</exception>
    public static ToolAuthorizationResult Deny(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new ToolAuthorizationResult { IsAllowed = false, Reason = reason };
    }
}
