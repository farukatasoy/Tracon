using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// The budget of a run that continues earlier runs of the same workflow starts from what they spent
/// (F-531), and the difference between that and what the tenant's counter has seen is measured from
/// there (F-529).
/// </summary>
public sealed class AgentRunBudgetCarryOverTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_carried_over_budget_starts_from_what_the_chain_spent()
    {
        var budget = new AgentRunBudget(maxDuration: null, new ManualTimeProvider(Start), new BudgetCarryOver(120, 0.8m, 3, TimeSpan.Zero))
        {
            MaxTotalCost = 1m,
            MaxTotalTokens = 150,
            MaxTotalRuns = 4,
        };

        budget.ConsumedTokens.ShouldBe(120);
        budget.ConsumedCost.ShouldBe(0.8m);
        budget.StartedRuns.ShouldBe(3);

        budget.IsCostBudgetExhausted.ShouldBeFalse();

        // The ceiling is the logical run's: 0.2 left, not 1.
        budget.RecordUsage(10, 0.2m);

        budget.IsCostBudgetExhausted.ShouldBeTrue();
        budget.TryReserveRun().ShouldBeFalse();
    }

    [Fact]
    public void The_run_count_ceiling_counts_the_runs_the_chain_started()
    {
        var budget = new AgentRunBudget(maxDuration: null, new ManualTimeProvider(Start), new BudgetCarryOver(0, 0m, 3, TimeSpan.Zero))
        {
            MaxTotalRuns = 4,
        };

        budget.TryReserveRun().ShouldBeTrue();
        budget.TryReserveRun().ShouldBeFalse();
    }

    [Fact]
    public void The_deadline_is_the_time_the_chain_has_left()
    {
        var clock = new ManualTimeProvider(Start);

        // 10 minutes used of 30: the person's three days between the two runs are not in it.
        var budget = new AgentRunBudget(TimeSpan.FromMinutes(30), clock, new BudgetCarryOver(0, 0m, 0, TimeSpan.FromMinutes(10)));

        budget.Deadline.ShouldBe(Start + TimeSpan.FromMinutes(20));
        budget.MaxDuration.ShouldBe(TimeSpan.FromMinutes(30));
        budget.IsDurationBudgetExhausted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromMinutes(20));

        budget.IsDurationBudgetExhausted.ShouldBeTrue();
    }

    [Fact]
    public void A_chain_that_used_all_of_the_time_is_exhausted_at_once_and_says_how_long_it_ran()
    {
        var budget = new AgentRunBudget(TimeSpan.FromMinutes(30), new ManualTimeProvider(Start), new BudgetCarryOver(0, 0m, 0, TimeSpan.FromMinutes(45)));

        budget.IsDurationBudgetExhausted.ShouldBeTrue();
        budget.IsNodeStartExhausted.ShouldBeTrue();
        budget.DescribeNodeStartExhaustion().ShouldContain("time budget is exhausted (00:45:00/00:30:00)", Case.Sensitive);
    }

    [Fact]
    public void A_chain_with_no_time_limit_has_no_deadline_whatever_it_used()
    {
        var budget = new AgentRunBudget(maxDuration: null, new ManualTimeProvider(Start), new BudgetCarryOver(0, 0m, 0, TimeSpan.FromDays(9)));

        budget.Deadline.ShouldBeNull();
        budget.IsDurationBudgetExhausted.ShouldBeFalse();
    }

    [Fact]
    public void An_absurd_amount_of_used_time_does_not_overflow_the_deadline()
    {
        var budget = new AgentRunBudget(TimeSpan.FromMinutes(5), new ManualTimeProvider(Start), new BudgetCarryOver(0, 0m, 0, TimeSpan.MaxValue));

        budget.IsDurationBudgetExhausted.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_carried_over_is_the_budget_a_new_run_gets()
    {
        var clock = new ManualTimeProvider(Start);
        var fresh = new AgentRunBudget(TimeSpan.FromMinutes(5), clock);
        var none = new AgentRunBudget(TimeSpan.FromMinutes(5), clock, BudgetCarryOver.None);

        none.Deadline.ShouldBe(fresh.Deadline);
        none.ConsumedTokens.ShouldBe(0);
        none.ConsumedCost.ShouldBe(0m);
        none.StartedRuns.ShouldBe(0);
        BudgetCarryOver.None.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void A_negative_carry_over_is_ignored()
    {
        var budget = new AgentRunBudget(maxDuration: null, new ManualTimeProvider(Start), new BudgetCarryOver(-5, -1m, -2, TimeSpan.FromSeconds(-9)));

        budget.ConsumedTokens.ShouldBe(0);
        budget.ConsumedCost.ShouldBe(0m);
        budget.StartedRuns.ShouldBe(0);
    }

    [Fact]
    public void The_options_build_a_carried_over_budget_with_their_own_limits()
    {
        var options = new TraconAgentGraphOptions { MaxTotalCost = 1m, MaxDuration = TimeSpan.FromMinutes(30) };

        var budget = options.CreateBudget(new ManualTimeProvider(Start), new BudgetCarryOver(0, 0.5m, 0, TimeSpan.FromMinutes(10)));

        budget.MaxTotalCost.ShouldBe(1m);
        budget.ConsumedCost.ShouldBe(0.5m);
        budget.Deadline.ShouldBe(Start + TimeSpan.FromMinutes(20));
    }

    // --- The part of the tree's spend its counter has not seen ---

    [Fact]
    public void A_tree_without_an_account_has_no_unrecorded_spend()
    {
        var budget = new AgentRunBudget();

        budget.RecordUsage(10, 0.5m);

        budget.UnrecordedSpend().ShouldBe((0, 0m));
    }

    [Fact]
    public void What_the_budget_held_when_the_account_was_named_is_not_unrecorded()
    {
        // The earlier runs of a continued workflow wrote their own share when they ended. Counted again it
        // would be counted twice.
        var budget = new AgentRunBudget(maxDuration: null, new ManualTimeProvider(Start), new BudgetCarryOver(100, 0.8m, 0, TimeSpan.Zero));

        budget.Account = Account();

        budget.UnrecordedSpend().ShouldBe((0, 0m));

        budget.RecordUsage(7, 0.25m);

        budget.UnrecordedSpend().ShouldBe((7, 0.25m));
    }

    [Fact]
    public async Task What_the_tree_wrote_to_the_counter_is_no_longer_unrecorded()
    {
        var budget = new AgentRunBudget();
        var account = Account();

        budget.Account = account;
        budget.RecordUsage(30, 1m);

        await TreeQuota.RecordAsync(account, Guid.NewGuid(), runs: 0, tokens: 12, amount: 0.4m, Start, CancellationToken.None);

        budget.UnrecordedSpend().ShouldBe((18, 0.6m));

        await TreeQuota.RecordAsync(account, Guid.NewGuid(), runs: 1, tokens: 18, amount: 0.6m, Start, CancellationToken.None);

        budget.UnrecordedSpend().ShouldBe((0, 0m));
    }

    [Fact]
    public async Task An_entry_with_nothing_to_add_leaves_the_difference_where_it_was()
    {
        var budget = new AgentRunBudget();
        var account = Account();

        budget.Account = account;
        budget.RecordUsage(30, 1m);

        // The unpriced run writes its tokens and no amount: the amount is not owed to the counter as zero.
        await TreeQuota.RecordAsync(account, Guid.NewGuid(), runs: 0, tokens: 5, amount: null, Start, CancellationToken.None);

        budget.UnrecordedSpend().ShouldBe((25, 1m));
    }

    [Fact]
    public async Task A_tree_that_wrote_more_than_the_budget_measured_is_not_negative()
    {
        // A run priced in another currency than the installation's writes an amount the budget did not hold.
        var budget = new AgentRunBudget();
        var account = Account();

        budget.Account = account;
        budget.RecordUsage(10);

        await TreeQuota.RecordAsync(account, Guid.NewGuid(), runs: 0, tokens: 40, amount: 3m, Start, CancellationToken.None);

        budget.UnrecordedSpend().ShouldBe((0, 0m));
    }

    [Fact]
    public void A_budget_a_second_root_reuses_starts_the_second_roots_difference_from_what_it_holds()
    {
        var budget = new AgentRunBudget();

        budget.Account = Account();
        budget.RecordUsage(10, 0.5m);

        budget.Account = Account();

        budget.UnrecordedSpend().ShouldBe((0, 0m));
    }

    [Fact]
    public void A_cost_is_measured_for_a_cost_limit_and_for_a_quota_that_is_on()
    {
        new AgentRunBudget().MeasuresCost.ShouldBeFalse();
        new AgentRunBudget { MaxTotalCost = 1m }.MeasuresCost.ShouldBeTrue();

        var onQuota = new AgentRunBudget { Account = Account(new StubRecorder(enabled: true)) };
        var offQuota = new AgentRunBudget { Account = Account(new StubRecorder(enabled: false)) };
        var noQuota = new AgentRunBudget { Account = Account((IQuotaRecorder?)null) };

        onQuota.MeasuresCost.ShouldBeTrue();
        offQuota.MeasuresCost.ShouldBeFalse();
        noQuota.MeasuresCost.ShouldBeFalse();
    }

    [Fact]
    public async Task Two_threads_writing_to_the_ledger_lose_nothing()
    {
        var ledger = new TreeLedger();

        await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            {
                for (var index = 0; index < 5_000; index++)
                {
                    ledger.Record(2, 0.001m);
                }
            })));

        ledger.RecordedTokens.ShouldBe(8 * 5_000 * 2);
        ledger.RecordedCostNanoUnits.ShouldBe(8L * 5_000 * 1_000_000);
    }

    private static TreeAccount Account() => Account(new StubRecorder(enabled: true));

    private static TreeAccount Account(IQuotaRecorder? recorder) => new("acme", "podcast", UserId: null, recorder);

    private sealed class StubRecorder(bool enabled) : IQuotaRecorder
    {
        public bool IsEnabled => enabled;

        public ValueTask<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(QuotaConsumption consumption, CancellationToken cancellationToken = default)
            => new([]);

        public ValueTask<QuotaDecision> CheckTreeStartAsync(
            TreeAccount account,
            TreeStartKind kind,
            long unrecordedTokens,
            decimal unrecordedCost,
            CancellationToken cancellationToken = default)
            => new(QuotaDecision.Allowed);
    }
}
