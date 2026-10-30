using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// Every run in a call tree writes its own consumption into the counter its root
/// names, when it ends; the identity is the tree's, never the caller's.
/// </summary>
/// <remarks>
/// <para>
/// The root used to write the whole tree once, from the run store, while its
/// own row closed. A child that ended later was never counted. These cases drive
/// <see cref="RunRecordingAgent"/> with the same hand-built options a consumer-built
/// child uses (<see cref="TraconRunOptions.Depth"/> and <see cref="TraconRunOptions.Budget"/>),
/// which isolates the rule from the nested-call machinery
/// <c>BackgroundChildQuotaTests</c> already runs end to end.
/// </para>
/// </remarks>
public sealed class TreeQuotaTests
{
    private const string Tenant = "test";
    private const string RootTenant = "root-tenant";
    private const string RootName = "root-agent";

    [Fact]
    public async Task A_root_run_names_the_counter_its_tree_adds_to()
    {
        using var harness = new Harness();
        var budget = new AgentRunBudget();

        await harness.Agent("router").RunAsync(Message(), session: null, new TraconRunOptions { Budget = budget });

        var account = budget.Account.ShouldNotBeNull();

        account.TenantId.ShouldBe(Tenant);
        account.RootName.ShouldBe("router");
        account.UserId.ShouldBeNull();
        account.Quota.ShouldBeSameAs(harness.Enforcer);
    }

    [Fact]
    public async Task A_budget_a_second_root_reuses_names_the_second_root()
    {
        // A consumer that reuses one budget for two roots must not leave the second
        // root's children writing under the first root's name.
        using var harness = new Harness();
        var budget = new AgentRunBudget();

        await harness.Agent("first").RunAsync(Message(), session: null, new TraconRunOptions { Budget = budget });
        await harness.Agent("second").RunAsync(Message(), session: null, new TraconRunOptions { Budget = budget });

        budget.Account.ShouldNotBeNull().RootName.ShouldBe("second");
    }

    [Fact]
    public async Task A_child_run_adds_its_tokens_and_no_run_to_the_roots_counter()
    {
        using var harness = new Harness();
        var budget = ChildBudget(harness);

        await harness.Agent("helper", Usage(4, 6)).RunAsync(Message(), session: null, ChildOptions(budget));

        // The root's tenant and name, not the child's own (tenant "test", name "helper").
        var counter = (await harness.CountersAsync(RootTenant, RootName)).Single(static record => record.Period == QuotaPeriod.Daily);

        counter.Runs.ShouldBe(0);
        counter.Tokens.ShouldBe(10);

        (await harness.CountersAsync(Tenant, "helper")).ShouldBeEmpty();
        (await harness.CountersAsync(Tenant, string.Empty)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_child_run_whose_tree_has_no_account_records_nothing()
    {
        // Depth is read directly from TraconRunOptions.Depth - the field ChildAgentInvoker
        // sets for a real sub-agent call. With no root of ours behind the budget there is no
        // counter to name, and a counter named from the child's own scope would be the
        // wrong tenant whenever the child ends in a task with no request.
        using var harness = new Harness();

        await harness.Agent("helper", Usage(4, 6)).RunAsync(
            Message(),
            session: null,
            new TraconRunOptions
            {
                RunId = TraconId.NewId(),
                Depth = 1,
                ParentRunId = TraconId.NewId(),
                RootRunId = TraconId.NewId(),
                Budget = new AgentRunBudget(),
            });

        (await harness.QuotaStore.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant })).ShouldBeEmpty();
        (await harness.QuotaStore.GetUsageAsync(new QuotaUsageQuery { TenantId = RootTenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_child_that_spent_nothing_writes_no_entry()
    {
        // A cancelled or failed child with no usage on it adds nothing: a store round trip, and
        // two more for the threshold check, to leave the counter where it was.
        using var harness = new Harness();
        var budget = ChildBudget(harness);

        await harness.Agent("helper").RunAsync(Message(), session: null, ChildOptions(budget));

        (await harness.CountersAsync(RootTenant, RootName)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("USD", "USD", 0.03)]
    [InlineData("usd", "USD", 0.03)]
    [InlineData(null, "USD", 0.03)]
    [InlineData("", "USD", 0.03)]
    [InlineData("EUR", "USD", 0)]
    [InlineData("EUR", null, 0)]
    public async Task A_child_adds_money_only_in_the_currency_tracon_prices_in(string? childCurrency, string? pricingCurrency, double expected)
    {
        // The counter holds a number with no currency, and the root's own
        // currency is not known while a child ends before it. The tokens always count.
        using var harness = new Harness(pricingCurrency, priceCurrency: childCurrency);
        var budget = ChildBudget(harness);

        await harness.Agent("helper", Usage(4, 6)).RunAsync(Message(), session: null, ChildOptions(budget));

        var counter = (await harness.CountersAsync(RootTenant, RootName)).Single(static record => record.Period == QuotaPeriod.Daily);

        counter.Tokens.ShouldBe(10);
        counter.Cost.ShouldBe((decimal)expected);
    }

    [Fact]
    public async Task A_root_adds_its_own_cost_whatever_currency_it_is_priced_in()
    {
        // The root keeps what it always counted; only the descendants are filtered.
        using var harness = new Harness(pricingCurrency: "USD", priceCurrency: "EUR");

        await harness.Agent("router", Usage(4, 6)).RunAsync(Message());

        var counter = (await harness.CountersAsync(Tenant, "router")).Single(static record => record.Period == QuotaPeriod.Daily);

        counter.Runs.ShouldBe(1);
        counter.Tokens.ShouldBe(10);
        counter.Cost.ShouldBe(0.03m);
    }

    [Theory]
    [InlineData("EUR", "USD", true)]
    [InlineData("EUR", null, true)]
    [InlineData("USD", "USD", false)]
    [InlineData("usd", "USD", false)]
    public void A_child_in_another_currency_keeps_the_amounts_it_reported_beyond_tokens(string? priceCurrency, string? pricingCurrency, bool tokenCostLeftOut)
    {
        // The reported amounts are in the pricing currency by construction (the accumulator
        // adds nothing else), so they count whatever currency the model's tokens were priced in.
        var cost = new RunCost { InputCost = 0.01m, OutputCost = 0.02m, Currency = priceCurrency, Source = PricingSource.Catalog };
        var reported = new RunNonTokenCost { Amount = 0.25m, Currency = pricingCurrency };
        var ownSpend = RunSpend.Total(cost, reported);

        var amount = RunRecordingAgent.QuotaAmount(isRoot: false, ownSpend, cost, reported, pricingCurrency, out var leftOut);

        leftOut.ShouldBe(tokenCostLeftOut);
        amount.ShouldBe(tokenCostLeftOut ? 0.25m : ownSpend);

        if (!tokenCostLeftOut)
        {
            amount.ShouldBe(0.28m);
        }
    }

    [Fact]
    public void A_child_in_another_currency_with_nothing_reported_beyond_tokens_adds_no_money()
    {
        var cost = new RunCost { InputCost = 0.01m, OutputCost = 0.02m, Currency = "EUR", Source = PricingSource.Catalog };

        RunRecordingAgent.QuotaAmount(isRoot: false, cost.Total(), cost, nonTokenCost: null, "USD", out var leftOut).ShouldBeNull();

        leftOut.ShouldBeTrue();
    }

    [Fact]
    public void A_root_and_an_unpriced_run_keep_their_own_spend_whatever_the_currency()
    {
        var cost = new RunCost { InputCost = 0.01m, OutputCost = 0.02m, Currency = "EUR", Source = PricingSource.Catalog };
        var unpriced = new RunCost { Currency = "EUR", Source = PricingSource.Unknown };

        RunRecordingAgent.QuotaAmount(isRoot: true, 0.03m, cost, null, "USD", out var rootLeftOut).ShouldBe(0.03m);
        RunRecordingAgent.QuotaAmount(isRoot: false, null, unpriced, null, "USD", out var unpricedLeftOut).ShouldBeNull();

        rootLeftOut.ShouldBeFalse();
        unpricedLeftOut.ShouldBeFalse();
    }

    private static AgentRunBudget ChildBudget(Harness harness)
        => new() { Account = new TreeAccount(RootTenant, RootName, "user-1", harness.Enforcer) };

    private static TraconRunOptions ChildOptions(AgentRunBudget budget)
        => new()
        {
            RunId = TraconId.NewId(),
            Depth = 1,
            ParentRunId = TraconId.NewId(),
            RootRunId = TraconId.NewId(),
            Budget = budget,
        };

    private static ChatMessage[] Message() => [new ChatMessage(ChatRole.User, "hello")];

    private static UsageDetails Usage(int input, int output)
        => new() { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output };

    private sealed class Harness : IDisposable
    {
        private readonly string? _pricingCurrency;
        private readonly string? _priceCurrency;

        public Harness(string? pricingCurrency = null, string? priceCurrency = null)
        {
            _pricingCurrency = pricingCurrency;
            _priceCurrency = priceCurrency;
            Store = new InMemoryRunStore(tenantContext: new FixedTenantContext());
            QuotaStore = new InMemoryQuotaStore();
            Enforcer = new QuotaEnforcer(QuotaStore, new StaticOptionsMonitor(new TraconQuotaOptions()));
        }

        public InMemoryRunStore Store { get; }

        public InMemoryQuotaStore QuotaStore { get; }

        public QuotaEnforcer Enforcer { get; }

        public RunRecordingAgent Agent(string name, UsageDetails? usage = null)
        {
            var client = new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")) { Usage = usage });
            var compiler = new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider(client)),
                TestData.Registry());

            return new RunRecordingAgent(
                compiler.Compile(TestData.Definition(name)),
                Store,
                new FixedTenantContext(),
                new TraconRunRecordingOptions(),
                NullLogger<RunRecordingAgent>.Instance,
                pricingResolver: new FakeRunPricingResolver((_, _, runUsage) => runUsage is null
                    ? null
                    : new RunCost
                    {
                        InputCost = 0.01m,
                        OutputCost = 0.02m,
                        Currency = _priceCurrency,
                        Source = PricingSource.Catalog,
                    }),
                quotaEnforcer: Enforcer,
                pricingCurrency: _pricingCurrency);
        }

        public async Task<IReadOnlyList<QuotaUsageRecord>> CountersAsync(string tenantId, string agentName)
            => await QuotaStore.GetUsageAsync(new QuotaUsageQuery { TenantId = tenantId, AgentName = agentName });

        public void Dispose()
        {
        }

        private sealed class FixedTenantContext : ITenantContext
        {
            public string TenantId => Tenant;
        }

        private sealed class StaticOptionsMonitor(TraconQuotaOptions value) : IOptionsMonitor<TraconQuotaOptions>
        {
            public TraconQuotaOptions CurrentValue => value;

            public TraconQuotaOptions Get(string? name) => value;

            public IDisposable? OnChange(Action<TraconQuotaOptions, string?> listener) => null;
        }
    }
}
