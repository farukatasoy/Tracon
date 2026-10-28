using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>The tenant tool policy gates the MCP tests build their tool sets with.</summary>
internal static class McpToolPolicyTestGates
{
    /// <summary>
    /// A gate over an empty store with the switch off: every call passes the policy and the handler alone decides,
    /// as for a host that saved no policy.
    /// </summary>
    public static TenantToolPolicyGate Open()
        => For(new InMemoryTenantToolPolicyStore());

    /// <summary>A gate over a store.</summary>
    /// <param name="store">The store the gate reads.</param>
    /// <param name="requirePolicy">The value of <c>Tracon:Tools:RequireTenantPolicy</c>.</param>
    public static TenantToolPolicyGate For(ITenantToolPolicyStore store, bool requirePolicy = false)
        => new(store, Options.Create(new TraconOptions { Tools = new TraconToolOptions { RequireTenantPolicy = requirePolicy } }));
}
