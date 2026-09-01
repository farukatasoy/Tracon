namespace AgentPrism.Core.UnitTests.Graph;

/// <summary>
/// The counters of the budget shared across the tree.
/// </summary>
public sealed class AgentRunBudgetTests
{
    [Fact]
    public void Unlimited_budget_always_reserves_a_slot()
    {
        var budget = new AgentRunBudget();

        for (var index = 0; index < 100; index++)
        {
            budget.TryReserveRun().ShouldBeTrue();
        }

        budget.StartedRuns.ShouldBe(100);
    }

    [Fact]
    public void No_slot_is_reserved_once_the_run_count_limit_is_exceeded()
    {
        var budget = new AgentRunBudget { MaxTotalRuns = 2 };

        budget.TryReserveRun().ShouldBeTrue();
        budget.TryReserveRun().ShouldBeTrue();
        budget.TryReserveRun().ShouldBeFalse();

        // A failed attempt does not increment the counter; otherwise, on a
        // tree that has reached the limit, every new attempt would show runs
        // in the UI that never actually started.
        budget.StartedRuns.ShouldBe(2);
    }

    [Fact]
    public void No_new_run_starts_once_the_token_limit_is_reached()
    {
        var budget = new AgentRunBudget { MaxTotalTokens = 1_000 };

        budget.TryReserveRun().ShouldBeTrue();
        budget.RecordUsage(999);
        budget.TryReserveRun().ShouldBeTrue();

        budget.RecordUsage(1);

        budget.IsTokenBudgetExhausted.ShouldBeTrue();
        budget.TryReserveRun().ShouldBeFalse();
        budget.DescribeExhaustion().ShouldContain("token budget is exhausted", Case.Sensitive);
    }

    [Fact]
    public void Negative_usage_is_ignored()
    {
        var budget = new AgentRunBudget();

        budget.RecordUsage(-5);

        budget.ConsumedTokens.ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_reservation_does_not_exceed_the_limit()
    {
        // Child runs start concurrently: MAF's background agent tasks run
        // without blocking, and the same budget is read from multiple threads.
        var budget = new AgentRunBudget { MaxTotalRuns = 10 };
        var granted = 0;

        await Parallel.ForAsync(0, 200, (_, _) =>
        {
            if (budget.TryReserveRun())
            {
                Interlocked.Increment(ref granted);
            }

            return ValueTask.CompletedTask;
        });

        granted.ShouldBe(10);
        budget.StartedRuns.ShouldBe(10);
    }

    [Fact]
    public void Budget_built_from_settings_carries_the_defaults()
    {
        var budget = new AgentPrismAgentGraphOptions().CreateBudget(TimeProvider.System);

        budget.MaxDepth.ShouldBe(3);
        budget.MaxTotalTokens.ShouldBe(200_000);
        budget.MaxTotalRuns.ShouldBe(25);
        budget.MaxDuration.ShouldBeNull();
        budget.Deadline.ShouldBeNull();
    }

    [Fact]
    public void Zero_value_removes_the_limit()
    {
        var budget = new AgentPrismAgentGraphOptions
        {
            MaxTotalTokens = 0,
            MaxTotalRuns = 0,
            MaxDuration = TimeSpan.Zero,
        }.CreateBudget(TimeProvider.System);

        budget.MaxTotalTokens.ShouldBeNull();
        budget.MaxTotalRuns.ShouldBeNull();
        budget.MaxDuration.ShouldBeNull();
        budget.Deadline.ShouldBeNull();
    }
}
