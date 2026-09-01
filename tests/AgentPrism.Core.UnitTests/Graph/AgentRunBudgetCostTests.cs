namespace AgentPrism.Core.UnitTests.Graph;

/// <summary>
/// The cost dimension <see cref="AgentRunBudget"/> gained in phase 114 —
/// <see cref="AgentRunBudgetTests"/> covers the pre-existing token/count
/// dimensions.
/// </summary>
public sealed class AgentRunBudgetCostTests
{
    [Fact]
    public void No_cost_limit_is_never_exhausted()
    {
        var budget = new AgentRunBudget();

        budget.RecordUsage(0, 1_000_000m);

        budget.IsCostBudgetExhausted.ShouldBeFalse();
        budget.IsExhausted.ShouldBeFalse();
    }

    [Fact]
    public void Cost_limit_trips_once_spend_reaches_it()
    {
        var budget = new AgentRunBudget { MaxTotalCost = 1.00m };

        budget.RecordUsage(0, 0.60m);
        budget.IsCostBudgetExhausted.ShouldBeFalse();

        budget.RecordUsage(0, 0.40m);
        budget.IsCostBudgetExhausted.ShouldBeTrue();
        budget.IsExhausted.ShouldBeTrue();
        budget.DescribeModelCallExhaustion().ShouldContain("cost budget is exhausted", Case.Sensitive);
    }

    [Fact]
    public void Null_cost_is_a_no_op_not_a_zero_charge()
    {
        // A turn whose price is undefined (PricingSource.Unknown) must not
        // silently register as "cost zero" — that would let a cost cap trip
        // on a tree that never actually measured a cost.
        var budget = new AgentRunBudget { MaxTotalCost = 0.01m };

        budget.RecordUsage(100, cost: null);

        budget.ConsumedCost.ShouldBe(0m);
        budget.IsCostBudgetExhausted.ShouldBeFalse();
    }

    [Fact]
    public void Negative_or_zero_cost_is_ignored()
    {
        var budget = new AgentRunBudget { MaxTotalCost = 1m };

        budget.RecordUsage(0, -5m);
        budget.RecordUsage(0, 0m);

        budget.ConsumedCost.ShouldBe(0m);
    }

    [Fact]
    public void Small_per_turn_charges_are_not_rounded_to_zero()
    {
        // Token pricing runs to small fractions of a cent per token (example:
        // $0.15 per million tokens = $0.00000015/token). Six decimal digits
        // of precision would round a single-turn charge like this to zero.
        var budget = new AgentRunBudget();

        budget.RecordUsage(1, 0.00000015m);

        budget.ConsumedCost.ShouldBe(0.00000015m);
    }

    [Fact]
    public async Task Concurrent_recording_sums_exactly_decimal_is_not_Interlocked_safe()
    {
        // 🚨 decimal has no Interlocked.Add overload; AgentRunBudget tracks
        // cost as a fixed-point long internally FOR THIS REASON. This test
        // proves the public RecordUsage(long, decimal?) contract sums
        // correctly under real concurrency, not that any particular internal
        // representation was used.
        var budget = new AgentRunBudget();
        const decimal perCall = 0.0001m;
        const int calls = 500;

        await Parallel.ForAsync(0, calls, (_, _) =>
        {
            budget.RecordUsage(1, perCall);
            return ValueTask.CompletedTask;
        });

        budget.ConsumedCost.ShouldBe(perCall * calls);
        budget.ConsumedTokens.ShouldBe(calls);
    }

    [Fact]
    public void TryReserveRun_also_refuses_once_the_cost_limit_is_reached()
    {
        // The run-count reservation gate (pre-existing) must honor the NEW
        // cost dimension the same way it already honors the token dimension —
        // otherwise a cost-exhausted tree could still spawn new child runs.
        var budget = new AgentRunBudget { MaxTotalCost = 1m };

        budget.RecordUsage(0, 1m);

        budget.TryReserveRun().ShouldBeFalse();
        budget.DescribeExhaustion().ShouldContain("cost budget is exhausted", Case.Sensitive);
    }

    [Fact]
    public void Zero_or_negative_MaxTotalCost_removes_the_limit()
    {
        var budget = new AgentPrismAgentGraphOptions { MaxTotalCost = 0m }.CreateBudget(TimeProvider.System);

        budget.MaxTotalCost.ShouldBeNull();
    }

    [Fact]
    public void Positive_MaxTotalCost_carries_through()
    {
        var budget = new AgentPrismAgentGraphOptions { MaxTotalCost = 12.5m }.CreateBudget(TimeProvider.System);

        budget.MaxTotalCost.ShouldBe(12.5m);
    }
}
