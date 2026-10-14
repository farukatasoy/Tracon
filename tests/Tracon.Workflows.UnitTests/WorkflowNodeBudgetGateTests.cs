using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Tracon.Tests.Common;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The start gate of a workflow function node (F-407): which budget boundary refuses it,
/// where in the call it reads, and what it leaves untouched.
/// </summary>
/// <remarks>
/// <para>
/// The gate reads the run's budget from the AMBIENT run scope, so each case opens a scope
/// of its own in its own body and clears it at the end. What the real engine does with that
/// scope, in the background task a node runs on, is the functional suite's question
/// (<c>FunctionNodeBudgetGateTests</c>).
/// </para>
/// </remarks>
public sealed class WorkflowNodeBudgetGateTests
{
    private static readonly IWorkflowContext NoContext = null!;

    [Fact]
    public async Task A_default_budget_never_refuses()
    {
        // The installation's defaults: 200,000 tokens, no cost or time ceiling. Every token
        // is spent, and the node still starts: token is not a boundary.
        var budget = new TraconAgentGraphOptions().CreateBudget(TimeProvider.System);

        budget.RecordUsage(10_000_000);
        budget.IsTokenBudgetExhausted.ShouldBeTrue();

        var calls = 0;

        await InScopeAsync(budget, async () =>
        {
            await Gate(() => calls++)(Messages, NoContext, CancellationToken.None);
        });

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_cost_below_the_ceiling_starts_the_node()
    {
        var budget = new AgentRunBudget { MaxTotalCost = 1m };

        budget.RecordUsage(0, 0.99m);

        var calls = 0;

        await InScopeAsync(budget, async () =>
        {
            await Gate(() => calls++)(Messages, NoContext, CancellationToken.None);
        });

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_cost_exactly_at_the_ceiling_refuses()
    {
        // `>=`, as the model call's gate reads it: a tree that has spent the ceiling has spent it.
        var budget = new AgentRunBudget { MaxTotalCost = 1m };

        budget.RecordUsage(0, 1m);

        var calls = 0;

        var refusal = await InScopeAsync(budget, () =>
            Should.ThrowAsync<TraconRunBudgetExceededException>(async () =>
                await Gate(() => calls++)(Messages, NoContext, CancellationToken.None)));

        calls.ShouldBe(0, "the handler was called after the refusal");
        refusal.ErrorType.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        refusal.Message.ShouldStartWith("Could not start function node 'render': ", Case.Sensitive);
        refusal.Message.ShouldContain("cost budget is exhausted (1.000000/1.000000)", Case.Sensitive);
        refusal.Message.ShouldContain("Tracon:AgentGraph:MaxTotalCost", Case.Sensitive);
    }

    [Fact]
    public async Task A_spent_time_budget_refuses_and_names_the_setting()
    {
        var clock = new ManualClock();
        var budget = new AgentRunBudget(TimeSpan.FromSeconds(5), clock);

        clock.Advance(TimeSpan.FromSeconds(5));

        var calls = 0;

        var refusal = await InScopeAsync(budget, () =>
            Should.ThrowAsync<TraconRunBudgetExceededException>(async () =>
                await Gate(() => calls++)(Messages, NoContext, CancellationToken.None)));

        calls.ShouldBe(0);
        refusal.Message.ShouldContain("time budget is exhausted", Case.Sensitive);
        refusal.Message.ShouldContain("Tracon:AgentGraph:MaxDuration", Case.Sensitive);

        // The model-turn sentence of the model call's text is about a model call, not a node.
        refusal.Message.ShouldNotContain("between model turns", Case.Sensitive);
    }

    [Fact]
    public async Task A_retried_node_does_not_start_its_next_attempt_once_the_budget_is_spent()
    {
        // The first attempt spends past the ceiling and fails with a transient error. The
        // retry is the reason this gate sits INSIDE it: the second attempt is new paid
        // work, and the tree has no room for it.
        var budget = new AgentRunBudget { MaxTotalCost = 1m };
        var attempts = 0;

        var node = WorkflowNodeRetry.Wrap(
            WorkflowNodeBudgetGate.Wrap<List<ChatMessage>, List<ChatMessage>>(
                (_, _, _) =>
                {
                    attempts++;
                    budget.RecordUsage(0, 5m);

                    throw new TraconProviderUnavailableException("flaky-provider");
                },
                "render"),
            new WorkflowNodeRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new DefaultRunErrorClassifier(),
            TimeProvider.System);

        var refusal = await InScopeAsync(budget, () =>
            Should.ThrowAsync<TraconRunBudgetExceededException>(async () =>
                await node(Messages, NoContext, CancellationToken.None)));

        attempts.ShouldBe(1, "a second attempt started after the ceiling was passed");
        refusal.Message.ShouldContain("Could not start function node 'render'", Case.Sensitive);
    }

    [Fact]
    public async Task A_cancelled_call_throws_the_cancellation_before_the_budget_refusal()
    {
        // The caller's stop wins: a run that was cancelled is Canceled, not "out of budget".
        var budget = new AgentRunBudget { MaxTotalCost = 1m };

        budget.RecordUsage(0, 5m);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var calls = 0;

        await InScopeAsync(budget, () =>
            Should.ThrowAsync<OperationCanceledException>(async () =>
                await Gate(() => calls++)(Messages, NoContext, cancellation.Token)));

        calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_call_outside_a_run_scope_passes()
    {
        // A handler called directly by a test, or by an executor that no run opened.
        TraconRunContext.SetCurrent(null);

        var calls = 0;

        await Gate(() => calls++)(Messages, NoContext, CancellationToken.None);

        calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_scope_without_a_budget_passes()
    {
        var calls = 0;

        TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), Budget = null });

        try
        {
            await Gate(() => calls++)(Messages, NoContext, CancellationToken.None);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        calls.ShouldBe(1);
    }

    private static List<ChatMessage> Messages { get; } = [new(ChatRole.User, "go")];

    private static Func<List<ChatMessage>, IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>> Gate(Action onCall)
        => WorkflowNodeBudgetGate.Wrap<List<ChatMessage>, List<ChatMessage>>(
            (messages, _, _) =>
            {
                onCall();

                return new ValueTask<List<ChatMessage>>(messages);
            },
            "render");

    private static async Task InScopeAsync(AgentRunBudget budget, Func<Task> body)
    {
        TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), Budget = budget });

        try
        {
            await body();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static async Task<T> InScopeAsync<T>(AgentRunBudget budget, Func<Task<T>> body)
    {
        TraconRunContext.SetCurrent(new AgentRunScope { RunId = Guid.NewGuid(), RootRunId = Guid.NewGuid(), Budget = budget });

        try
        {
            return await body();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }
}
