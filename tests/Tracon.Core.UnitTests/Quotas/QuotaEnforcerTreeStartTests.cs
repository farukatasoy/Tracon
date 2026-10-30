using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// The check a run tree that is already running makes before it starts a function node or an agent step
///The same rules and reads as the door's check, the tree's own unwritten spend added.
/// </summary>
public sealed class QuotaEnforcerTreeStartTests
{
    private const string Tenant = "acme";
    private const string Root = "podcast";

    [Fact]
    public async Task A_function_node_is_stopped_by_the_cost_limit_and_not_by_tokens()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(maxTokens: 1_000, maxCost: 10m);
        await harness.RecordAsync(tokens: 5_000, cost: 1m);

        var node = await harness.CheckAsync(TreeStartKind.FunctionNode);
        var step = await harness.CheckAsync(TreeStartKind.AgentStep);

        node.IsAllowed.ShouldBeTrue("a function node spends no tokens");
        step.IsAllowed.ShouldBeFalse();
        step.Metric.ShouldBe(QuotaMetric.Tokens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_kinds_are_stopped_by_a_spent_cost_limit_and_the_decision_names_it(bool agentStep)
    {
        var kind = agentStep ? TreeStartKind.AgentStep : TreeStartKind.FunctionNode;
        var harness = Harness.Create();

        await harness.SaveAsync(maxCost: 10m);
        await harness.RecordAsync(cost: 10m);

        var decision = await harness.CheckAsync(kind);

        decision.IsAllowed.ShouldBeFalse();
        decision.Metric.ShouldBe(QuotaMetric.Cost);
        decision.Period.ShouldBe(QuotaPeriod.Daily);
        decision.Limit.ShouldBe(10m);
        decision.Used.ShouldBe(10m);
        decision.ResetsAt.ShouldNotBeNull();
        decision.Reason.ShouldNotBeNull().ShouldContain("daily cost quota", Case.Sensitive);
    }

    [Fact]
    public async Task The_trees_unwritten_spend_is_added_to_the_counter()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(maxCost: 1m);
        await harness.RecordAsync(cost: 0.5m);

        (await harness.CheckAsync(TreeStartKind.FunctionNode, unrecordedCost: 0.4m)).IsAllowed.ShouldBeTrue();

        var decision = await harness.CheckAsync(TreeStartKind.FunctionNode, unrecordedCost: 0.5m);

        decision.IsAllowed.ShouldBeFalse();
        decision.Used.ShouldBe(1m, "the counter and the tree's own spend");
    }

    [Fact]
    public async Task The_trees_unwritten_tokens_are_added_for_an_agent_step()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(maxTokens: 100);
        await harness.RecordAsync(tokens: 60);

        (await harness.CheckAsync(TreeStartKind.AgentStep, unrecordedTokens: 39)).IsAllowed.ShouldBeTrue();
        (await harness.CheckAsync(TreeStartKind.AgentStep, unrecordedTokens: 40)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_counter_nothing_has_written_to_counts_as_zero_for_a_tree_that_has_spent()
    {
        // The workflow root writes at its end. A first node that already passed the limit is stopped
        // before a single entry exists.
        var harness = Harness.Create();

        await harness.SaveAsync(maxCost: 1m);

        (await harness.CheckAsync(TreeStartKind.FunctionNode)).IsAllowed.ShouldBeTrue();
        (await harness.CheckAsync(TreeStartKind.FunctionNode, unrecordedCost: 1m)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_run_limit_never_stops_a_tree_that_is_already_running()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(maxRuns: 1);
        await harness.RecordAsync(runs: 1);

        (await harness.Enforcer.CheckAsync(Tenant, Root)).IsAllowed.ShouldBeFalse("a new run is held to the run limit");
        (await harness.CheckAsync(TreeStartKind.FunctionNode)).IsAllowed.ShouldBeTrue();
        (await harness.CheckAsync(TreeStartKind.AgentStep)).IsAllowed.ShouldBeTrue("a child run adds no run");
    }

    [Fact]
    public async Task A_new_run_check_adds_nothing_and_reads_as_before()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(maxCost: 1m);
        await harness.RecordAsync(cost: 0.99m);

        (await harness.Enforcer.CheckAsync(Tenant, Root)).IsAllowed.ShouldBeTrue();

        await harness.RecordAsync(runs: 0, cost: 0.01m);

        (await harness.Enforcer.CheckAsync(Tenant, Root)).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task The_identity_is_the_trees_and_another_tenants_counter_is_not_read()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(maxCost: 1m);
        await harness.SaveAsync(tenantId: "globex", maxCost: 1m);
        await harness.RecordAsync(tenantId: "globex", cost: 5m);

        (await harness.CheckAsync(TreeStartKind.FunctionNode)).IsAllowed.ShouldBeTrue("acme's counter is empty");
        (await harness.CheckAsync(TreeStartKind.FunctionNode, tenantId: "globex")).IsAllowed.ShouldBeFalse();
    }

    [Fact]
    public async Task A_rule_of_an_agent_reads_the_counter_of_the_trees_root()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(agentName: Root, maxCost: 1m);
        await harness.RecordAsync(agentName: Root, cost: 1m);

        (await harness.CheckAsync(TreeStartKind.FunctionNode)).IsAllowed.ShouldBeFalse();
        (await harness.CheckAsync(TreeStartKind.FunctionNode, rootName: "another")).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task A_rule_that_is_off_or_a_quota_that_is_off_stops_nothing()
    {
        var disabledRule = Harness.Create();

        await disabledRule.SaveAsync(maxCost: 1m, enabled: false);
        await disabledRule.RecordAsync(cost: 5m);

        (await disabledRule.CheckAsync(TreeStartKind.FunctionNode)).IsAllowed.ShouldBeTrue();

        var disabledQuota = Harness.Create(options => options.Enabled = false);

        await disabledQuota.SaveAsync(maxCost: 1m);

        (await disabledQuota.CheckAsync(TreeStartKind.FunctionNode, unrecordedCost: 5m)).IsAllowed.ShouldBeTrue();
        disabledQuota.Enforcer.IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task A_tenant_with_no_rule_costs_one_read_and_one_with_a_rule_costs_two()
    {
        // The measured cost of the check at the start of a node (the change's cost note): the rules,
        // then the current periods' usage.
        var counting = new CountingQuotaStore(new InMemoryQuotaStore());
        var harness = Harness.Create(store: counting);

        (await harness.CheckAsync(TreeStartKind.AgentStep)).IsAllowed.ShouldBeTrue();

        counting.Lists.ShouldBe(1);
        counting.Usages.ShouldBe(0);

        await harness.SaveAsync(maxCost: 100m);
        counting.Reset();

        (await harness.CheckAsync(TreeStartKind.AgentStep)).IsAllowed.ShouldBeTrue();

        counting.Lists.ShouldBe(1);
        counting.Usages.ShouldBe(1);
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_follows_AllowOnStoreFailure()
    {
        var allowing = Harness.Create(store: new FailingQuotaStore());
        var strict = Harness.Create(options => options.AllowOnStoreFailure = false, store: new FailingQuotaStore());

        (await allowing.CheckAsync(TreeStartKind.FunctionNode)).IsAllowed.ShouldBeTrue();

        var refused = await strict.CheckAsync(TreeStartKind.FunctionNode);

        refused.IsAllowed.ShouldBeFalse();
        refused.Metric.ShouldBeNull();
        refused.Reason.ShouldBe("Quota could not be verified.");
    }

    [Fact]
    public async Task A_cancelled_check_throws_the_cancellation()
    {
        var harness = Harness.Create(store: new FailingQuotaStore(throwsCancellation: true));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await harness.CheckAsync(TreeStartKind.FunctionNode, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task A_negative_unwritten_spend_is_read_as_none()
    {
        var harness = Harness.Create();

        await harness.SaveAsync(maxCost: 1m);
        await harness.RecordAsync(cost: 0.9m);

        (await harness.CheckAsync(TreeStartKind.FunctionNode, unrecordedCost: -5m)).IsAllowed.ShouldBeTrue();
    }

    private sealed class Harness
    {
        private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

        private Harness(IQuotaStore store, TraconQuotaOptions options)
        {
            Store = store;
            Enforcer = new QuotaEnforcer(store, new StaticOptionsMonitor<TraconQuotaOptions>(options), webhookPublisher: null, _clock);
        }

        public IQuotaStore Store { get; }

        public QuotaEnforcer Enforcer { get; }

        public static Harness Create(Action<TraconQuotaOptions>? configure = null, IQuotaStore? store = null)
        {
            var options = new TraconQuotaOptions();
            configure?.Invoke(options);

            return new Harness(store ?? new InMemoryQuotaStore(), options);
        }

        public ValueTask<QuotaDecision> CheckAsync(
            TreeStartKind kind,
            long unrecordedTokens = 0,
            decimal unrecordedCost = 0m,
            string tenantId = Tenant,
            string rootName = Root,
            CancellationToken cancellationToken = default)
            => Enforcer.CheckTreeStartAsync(
                new TreeAccount(tenantId, rootName, UserId: null, Enforcer),
                kind,
                unrecordedTokens,
                unrecordedCost,
                cancellationToken);

        public async Task SaveAsync(
            string tenantId = Tenant,
            string? agentName = null,
            long? maxRuns = null,
            long? maxTokens = null,
            decimal? maxCost = null,
            bool enabled = true)
            => await Store.SaveAsync(new QuotaDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AgentName = agentName,
                Period = QuotaPeriod.Daily,
                MaxRuns = maxRuns,
                MaxTokens = maxTokens,
                MaxCost = maxCost,
                Enabled = enabled,
                CreatedAt = _clock.GetUtcNow(),
                UpdatedAt = _clock.GetUtcNow(),
            });

        public async Task RecordAsync(
            string tenantId = Tenant,
            string agentName = Root,
            long runs = 1,
            long tokens = 0,
            decimal? cost = null)
            => await Enforcer.RecordAsync(new QuotaConsumption
            {
                TenantId = tenantId,
                AgentName = agentName,
                Runs = runs,
                Tokens = tokens,
                Cost = cost,
                OccurredAt = _clock.GetUtcNow(),
            });
    }

    private sealed class CountingQuotaStore(IQuotaStore inner) : IQuotaStore
    {
        public int Lists { get; private set; }

        public int Usages { get; private set; }

        public void Reset()
        {
            Lists = 0;
            Usages = 0;
        }

        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Lists++;

            return inner.ListAsync(tenantId, cancellationToken);
        }

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, id, cancellationToken);

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => inner.SaveAsync(definition, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, id, cancellationToken);

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
        {
            Usages++;

            return inner.GetUsageAsync(query, cancellationToken);
        }

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => inner.AddUsageAsync(consumption, periodStarts, cancellationToken);

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => inner.TryClaimThresholdNotificationAsync(tenantId, agentName, period, periodStart, metric, thresholdPercent, cancellationToken);
    }

    private sealed class FailingQuotaStore(bool throwsCancellation = false) : IQuotaStore
    {
        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throwsCancellation
                ? throw new OperationCanceledException(cancellationToken)
                : throw new InvalidOperationException("the quota store is down");

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
