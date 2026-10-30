using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// The tenant's quota stands in front of a child agent run, as the tree's budget does: in a workflow
/// the refusal ends the step, in agent delegation it comes back to the model as a tool result.
/// </summary>
/// <remarks>
/// The wrapper is called with <c>options = null</c>, the way Microsoft Agent Framework calls it
/// (see <see cref="ChildAgentInvokerTests"/>).
/// </remarks>
public sealed class ChildAgentInvokerQuotaTests
{
    private const string Tenant = "test";
    private const string Root = "router";

    [Fact]
    public async Task A_delegated_call_is_refused_as_a_tool_result_and_opens_no_run()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.ToolResult, maxCost: 1m);

        await harness.SpentAsync(1m);

        var budget = harness.Scope();
        var response = await harness.Invoker.RunAsync("run");

        response.Text.ShouldContain("Could not call agent 'researcher'", Case.Sensitive);
        response.Text.ShouldContain("daily cost quota for the whole tenant has been exceeded (1/1)", Case.Sensitive);
        response.Text.ShouldContain("A new child run cannot be started.", Case.Sensitive);
        (await harness.Store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldBeEmpty();
        budget.StartedRuns.ShouldBe(0, "a refused step must not use up a run of the tree");
    }

    [Fact]
    public async Task A_workflow_step_is_refused_with_the_tenant_quota_error_and_its_numbers()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m);

        await harness.SpentAsync(1m);

        var budget = harness.Scope();

        var refusal = await Should.ThrowAsync<TraconTenantQuotaExceededException>(async () => await harness.Invoker.RunAsync("run"));

        refusal.ErrorType.ShouldBe(TraconTenantQuotaExceededException.TenantQuotaExceededErrorType);
        refusal.Metric.ShouldBe(QuotaMetric.Cost);
        refusal.Period.ShouldBe(QuotaPeriod.Daily);
        refusal.Limit.ShouldBe(1m);
        budget.StartedRuns.ShouldBe(0);
    }

    [Fact]
    public async Task The_streaming_form_is_refused_the_same_way()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m);

        await harness.SpentAsync(1m);
        harness.Scope();

        await Should.ThrowAsync<TraconTenantQuotaExceededException>(async () =>
        {
            await foreach (var _ in harness.Invoker.RunStreamingAsync("run"))
            {
                // Drained.
            }
        });
    }

    [Fact]
    public async Task The_trees_own_unwritten_spend_counts_for_a_step()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m);
        var budget = harness.Scope();

        // 0.99 is in the counter, and the tree has spent 0.02 more that its root has not written yet.
        await harness.SpentAsync(0.99m);
        budget.RecordUsage(0, 0.02m);

        await Should.ThrowAsync<TraconTenantQuotaExceededException>(async () => await harness.Invoker.RunAsync("run"));
    }

    [Fact]
    public async Task What_the_trees_budget_held_when_its_root_named_the_account_is_not_counted_again()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m);

        // The earlier runs of a continued workflow spent 0.99 and wrote it; the budget starts from it.
        await harness.SpentAsync(0.99m);

        var budget = new AgentRunBudget(maxDuration: null, TimeProvider.System, new BudgetCarryOver(0, 0.99m, 0, TimeSpan.Zero))
        {
            MaxDepth = 3,
        };

        harness.Scope(budget);

        var response = await harness.Invoker.RunAsync("run");

        response.Text.ShouldBe("research result");
        budget.StartedRuns.ShouldBe(1);
    }

    [Fact]
    public async Task A_spent_budget_answers_before_the_quota_is_read()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.ToolResult, maxCost: 1m, store: new CountingStore());

        await harness.SpentAsync(1m);

        var budget = new AgentRunBudget { MaxDepth = 3, MaxTotalTokens = 1 };

        budget.RecordUsage(10);
        harness.Scope(budget);
        harness.Counting!.Reset();

        var response = await harness.Invoker.RunAsync("run");

        response.Text.ShouldContain("token budget is exhausted", Case.Sensitive);
        harness.Counting.Lists.ShouldBe(0, "the budget is in memory and the quota is a store read");
    }

    [Fact]
    public async Task A_step_below_the_quota_reserves_its_run_and_runs()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m);

        await harness.SpentAsync(0.5m);

        var budget = harness.Scope();
        var response = await harness.Invoker.RunAsync("run");

        response.Text.ShouldBe("research result");
        budget.StartedRuns.ShouldBe(1);
        (await harness.Store.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_counter_read_is_the_trees_and_not_the_ambient_callers()
    {
        // A background step carries neither the root's tenant nor its user: the ambient tenant is "test", the tree's is
        // "root-tenant". The spent counter is the tree's.
        var spentTree = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m, ruleTenant: "root-tenant");

        await spentTree.SpentAsync(1m, tenant: "root-tenant");
        spentTree.Scope(accountTenant: "root-tenant");

        await Should.ThrowAsync<TraconTenantQuotaExceededException>(async () => await spentTree.Invoker.RunAsync("run"));

        // The reverse: the ambient tenant's counter is spent and the tree's is not. The step starts.
        var spentCaller = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m);

        await spentCaller.SpentAsync(1m);
        spentCaller.Scope(accountTenant: "root-tenant");

        (await spentCaller.Invoker.RunAsync("run")).Text.ShouldBe("research result");
    }

    [Fact]
    public async Task A_tree_with_no_account_is_not_asked_anything()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m, store: new CountingStore());

        await harness.SpentAsync(5m);
        harness.Counting!.Reset();

        // A budget no root of ours opened: the consumer built it for a child it runs itself.
        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
            Depth = 0,
            AgentName = Root,
            TenantId = Tenant,
            Budget = new AgentRunBudget { MaxDepth = 3 },
        });

        (await harness.Invoker.RunAsync("run")).Text.ShouldBe("research result");
        harness.Counting.Lists.ShouldBe(0);
    }

    [Fact]
    public async Task A_quota_that_is_off_is_not_asked_anything()
    {
        var harness = await Harness.CreateAsync(ChildRefusalMode.Fail, maxCost: 1m, quotaEnabled: false, store: new CountingStore());

        await harness.SpentAsync(5m);
        harness.Scope();
        harness.Counting!.Reset();

        (await harness.Invoker.RunAsync("run")).Text.ShouldBe("research result");
        harness.Counting.Lists.ShouldBe(0);
    }

    private sealed class Harness
    {
        private readonly QuotaEnforcer _enforcer;

        private Harness(QuotaEnforcer enforcer, InMemoryRunStore store, ChildAgentInvoker invoker, CountingStore? counting)
        {
            _enforcer = enforcer;
            Store = store;
            Invoker = invoker;
            Counting = counting;
        }

        public InMemoryRunStore Store { get; }

        public ChildAgentInvoker Invoker { get; }

        public CountingStore? Counting { get; }

        public static async Task<Harness> CreateAsync(
            ChildRefusalMode mode,
            decimal maxCost,
            bool quotaEnabled = true,
            CountingStore? store = null,
            string ruleTenant = Tenant)
        {
            var quotaStore = store ?? new CountingStore();
            var options = new TraconQuotaOptions { Enabled = quotaEnabled };
            var enforcer = new QuotaEnforcer(quotaStore, new StaticOptionsMonitor<TraconQuotaOptions>(options));

            await quotaStore.SaveAsync(new QuotaDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = ruleTenant,
                Period = QuotaPeriod.Daily,
                MaxCost = maxCost,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            var runStore = new InMemoryRunStore(tenantContext: new FixedTenantContext());
            var tenant = new FixedTenantContext();

            var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "research result")));
            var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider(client)), TestData.Registry());

            var inner = new RunRecordingAgent(
                compiler.Compile(TestData.Definition(name: "researcher")),
                runStore,
                tenant,
                new TraconRunRecordingOptions(),
                NullLogger<RunRecordingAgent>.Instance);

            var invoker = new ChildAgentInvoker(
                new CallableAgentResolver(new SingleAgentServices(inner)),
                tenant,
                NullLogger.Instance,
                Root,
                new CallableAgentInfo("researcher", "Performs research.", 1),
                childDeadline: TimeSpan.FromSeconds(30),
                waitTimeout: TimeSpan.FromSeconds(60),
                refusalMode: mode);

            return new Harness(enforcer, runStore, invoker, quotaStore);
        }

        /// <summary>Opens a root scope whose budget names the tenant's counter, as the recorder or the runner does.</summary>
        public AgentRunBudget Scope(AgentRunBudget? budget = null, string accountTenant = Tenant)
        {
            budget ??= new AgentRunBudget { MaxDepth = 3 };
            budget.Account = new TreeAccount(accountTenant, Root, UserId: null, _enforcer);

            var id = TraconId.NewId();

            TraconRunContext.SetCurrent(new AgentRunScope
            {
                RunId = id,
                RootRunId = id,
                Depth = 0,
                AgentName = Root,
                TenantId = Tenant,
                Budget = budget,
            });

            return budget;
        }

        public async Task SpentAsync(decimal cost, string tenant = Tenant)
            => await _enforcer.RecordAsync(new QuotaConsumption
            {
                TenantId = tenant,
                AgentName = Root,
                Runs = 1,
                Cost = cost,
                OccurredAt = DateTimeOffset.UtcNow,
            });
    }

    /// <summary>An in-memory quota store that counts the reads of the rules.</summary>
    private sealed class CountingStore : IQuotaStore
    {
        private readonly InMemoryQuotaStore _inner = new();

        public int Lists { get; private set; }

        public void Reset() => Lists = 0;

        public ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
        {
            Lists++;

            return _inner.ListAsync(tenantId, cancellationToken);
        }

        public ValueTask<QuotaDefinition?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => _inner.GetAsync(tenantId, id, cancellationToken);

        public ValueTask<QuotaDefinition> SaveAsync(QuotaDefinition definition, CancellationToken cancellationToken = default)
            => _inner.SaveAsync(definition, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
            => _inner.DeleteAsync(tenantId, id, cancellationToken);

        public ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(QuotaUsageQuery query, CancellationToken cancellationToken = default)
            => _inner.GetUsageAsync(query, cancellationToken);

        public ValueTask AddUsageAsync(
            QuotaConsumption consumption,
            IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
            CancellationToken cancellationToken = default)
            => _inner.AddUsageAsync(consumption, periodStarts, cancellationToken);

        public ValueTask<bool> TryClaimThresholdNotificationAsync(
            string tenantId,
            string agentName,
            QuotaPeriod period,
            DateOnly periodStart,
            QuotaMetric metric,
            int thresholdPercent,
            CancellationToken cancellationToken = default)
            => _inner.TryClaimThresholdNotificationAsync(tenantId, agentName, period, periodStart, metric, thresholdPercent, cancellationToken);
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => Tenant;
    }

    private sealed class SingleAgentServices(AIAgent agent) : IServiceProvider, IAgentCatalog
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IAgentCatalog) ? this : null;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                new AgentDescriptor
                {
                    Name = "researcher",
                    Origin = AgentDefinitionOrigin.Database,
                    SourceName = "database",
                },
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new(string.Equals(agentName, "researcher", StringComparison.Ordinal) ? agent : null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, culture, cancellationToken);
    }
}
