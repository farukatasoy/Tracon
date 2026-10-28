using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// The rule of the tenant tool policy, row by row: a call passes when the tool's name is in the allowed list or the
/// tool's required permission is in the granted list, and a tenant with no policy is unrestricted unless the host
/// requires one.
/// </summary>
public sealed class TenantToolPolicyGateTests
{
    private static TenantToolPolicy Policy(string[] allowedTools, string[] grantedPermissions)
        => new()
        {
            TenantId = "acme",
            AllowedTools = allowedTools,
            GrantedPermissions = grantedPermissions,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

    [Fact]
    public void A_tenant_with_no_policy_is_unrestricted_while_the_switch_is_off()
        => TenantToolPolicyGate.Decide(policy: null, "get_order", requiredPermission: null, requirePolicy: false)
            .IsAllowed.ShouldBeTrue();

    [Fact]
    public void A_tenant_with_no_policy_runs_no_tool_while_the_switch_is_on_and_the_text_names_the_switch()
    {
        var decision = TenantToolPolicyGate.Decide(policy: null, "get_order", requiredPermission: "orders.read", requirePolicy: true);

        decision.IsAllowed.ShouldBeFalse();
        decision.Reason.ShouldNotBeNull().ShouldContain("Tracon:Tools:RequireTenantPolicy", Case.Sensitive);
    }

    [Theory]
    // The name is listed.
    [InlineData("get_order", null, new[] { "get_order" }, new string[0], true)]
    // The permission is granted.
    [InlineData("refund_order", "orders.refund", new[] { "get_order" }, new[] { "orders.refund" }, true)]
    // Either is enough: the name is listed and the permission is not granted.
    [InlineData("refund_order", "orders.refund", new[] { "refund_order" }, new string[0], true)]
    // A tool with no permission cannot pass on a granted permission.
    [InlineData("get_order", null, new[] { "get_weather" }, new[] { "orders.refund" }, false)]
    // The name is not listed and the permission is not granted.
    [InlineData("refund_order", "orders.refund", new[] { "get_order" }, new[] { "orders.read" }, false)]
    // Two empty lists are a policy that allows nothing, not the absence of a policy.
    [InlineData("get_order", null, new string[0], new string[0], false)]
    [InlineData("refund_order", "orders.refund", new string[0], new string[0], false)]
    // Names and permissions are compared exactly, with letter case.
    [InlineData("Get_Order", null, new[] { "get_order" }, new string[0], false)]
    [InlineData("refund_order", "Orders.Refund", new string[0], new[] { "orders.refund" }, false)]
    // An empty permission never matches an empty entry.
    [InlineData("get_order", "", new string[0], new[] { "" }, false)]
    public void A_call_passes_on_the_name_or_the_permission_and_on_nothing_else(
        string tool,
        string? permission,
        string[] allowedTools,
        string[] grantedPermissions,
        bool expected)
    {
        var decision = TenantToolPolicyGate.Decide(Policy(allowedTools, grantedPermissions), tool, permission, requirePolicy: false);

        decision.IsAllowed.ShouldBe(expected);

        if (!expected)
        {
            // The text goes to the model: no tenant, no tool list, no permission.
            decision.Reason.ShouldBe("This tool is not enabled for this tenant.");
        }
    }

    [Fact]
    public void The_switch_changes_nothing_for_a_tenant_that_has_a_policy()
    {
        var policy = Policy(["get_order"], []);

        TenantToolPolicyGate.Decide(policy, "get_order", null, requirePolicy: true).IsAllowed.ShouldBeTrue();
        TenantToolPolicyGate.Decide(policy, "get_weather", null, requirePolicy: true).IsAllowed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("ACME")]
    [InlineData("Acme")]
    public async Task The_policy_of_a_tenant_is_found_whatever_letter_case_the_run_carries(string tenantOfTheRun)
    {
        var store = new InMemoryTenantToolPolicyStore();
        await store.UpsertAsync("acme", ["get_order"], [], TestContext.Current.CancellationToken);

        var gate = TestData.ToolPolicyGate(store);

        // A missed row is a tenant with no policy, and with the switch off that is a tenant that runs every tool.
        (await gate.CheckAsync(tenantOfTheRun, "get_order", null, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        (await gate.CheckAsync(tenantOfTheRun, "get_weather", null, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_gate_reads_the_store_on_every_call_and_keeps_no_copy()
    {
        var store = new InMemoryTenantToolPolicyStore();
        var gate = TestData.ToolPolicyGate(store);

        (await gate.CheckAsync("acme", "get_order", null, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();

        await store.UpsertAsync("acme", [], [], TestContext.Current.CancellationToken);
        (await gate.CheckAsync("acme", "get_order", null, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeFalse();

        await store.DeleteAsync("acme", TestContext.Current.CancellationToken);
        (await gate.CheckAsync("acme", "get_order", null, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task The_switch_is_read_from_the_options_the_gate_was_built_with()
    {
        var store = new InMemoryTenantToolPolicyStore();

        (await TestData.ToolPolicyGate(store, requirePolicy: false)
            .CheckAsync("acme", "get_order", null, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeTrue();
        (await TestData.ToolPolicyGate(store, requirePolicy: true)
            .CheckAsync("acme", "get_order", null, TestContext.Current.CancellationToken)).IsAllowed.ShouldBeFalse();
    }
}
