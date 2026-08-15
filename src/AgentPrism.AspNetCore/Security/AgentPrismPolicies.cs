namespace AgentPrism;

/// <summary>
/// Role-based authorization policy names that AgentPrism defines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism stores no user and no role.</strong> Roles come from the
/// consumer identity system; AgentPrism defines only the policy <em>name</em>, and
/// the consumer binds those names to its own claims:
/// </para>
/// <code>
/// builder.Services.AddAuthorization(options =>
/// {
///     options.AddPolicy(AgentPrismPolicies.Reader,   p => p.RequireRole("agentprism-reader",
///                                                                      "agentprism-operator",
///                                                                      "agentprism-admin"));
///     options.AddPolicy(AgentPrismPolicies.Operator, p => p.RequireRole("agentprism-operator",
///                                                                      "agentprism-admin"));
///     options.AddPolicy(AgentPrismPolicies.Admin,    p => p.RequireRole("agentprism-admin"));
/// });
/// </code>
/// <para>
/// <strong>When a policy is not registered, that endpoint falls back to its earlier
/// behavior</strong> (only the existing three-layer protection: loopback, bearer token,
/// general authorization policy). Otherwise this role model would break the setup of
/// everyone who updates, with a <c>403</c>. A production setup can turn a missing policy
/// into a startup failure with <see cref="AgentPrismEndpointOptions.RequireRolePolicies"/>.
/// </para>
/// </remarks>
public static class AgentPrismPolicies
{
    /// <summary>Read access: agents, runs, sessions, traces, statistics.</summary>
    public const string Reader = "AgentPrism.Reader";

    /// <summary>Reader plus starting a run, granting an approval, deleting a session.</summary>
    public const string Operator = "AgentPrism.Operator";

    /// <summary>Everything: writing an agent definition, adding an MCP server, deleting an approval rule, tenant management.</summary>
    public const string Admin = "AgentPrism.Admin";
}
