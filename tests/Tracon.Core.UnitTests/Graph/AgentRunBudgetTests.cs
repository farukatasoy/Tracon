using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Graph;

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
        var budget = new TraconAgentGraphOptions().CreateBudget(TimeProvider.System);

        budget.MaxDepth.ShouldBe(3);
        budget.MaxTotalTokens.ShouldBe(200_000);
        budget.MaxTotalRuns.ShouldBe(25);
        budget.MaxDuration.ShouldBeNull();
        budget.Deadline.ShouldBeNull();
    }

    [Fact]
    public void Zero_value_removes_the_limit()
    {
        var budget = new TraconAgentGraphOptions
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

    // --- an earlier fix: where a function node may start ---

    [Fact]
    public void A_spent_token_budget_does_not_refuse_a_node_start()
    {
        // Token is on by default (200,000) and a function node spends none: stopping it on
        // this boundary would change every installation's behavior.
        var budget = new AgentRunBudget { MaxTotalTokens = 1_000 };

        budget.RecordUsage(1_000);

        budget.IsTokenBudgetExhausted.ShouldBeTrue();
        budget.IsNodeStartExhausted.ShouldBeFalse();
    }

    [Fact]
    public void A_spent_run_count_does_not_refuse_a_node_start()
    {
        // A function node opens no child run.
        var budget = new AgentRunBudget { MaxTotalRuns = 1 };

        budget.TryReserveRun().ShouldBeTrue();
        budget.TryReserveRun().ShouldBeFalse();

        budget.IsNodeStartExhausted.ShouldBeFalse();
    }

    [Fact]
    public void A_spent_cost_or_time_budget_refuses_a_node_start()
    {
        var cost = new AgentRunBudget { MaxTotalCost = 1m };

        cost.RecordUsage(0, 1m);

        cost.IsNodeStartExhausted.ShouldBeTrue();

        var clock = new ManualTimeProvider();
        var time = new AgentRunBudget(TimeSpan.FromSeconds(5), clock);

        time.IsNodeStartExhausted.ShouldBeFalse();

        clock.Advance(TimeSpan.FromSeconds(5));

        time.IsNodeStartExhausted.ShouldBeTrue();
    }

    [Fact]
    public void The_node_start_message_names_cost_even_when_tokens_are_spent_too()
    {
        // DescribeExhaustion names the token budget first. A node is not stopped by token,
        // so a message that named it would send the operator to the wrong setting.
        var budget = new AgentRunBudget { MaxTotalTokens = 10, MaxTotalCost = 0.005m };

        budget.RecordUsage(10, 0.0076m);

        var text = budget.DescribeNodeStartExhaustion();

        text.ShouldBe(
            "The run tree's cost budget is exhausted (0.007600/0.005000). " +
            "Raise Tracon:AgentGraph:MaxTotalCost to allow more. A node that is already running is not stopped.");
        text.ShouldNotContain("token", Case.Insensitive);
    }

    [Fact]
    public void The_node_start_message_names_the_time_budget_without_the_model_turn_sentence()
    {
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(TimeSpan.FromSeconds(5), clock);

        clock.Advance(TimeSpan.FromSeconds(7));

        var text = budget.DescribeNodeStartExhaustion();

        text.ShouldContain("time budget is exhausted (00:00:07/00:00:05)", Case.Sensitive);
        text.ShouldContain("Raise Tracon:AgentGraph:MaxDuration to allow more.", Case.Sensitive);
        text.ShouldEndWith("A node that is already running is not stopped.", Case.Sensitive);
        text.ShouldNotContain("between model turns", Case.Sensitive);
    }

    [Fact]
    public void The_node_start_message_names_cost_first_when_time_is_spent_too()
    {
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(TimeSpan.FromSeconds(5), clock) { MaxTotalCost = 1m };

        budget.RecordUsage(0, 2m);
        clock.Advance(TimeSpan.FromSeconds(7));

        budget.DescribeNodeStartExhaustion().ShouldContain("cost budget is exhausted", Case.Sensitive);
        budget.DescribeNodeStartExhaustion().ShouldNotContain("time budget", Case.Sensitive);
    }

    [Fact]
    public void The_time_messages_of_the_model_call_and_the_child_run_are_unchanged_by_the_node_start_text()
    {
        // The time sentence is split in two so the node text can leave the model-turn sentence
        // out; the model call and the child run must still read the whole text.
        var clock = new ManualTimeProvider();
        var budget = new AgentRunBudget(TimeSpan.FromSeconds(5), clock);

        clock.Advance(TimeSpan.FromSeconds(7));

        const string Whole =
            "The run tree's time budget is exhausted (00:00:07/00:00:05). " +
            "Raise Tracon:AgentGraph:MaxDuration to allow more. " +
            "This is a cutoff between model turns, not a hard timeout: " +
            "a tool call already in progress is not interrupted.";

        budget.DescribeModelCallExhaustion().ShouldBe($"{Whole} No further model calls can be made in this run tree.");
        budget.DescribeExhaustion().ShouldBe($"{Whole} A new child run cannot be started.");
    }

    [Fact]
    public void The_model_call_messages_are_unchanged_by_the_node_start_text()
    {
        // The node text shares the cost and time sentences with the model-call text, one
        // copy each: pin the model call's wording so a shared edit cannot move it.
        var budget = new AgentRunBudget { MaxTotalCost = 0.005m };

        budget.RecordUsage(0, 0.0076m);

        budget.DescribeModelCallExhaustion().ShouldBe(
            "The run tree's cost budget is exhausted (0.007600/0.005000). " +
            "Raise Tracon:AgentGraph:MaxTotalCost to allow more. No further model calls can be made in this run tree.");
        budget.DescribeExhaustion().ShouldBe(
            "The run tree's cost budget is exhausted (0.007600/0.005000). " +
            "Raise Tracon:AgentGraph:MaxTotalCost to allow more. A new child run cannot be started.");
    }
}
