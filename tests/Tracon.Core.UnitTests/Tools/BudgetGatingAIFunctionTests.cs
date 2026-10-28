using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// The tool budget gate (F-530) in the chain <see cref="ToolWrapperChain"/> composes: where it
/// sits, what it refuses, and what it leaves alone. The behavior through a real tool-call loop
/// is in <c>ToolBudgetGateTests</c>.
/// </summary>
public sealed class BudgetGatingAIFunctionTests
{
    [Fact]
    public async Task A_spent_budget_stops_the_call_before_the_authorization_handler_is_asked()
    {
        var handler = new CountingHandler();
        var ran = false;

        var function = Compose(
            AIFunctionFactory.Create(() => { ran = true; return "spoken"; }, "speak"),
            handler);

        var result = await InvokeInScopeAsync(function, SpentBudget());

        // The order is the claim: authorization is the consumer's code, perhaps a network
        // call, and a run that cannot start a tool does not ask it.
        handler.Calls.ShouldBe(0);
        ran.ShouldBeFalse();

        // ExplainedFailure sits outside the gate and turns the refusal into the result.
        result.ShouldBeOfType<string>().ShouldContain("The tool call did not start.", Case.Sensitive);
    }

    [Fact]
    public async Task A_budget_that_is_not_spent_lets_the_call_through_every_layer()
    {
        var handler = new CountingHandler();
        var ran = false;

        var function = Compose(
            AIFunctionFactory.Create(() => { ran = true; return "spoken"; }, "speak"),
            handler);

        await InvokeInScopeAsync(function, new AgentRunBudget { MaxTotalCost = 1m });

        handler.Calls.ShouldBe(1);
        ran.ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_without_a_budget_is_not_gated()
    {
        var ran = false;

        var function = Compose(
            AIFunctionFactory.Create(() => { ran = true; return "spoken"; }, "speak"),
            new CountingHandler());

        await InvokeInScopeAsync(function, budget: null);

        ran.ShouldBeTrue();
    }

    [Fact]
    public async Task A_call_outside_any_run_is_not_gated()
    {
        var ran = false;

        var function = Compose(
            AIFunctionFactory.Create(() => { ran = true; return "spoken"; }, "speak"),
            new CountingHandler());

        TraconRunContext.SetCurrent(null);

        await ((AIFunction)function).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        ran.ShouldBeTrue();
    }

    [Fact]
    public async Task The_refused_call_is_the_budget_exception_so_the_chain_above_can_explain_it()
    {
        var gate = new BudgetGatingAIFunction(AIFunctionFactory.Create(() => "spoken", "speak"));

        var exception = await Should.ThrowAsync<TraconRunBudgetExceededException>(
            () => InvokeInScopeAsync(gate, SpentBudget()).AsTask());

        exception.ErrorType.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
    }

    [Fact]
    public async Task A_cancellation_stays_a_cancellation_while_the_budget_is_not_spent()
    {
        // K-737: the gate reads no token. A call the caller cancelled is cancelled, not refused.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var function = Compose(
            AIFunctionFactory.Create(
                (CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return "spoken";
                },
                "speak"),
            new CountingHandler());

        TraconRunContext.SetCurrent(Scope(new AgentRunBudget { MaxTotalCost = 1m }));

        try
        {
            await Should.ThrowAsync<OperationCanceledException>(
                async () => await ((AIFunction)function).InvokeAsync(
                    new AIFunctionArguments(StringComparer.Ordinal),
                    cancelled.Token));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public async Task A_spent_budget_answers_before_anything_reads_the_token()
    {
        // The pair of the test above, pinned so the order is not changed by accident: the gate
        // does not read the token, so a spent run is refused whether or not its caller has
        // cancelled. The cancellation is not lost. The next await point that reads the token,
        // the next model call, sees it, as it did before the gate existed.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var ran = false;
        var function = Compose(
            AIFunctionFactory.Create(() => { ran = true; return "spoken"; }, "speak"),
            new CountingHandler());

        TraconRunContext.SetCurrent(Scope(SpentBudget()));

        try
        {
            var result = await ((AIFunction)function).InvokeAsync(
                new AIFunctionArguments(StringComparer.Ordinal),
                cancelled.Token);

            result.ShouldBeOfType<string>().ShouldContain("The tool call did not start.", Case.Sensitive);
            ran.ShouldBeFalse();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    [Fact]
    public void The_gate_is_in_the_chain_of_every_server_side_tool()
    {
        var function = Compose(AIFunctionFactory.Create(() => "spoken", "speak"), new CountingHandler());

        ((AITool)function).GetService<BudgetGatingAIFunction>().ShouldBeOfType<BudgetGatingAIFunction>();
        ((AITool)function).GetService<ExplainedFailureAIFunction>().ShouldBeOfType<ExplainedFailureAIFunction>();
        ((AITool)function).GetType().ShouldBe(typeof(CallRecordingAIFunction));
    }

    [Fact]
    public void The_refusal_names_the_limit_that_stopped_the_call()
    {
        var clock = new ManualTimeProvider();

        var tokens = new AgentRunBudget { MaxTotalTokens = 10 };
        tokens.RecordUsage(10);

        var cost = new AgentRunBudget { MaxTotalCost = 1m };
        cost.RecordUsage(0, 2m);

        var time = new AgentRunBudget(TimeSpan.FromSeconds(5), clock);
        clock.Advance(TimeSpan.FromSeconds(7));

        tokens.DescribeToolStartExhaustion().ShouldBe(
            "The run tree's token budget is exhausted (10/10). " +
            "Raise Tracon:AgentGraph:MaxTotalTokens to allow more. The tool call did not start.");
        cost.DescribeToolStartExhaustion().ShouldBe(
            "The run tree's cost budget is exhausted (2.000000/1.000000). " +
            "Raise Tracon:AgentGraph:MaxTotalCost to allow more. The tool call did not start.");
        time.DescribeToolStartExhaustion().ShouldContain("time budget is exhausted (00:00:07/00:00:05)", Case.Sensitive);
        time.DescribeToolStartExhaustion().ShouldEndWith("The tool call did not start.", Case.Sensitive);
        time.DescribeToolStartExhaustion().ShouldNotContain("between model turns", Case.Sensitive);
    }

    [Fact]
    public void Every_limit_a_model_call_is_refused_for_also_refuses_a_tool_call()
    {
        // The condition is the model call's own (IsExhausted), not the function node's
        // (cost and time only): a tool's result is read by a model call, and that call is
        // refused. The token limit therefore stops a tool.
        var budget = new AgentRunBudget { MaxTotalTokens = 10 };

        budget.IsNodeStartExhausted.ShouldBeFalse();

        budget.RecordUsage(10);

        budget.IsNodeStartExhausted.ShouldBeFalse("a token limit does not stop a function node");
        budget.IsExhausted.ShouldBeTrue();
        BudgetRefusal(budget).ShouldNotBeNull();
    }

    private static string? BudgetRefusal(AgentRunBudget budget)
    {
        TraconRunContext.SetCurrent(Scope(budget));

        try
        {
            return BudgetGatingAIFunction.Refusal();
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static AgentRunBudget SpentBudget()
    {
        var budget = new AgentRunBudget { MaxTotalCost = 1m };
        budget.RecordUsage(0, 5m);

        return budget;
    }

    private static AgentRunScope Scope(AgentRunBudget? budget)
    {
        var runId = Guid.NewGuid();

        return new AgentRunScope { RunId = runId, RootRunId = runId, Budget = budget };
    }

    private static AIFunctionDeclaration Compose(AIFunction tool, IToolAuthorizationHandler handler)
        => ToolWrapperChain.Compose(
            new TraconToolRegistration(tool),
            new ToolDescriptor { Name = tool.Name, Effect = ToolEffect.Read },
            handler,
            TestData.OpenToolPolicy(),
            NoOpToolArgumentsValidator.Instance,
            defaultTimeout: TimeSpan.FromSeconds(30),
            defaultMaxOutputBytes: null,
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

    private static async ValueTask<object?> InvokeInScopeAsync(AIFunctionDeclaration function, AgentRunBudget? budget)
    {
        // The scope is set in this method's own body: an AsyncLocal assigned in a helper does
        // not flow back to the caller.
        TraconRunContext.SetCurrent(Scope(budget));

        try
        {
            return await ((AIFunction)function).InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private sealed class CountingHandler : IToolAuthorizationHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<ToolAuthorizationResult> AuthorizeAsync(
            ToolAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(ToolAuthorizationResult.Allow());
        }
    }
}
