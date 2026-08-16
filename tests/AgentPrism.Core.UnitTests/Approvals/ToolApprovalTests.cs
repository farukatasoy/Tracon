using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Approvals;

/// <summary>
/// Tool approval: registry wrapping and enforcement of persisted rules.
/// </summary>
public sealed class ToolApprovalTests
{
    [Fact]
    public void Approval_required_tool_is_wrapped_by_the_registry()
    {
        // Wrapping MUST happen HERE: the registry is the only place where the
        // rule "an agent can only reference a registered tool" is enforced. No
        // other code path should be able to skip the wrapping.
        var registry = new ToolRegistry(
        [
            new AgentPrismToolRegistration(Function("safe_tool")),
            new AgentPrismToolRegistration(Function("dangerous_tool"), requiresApproval: true),
        ]);

        registry.TryGet("safe_tool", out var safe).ShouldBeTrue();
        safe.ShouldNotBeOfType<ApprovalRequiredAIFunction>();

        registry.TryGet("dangerous_tool", out var dangerous).ShouldBeTrue();
        dangerous.ShouldBeOfType<ApprovalRequiredAIFunction>();
    }

    [Fact]
    public void Wrapping_preserves_name_and_description()
    {
        // ApprovalRequiredAIFunction is a DelegatingAIFunction; wrapping it
        // must not change the contract the model sees.
        var registry = new ToolRegistry(
        [
            new AgentPrismToolRegistration(Function("dangerous_tool"), requiresApproval: true),
        ]);

        registry.TryGet("dangerous_tool", out var tool).ShouldBeTrue();

        tool.Name.ShouldBe("dangerous_tool");

        var descriptor = registry.List().ShouldHaveSingleItem();

        descriptor.RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void Source_information_is_carried_to_the_descriptor()
    {
        var registry = new ToolRegistry(
        [
            new AgentPrismToolRegistration(Function("remote_tool"), requiresApproval: true, source: "github"),
        ]);

        var descriptor = registry.List().ShouldHaveSingleItem();

        string.Equals(descriptor.Source, "github", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task No_rule_means_no_automatic_approval()
    {
        var evaluator = CreateEvaluator(new InMemoryToolApprovalRuleStore());

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Argument_unscoped_rule_approves_every_call()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: "support", argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order", ("orderId", "A"))))
            .ShouldBeTrue();
        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order", ("orderId", "B"))))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Argument_scoped_rule_matches_only_the_same_arguments()
    {
        var store = new InMemoryToolApprovalRuleStore();
        var call = Call("cancel_order", ("orderId", "ORD-1"));

        await store.AddAsync(Rule(
            "cancel_order",
            agentName: "support",
            argumentsHash: ToolApprovalRuleEvaluator.ComputeArgumentsHash(call.Arguments)));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", call)).ShouldBeTrue();
        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order", ("orderId", "ORD-2"))))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Another_agents_rule_does_not_pass()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: "baska-agent", argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Agent_unscoped_rule_covers_all_agents()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: null, argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("herhangi-bir-agent", Call("cancel_order"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Another_tenants_rule_does_not_pass()
    {
        // Tenant boundary: an approval granted by one tenant must not run
        // another tenant's call.
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: null, argumentsHash: null) with
        {
            TenantId = "baska-kiraci",
        });

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Store_failure_does_not_grant_approval()
    {
        // Fail safe: if the rule cannot be read, the call is asked of the user.
        var evaluator = CreateEvaluator(new ThrowingRuleStore());

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public void Argument_fingerprint_is_independent_of_key_order()
    {
        // Dictionary order can change between runs; if it did, the
        // "don't ask again" rule would never match.
        var first = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["b"] = 2, ["a"] = 1 });

        var second = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 });

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void Different_arguments_produce_different_fingerprints()
    {
        var first = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "1", ["b"] = "2" });

        // Without a separator, "a=1" + "b=2" and "a=1b=" + "2" could produce
        // the same fingerprint.
        var second = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "1b=2" });

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task Same_scope_is_not_added_twice()
    {
        var store = new InMemoryToolApprovalRuleStore();

        var first = await store.AddAsync(Rule("cancel_order", "support", null));
        var second = await store.AddAsync(Rule("cancel_order", "support", null));

        second.Id.ShouldBe(first.Id);
        (await store.ListAsync("default")).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Another_tenants_rule_cannot_be_deleted()
    {
        var store = new InMemoryToolApprovalRuleStore();
        var rule = await store.AddAsync(Rule("cancel_order", "support", null));

        (await store.DeleteAsync("baska-kiraci", rule.Id)).ShouldBeFalse();
        (await store.DeleteAsync("default", rule.Id)).ShouldBeTrue();
    }

    private static ToolApprovalRuleEvaluator CreateEvaluator(IToolApprovalRuleStore store)
        => new(store, new FixedTenantContext(), NullLogger<ToolApprovalRuleEvaluator>.Instance);

    private static ToolApprovalRule Rule(string toolName, string? agentName, string? argumentsHash)
        => new()
        {
            Id = AgentPrismId.NewId(),
            TenantId = "default",
            AgentName = agentName,
            ToolName = toolName,
            ArgumentsHash = argumentsHash,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static FunctionCallContent Call(string name, params (string Key, object? Value)[] arguments)
        => new(
            Guid.NewGuid().ToString("N"),
            name,
            arguments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    private static AIFunction Function(string name)
        => AIFunctionFactory.Create(() => "ok", name, $"{name} description");

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "default";
    }

    private sealed class ThrowingRuleStore : IToolApprovalRuleStore
    {
        public ValueTask<IReadOnlyList<ToolApprovalRule>> ListAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is unreachable");

        public ValueTask<ToolApprovalRule> AddAsync(
            ToolApprovalRule rule,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is unreachable");

        public ValueTask<bool> DeleteAsync(
            string tenantId,
            Guid ruleId,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is unreachable");
    }
}
