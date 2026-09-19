using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Approvals;

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
        //
        // Every server-side tool is wrapped in AuthorizingAIFunction (F-113)
        // and TimeoutAIFunction (F-114), whether it requires approval or not
        // (docs/69, section 69.1); approval alone no longer determines the
        // outermost type, and since HATA-S1-024 the outermost layer is
        // ExplainedFailureAIFunction. The descriptor's RequiresApproval flag is
        // the stable, wrapping-order-independent way to observe approval.
        var withApproval = new ToolRegistry(
            [
                new TraconToolRegistration(Function("safe_tool")),
                new TraconToolRegistration(Function("dangerous_tool"), requiresApproval: true),
            ],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        withApproval.TryGet("safe_tool", out var safe).ShouldBeTrue();
        safe.ShouldBeOfType<ExplainedFailureAIFunction>();

        withApproval.TryGet("dangerous_tool", out var dangerous).ShouldBeTrue();
        dangerous.ShouldBeOfType<ExplainedFailureAIFunction>();

        withApproval.List().Single(d => string.Equals(d.Name, "safe_tool", StringComparison.Ordinal))
            .RequiresApproval.ShouldBeFalse();
        withApproval.List().Single(d => string.Equals(d.Name, "dangerous_tool", StringComparison.Ordinal))
            .RequiresApproval.ShouldBeTrue();
    }

    [Fact]
    public void Wrapping_preserves_name_and_description()
    {
        // AuthorizingAIFunction/TimeoutAIFunction/ApprovalRequiredAIFunction
        // are all DelegatingAIFunction; wrapping must not change the contract
        // the model sees.
        var registry = new ToolRegistry(
            [
                new TraconToolRegistration(Function("dangerous_tool"), requiresApproval: true),
            ],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

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
                new TraconToolRegistration(Function("remote_tool"), requiresApproval: true, source: "github"),
            ],
            new AllowAllToolAuthorizationHandler(),
            NoOpToolArgumentsValidator.Instance,
            TestData.DefaultOptionsMonitor(),
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

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

        await store.AddAsync(Rule("cancel_order", agentName: "another-agent", argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("cancel_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Agent_unscoped_rule_covers_all_agents()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: null, argumentsHash: null));

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("any-agent", Call("cancel_order"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Another_tenants_rule_does_not_pass()
    {
        // Tenant boundary: an approval granted by one tenant must not run
        // another tenant's call.
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("cancel_order", agentName: null, argumentsHash: null) with
        {
            TenantId = "other-tenant",
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

        // Without field boundaries, "a=1" + "b=2" and "a=1b=" + "2" could
        // produce the same fingerprint.
        var second = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "1b=2" });

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Theory]
    // 🚨 The constructed collision that closed the separator assumption. The
    // old format joined fields with the unit separator (U+001F) and a comment
    // claimed the character "is not present in text values" -- nothing
    // enforced that, and a JSON argument can carry any character. Each pair
    // below hashed IDENTICALLY under the old format, which means a grant for
    // one call could be inherited by a call of a different SHAPE. The path is
    // the script dispatcher: the surface that runs code.
    [InlineData("b", "y", "\u001Fb=y")]
    [InlineData("b", "", "\u001Fb=")]
    [InlineData("bb", "y", "\u001Fbb=y")]
    public void A_value_cannot_be_read_as_a_field_boundary(string secondKey, string secondValue, string smuggled)
    {
        var twoFields = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "x", [secondKey] = secondValue });

        var oneFieldCarryingTheSeparator = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "x" + smuggled });

        string.Equals(twoFields, oneFieldCarryingTheSeparator, StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public void The_fingerprint_counts_bytes_and_not_characters()
    {
        // A character count would let two values of equal character length but
        // different byte length agree on the prefix. The buffer is hashed as
        // UTF-8, so the prefix counts UTF-8 bytes.
        var ascii = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "ab" });

        var multiByte = ToolApprovalRuleEvaluator.ComputeArgumentsHash(
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["a"] = "\u00e9b" });

        string.Equals(ascii, multiByte, StringComparison.Ordinal).ShouldBeFalse();
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

        (await store.DeleteAsync("other-tenant", rule.Id)).ShouldBeFalse();
        (await store.DeleteAsync("default", rule.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task Argument_condition_rule_matches_within_threshold()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("refund_order", "support", argumentsHash: null) with
        {
            ArgumentConditions = [ThresholdCondition(100)],
        });

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order", ("amount", 50)))).ShouldBeTrue();
        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order", ("amount", 500)))).ShouldBeFalse();
    }

    [Fact]
    public async Task Argument_condition_rule_asks_when_argument_missing()
    {
        var store = new InMemoryToolApprovalRuleStore();

        await store.AddAsync(Rule("refund_order", "support", argumentsHash: null) with
        {
            ArgumentConditions = [ThresholdCondition(100)],
        });

        var evaluator = CreateEvaluator(store);

        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Code_policy_required_overrides_a_matching_data_rule()
    {
        var store = new InMemoryToolApprovalRuleStore();
        await store.AddAsync(Rule("refund_order", "support", argumentsHash: null));

        var evaluator = CreateEvaluator(
            store,
            [new ToolApprovalPolicyRegistration("refund_order", static _ => ToolApprovalPolicyDecision.Required)]);

        // Data alone would auto-approve (unconditional rule); code overrides it.
        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Code_policy_not_required_overrides_absence_of_a_data_rule()
    {
        var evaluator = CreateEvaluator(
            new InMemoryToolApprovalRuleStore(),
            [new ToolApprovalPolicyRegistration("refund_order", static _ => ToolApprovalPolicyDecision.NotRequired)]);

        // No data rule exists at all; code alone auto-approves.
        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order"))).ShouldBeTrue();
    }

    [Fact]
    public async Task Undecided_code_policy_falls_through_to_data_rules()
    {
        var store = new InMemoryToolApprovalRuleStore();
        await store.AddAsync(Rule("refund_order", "support", argumentsHash: null));

        var evaluator = CreateEvaluator(
            store,
            [new ToolApprovalPolicyRegistration("refund_order", static _ => ToolApprovalPolicyDecision.Undecided)]);

        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order"))).ShouldBeTrue();
    }

    [Fact]
    public async Task A_throwing_code_policy_requires_approval()
    {
        var store = new InMemoryToolApprovalRuleStore();
        // Even an unconditional data rule that would otherwise auto-approve
        // must not rescue a broken policy.
        await store.AddAsync(Rule("refund_order", "support", argumentsHash: null));

        var evaluator = CreateEvaluator(
            store,
            [new ToolApprovalPolicyRegistration("refund_order", static _ => throw new InvalidOperationException("boom"))]);

        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Code_policy_reads_the_call_arguments()
    {
        var evaluator = CreateEvaluator(
            new InMemoryToolApprovalRuleStore(),
            [
                new ToolApprovalPolicyRegistration(
                    "refund_order",
                    static context => context.GetNumber("amount") is { } amount && amount <= 100
                        ? ToolApprovalPolicyDecision.NotRequired
                        : ToolApprovalPolicyDecision.Required),
            ]);

        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order", ("amount", 50)))).ShouldBeTrue();
        (await evaluator.IsAutoApprovedAsync("support", Call("refund_order", ("amount", 500)))).ShouldBeFalse();
    }

    private static ToolArgumentCondition ThresholdCondition(double max)
        => new()
        {
            Path = "amount",
            Operator = ToolArgumentOperator.LessThanOrEqual,
            Value = System.Text.Json.JsonSerializer.SerializeToElement(max),
        };

    private static ToolApprovalRuleEvaluator CreateEvaluator(
        IToolApprovalRuleStore store,
        IEnumerable<ToolApprovalPolicyRegistration>? policies = null)
        => new(
            store,
            new ToolApprovalPolicyRegistry(policies ?? []),
            new FixedTenantContext(),
            NullLogger<ToolApprovalRuleEvaluator>.Instance);

    private static ToolApprovalRule Rule(string toolName, string? agentName, string? argumentsHash)
        => new()
        {
            Id = TraconId.NewId(),
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
