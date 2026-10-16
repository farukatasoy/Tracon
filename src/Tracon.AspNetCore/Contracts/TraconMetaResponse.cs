using System.ComponentModel;

namespace Tracon;

/// <remarks>
/// This endpoint is reachable <strong>without authentication</strong>; the UI
/// has no other way to learn which authentication method to use. Its content
/// is therefore deliberately narrow: it carries no secret, tenant data, agent
/// name, or count information.
/// </remarks>
[Description(
    "Response for `{prefix}/api/meta`. Carries the minimum information the\n" +
    "UI needs to configure itself.")]
internal sealed record TraconMetaResponse
{
    [Description("Tracon version.")]
    public required string Version { get; init; }

    [Description("Path prefix the endpoints are connected to. The UI builds its own calls from this.")]
    public required string Prefix { get; init; }

    [Description("Active authentication methods.")]
    public required TraconAuthenticationMeta Authentication { get; init; }

    [Description("Active storage implementations.")]
    public required TraconStorageMeta Storage { get; init; }

    [Description("Role permissions of the current caller.")]
    public required TraconRoleMeta Roles { get; init; }
}

/// <remarks>
/// The UI uses this field to hide edit buttons the caller has no permission
/// for; server-side authorization is still the only source of truth, this
/// field is <strong>not</strong> a security measure. If the corresponding role
/// policy (<see cref="TraconPolicies"/>) is not registered in the
/// consumer's authorization configuration, the corresponding field returns
/// <see langword="true"/>: there is no role restriction, the endpoint only
/// passes through the existing three-layer protection.
/// </remarks>
[Description("Reports which role levels the current request's caller satisfies.")]
internal sealed record TraconRoleMeta
{
    [Description("Whether the caller may read agents, runs, sessions, traces, and statistics.")]
    public required bool CanRead { get; init; }

    [Description("Whether the caller may, in addition to Reader, start runs, give approvals, and delete sessions.")]
    public required bool CanOperate { get; init; }

    [Description("Whether the caller may write agent definitions, add MCP servers, and manage tenants and audit trails.")]
    public required bool CanAdminister { get; init; }
}

/// <remarks>
/// Carries only <see langword="bool"/> fields. The policy name is
/// deliberately <strong>not</strong> returned: the UI cannot do anything with
/// the name, and the name would be a configuration detail leaking from an
/// unauthenticated endpoint.
/// </remarks>
[Description("Reports which authentication layers are enabled.")]
internal sealed record TraconAuthenticationMeta
{
    [Description("Whether access from outside loopback is allowed.")]
    public required bool AllowRemoteAccess { get; init; }

    [Description("Whether an `Authorization: Bearer` header is expected.")]
    public required bool RequiresBearerToken { get; init; }

    [Description("Whether an ASP.NET Core authorization policy is applied.")]
    public required bool RequiresAuthorizationPolicy { get; init; }
}

/// <remarks>
/// In-memory stores are a supported mode, not a test helper.
/// They do have limits, however — process lifetime and single node — and the
/// UI should be able to show this to the user.
/// </remarks>
[Description("Reports which storage implementations are active.")]
internal sealed record TraconStorageMeta
{
    [Description(
        "Whether all three stores are persistent. Returns\n" +
        "`false` if any is in-memory.")]
    public required bool Persistent { get; init; }

    [Description("Type name of the agent definition store.")]
    public required string AgentDefinitionStore { get; init; }

    [Description("Type name of the run store.")]
    public required string RunStore { get; init; }

    [Description("Type name of the session store.")]
    public required string SessionStore { get; init; }

    [Description("Type name of the job queue store.")]
    public required string JobStore { get; init; }

    [Description(
        "Whether the background job worker is running in this process. If\n" +
        "`false`, the queue can still be written to and read\n" +
        "from; only this process does not lease jobs.")]
    public required bool JobWorkerEnabled { get; init; }
}
