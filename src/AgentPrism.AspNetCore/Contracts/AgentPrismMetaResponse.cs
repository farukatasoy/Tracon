namespace AgentPrism;

/// <summary>
/// Response for <c>{prefix}/api/meta</c>. Carries the minimum information the
/// UI needs to configure itself.
/// </summary>
/// <remarks>
/// This endpoint is reachable <strong>without authentication</strong>; the UI
/// has no other way to learn which authentication method to use. Its content
/// is therefore deliberately narrow: it carries no secret, tenant data, agent
/// name, or count information.
/// </remarks>
public sealed record AgentPrismMetaResponse
{
    /// <summary>AgentPrism version.</summary>
    public required string Version { get; init; }

    /// <summary>Path prefix the endpoints are connected to. The UI builds its own calls from this.</summary>
    public required string Prefix { get; init; }

    /// <summary>Active authentication methods.</summary>
    public required AgentPrismAuthenticationMeta Authentication { get; init; }

    /// <summary>Active storage implementations.</summary>
    public required AgentPrismStorageMeta Storage { get; init; }

    /// <summary>Role permissions of the current caller.</summary>
    public required AgentPrismRoleMeta Roles { get; init; }
}

/// <summary>
/// Reports which role levels the current request's caller satisfies.
/// </summary>
/// <remarks>
/// The UI uses this field to hide edit buttons the caller has no permission
/// for; server-side authorization is still the only source of truth, this
/// field is <strong>not</strong> a security measure. If the corresponding role
/// policy (<see cref="AgentPrismPolicies"/>) is not registered in the
/// consumer's authorization configuration, the corresponding field returns
/// <see langword="true"/>: there is no role restriction, the endpoint only
/// passes through the existing three-layer protection.
/// </remarks>
public sealed record AgentPrismRoleMeta
{
    /// <summary>Whether the caller may read agents, runs, sessions, traces, and statistics.</summary>
    public required bool CanRead { get; init; }

    /// <summary>Whether the caller may, in addition to Reader, start runs, give approvals, and delete sessions.</summary>
    public required bool CanOperate { get; init; }

    /// <summary>Whether the caller may write agent definitions, add MCP servers, and manage tenants and audit trails.</summary>
    public required bool CanAdminister { get; init; }
}

/// <summary>
/// Reports which authentication layers are enabled.
/// </summary>
/// <remarks>
/// Carries only <see langword="bool"/> fields. The policy name is
/// deliberately <strong>not</strong> returned: the UI cannot do anything with
/// the name, and the name would be a configuration detail leaking from an
/// unauthenticated endpoint.
/// </remarks>
public sealed record AgentPrismAuthenticationMeta
{
    /// <summary>Whether access from outside loopback is allowed.</summary>
    public required bool AllowRemoteAccess { get; init; }

    /// <summary>Whether an <c>Authorization: Bearer</c> header is expected.</summary>
    public required bool RequiresBearerToken { get; init; }

    /// <summary>Whether an ASP.NET Core authorization policy is applied.</summary>
    public required bool RequiresAuthorizationPolicy { get; init; }
}

/// <summary>
/// Reports which storage implementations are active.
/// </summary>
/// <remarks>
/// In-memory stores are a supported mode, not a test helper.
/// They do have limits, however — process lifetime and single node — and the
/// UI should be able to show this to the user.
/// </remarks>
public sealed record AgentPrismStorageMeta
{
    /// <summary>
    /// Whether all three stores are persistent. Returns
    /// <see langword="false"/> if any is in-memory.
    /// </summary>
    public required bool Persistent { get; init; }

    /// <summary>Type name of the agent definition store.</summary>
    public required string AgentDefinitionStore { get; init; }

    /// <summary>Type name of the run store.</summary>
    public required string RunStore { get; init; }

    /// <summary>Type name of the session store.</summary>
    public required string SessionStore { get; init; }

    /// <summary>Type name of the job queue store.</summary>
    public required string JobStore { get; init; }

    /// <summary>
    /// Whether the background job worker is running in this process. If
    /// <see langword="false"/>, the queue can still be written to and read
    /// from; only this process does not lease jobs.
    /// </summary>
    public required bool JobWorkerEnabled { get; init; }
}
