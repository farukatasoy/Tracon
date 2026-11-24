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

    /// <summary>The code-defined approval policies of a host that registered none.</summary>
    public static ToolApprovalPolicyRegistry NoApprovalPolicies()
        => new([]);

    /// <summary>The code-defined approval policies of a host that registered these.</summary>
    /// <param name="policies">The policies to register.</param>
    public static ToolApprovalPolicyRegistry ApprovalPolicies(params (string ToolName, Func<ToolApprovalContext, ToolApprovalPolicyDecision> Policy)[] policies)
        => new(policies.Select(static policy => new ToolApprovalPolicyRegistration(policy.ToolName, policy.Policy)));
}
