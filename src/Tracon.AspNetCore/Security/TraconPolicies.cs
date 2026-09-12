namespace Tracon;

/// <summary>
/// Role-based authorization policy names that Tracon defines.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Tracon stores no user and no role.</strong> Roles come from the
/// consumer identity system; Tracon defines only the policy <em>name</em>, and
/// the consumer binds those names to its own claims:
/// </para>
/// <code>
/// builder.Services.AddAuthorization(options =>
/// {
///     options.AddPolicy(TraconPolicies.Reader,   p => p.RequireRole("tracon-reader",
///                                                                      "tracon-operator",
///                                                                      "tracon-admin"));
///     options.AddPolicy(TraconPolicies.Operator, p => p.RequireRole("tracon-operator",
///                                                                      "tracon-admin"));
///     options.AddPolicy(TraconPolicies.Admin,    p => p.RequireRole("tracon-admin"));
/// });
/// </code>
/// <para>
/// <strong>When a policy is not registered, that endpoint falls back to its earlier
/// behavior</strong> (only the existing three-layer protection: loopback, bearer token,
/// general authorization policy). Otherwise this role model would break the setup of
/// everyone who updates, with a <c>403</c>. A production setup can turn a missing policy
/// into a startup failure with <see cref="TraconEndpointOptions.RequireRolePolicies"/>.
/// </para>
/// </remarks>
public static class TraconPolicies
{
    /// <summary>Read access: agents, runs, sessions, traces, statistics.</summary>
    public const string Reader = "Tracon.Reader";

    /// <summary>Reader plus starting a run, granting an approval, deleting a session.</summary>
    public const string Operator = "Tracon.Operator";

    /// <summary>
    /// Everything: writing an agent definition, adding an MCP server, deleting an
    /// approval rule, tenant management.
    /// </summary>
    public const string Admin = "Tracon.Admin";
}
