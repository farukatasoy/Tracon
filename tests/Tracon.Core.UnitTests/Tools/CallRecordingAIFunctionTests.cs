using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Tools;

/// <summary>
/// The layer that writes a tool call into the run's ledger (F-689): what it records, and that it changes nothing
/// else. Every case runs through a real <see cref="FunctionInvokingChatClient"/>, because the call identity the
/// layer keys on exists only inside the tool-call loop.
/// </summary>
public sealed class CallRecordingAIFunctionTests
{
    [Fact]
    public async Task A_call_that_succeeds_is_recorded_with_the_result_the_loop_reads()
    {
        var ledger = new ToolCallLedger(TimeProvider.System);

        var response = await RunAsync(
            new CallRecordingAIFunction(AIFunctionFactory.Create(() => "sunny", "lookup")),
            ledger);

        var call = ledger.Unclaimed().ShouldHaveSingleItem();

        call.Call.Name.ShouldBe("lookup");
        call.Call.CallId.ShouldBe("call-1");
        call.IsSettled.ShouldBeTrue();
        call.Failure.ShouldBeNull();
        call.Result.ShouldNotBeNull().ToString().ShouldBe("sunny");
        response.Text.ShouldBe("done");
    }

    [Fact]
    public async Task A_call_that_throws_is_recorded_with_the_exception_and_the_exception_still_travels()
    {
        // The loop turns the exception into the tool's result for the model; the layer sees it first.
        var ledger = new ToolCallLedger(TimeProvider.System);
        var failure = new InvalidOperationException("upstream connection reset");

        var response = await RunAsync(
            new CallRecordingAIFunction(AIFunctionFactory.Create((Func<string>)(() => throw failure), "lookup")),
            ledger);

        var call = ledger.Unclaimed().ShouldHaveSingleItem();

        call.IsSettled.ShouldBeTrue();
        call.Failure.ShouldBeSameAs(failure);
        call.Result.ShouldBeNull();
        response.Messages
            .SelectMany(static message => message.Contents)
            .OfType<FunctionResultContent>()
            .ShouldHaveSingleItem()
            .Exception.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task A_call_that_the_run_cancels_stays_open()
    {
        // The run ended before the result: the run record says so, so the ledger must not make up an outcome.
        var ledger = new ToolCallLedger(TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var function = new CallRecordingAIFunction(AIFunctionFactory.Create(
            async (CancellationToken token) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token); // delay: simulated

                return "never";
            },
            "lookup"));

        var run = RunAsync(function, ledger, cancellationToken: cancellation.Token);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await run);

        var call = ledger.Unclaimed().ShouldHaveSingleItem();

        call.IsSettled.ShouldBeFalse();
        call.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task A_call_in_a_run_with_no_ledger_goes_through_untouched()
    {
        var ran = false;

        var response = await RunAsync(
            new CallRecordingAIFunction(AIFunctionFactory.Create(() => { ran = true; return "sunny"; }, "lookup")),
            ledger: null);

        ran.ShouldBeTrue();
        response.Text.ShouldBe("done");
    }

    [Fact]
    public async Task A_call_outside_the_tool_call_loop_goes_through_untouched()
    {
        // No call identity exists outside the loop (a tool called by hand): there is nothing to key the call on.
        var ledger = new ToolCallLedger(TimeProvider.System);
        var function = new CallRecordingAIFunction(AIFunctionFactory.Create(() => "sunny", "lookup"));

        TraconRunContext.SetCurrent(Scope(ledger));

        try
        {
            var result = await function.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), TestContext.Current.CancellationToken);

            result?.ToString().ShouldBe("sunny");
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        ledger.Unclaimed().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_chain_composed_twice_around_one_tool_records_the_call_once()
    {
        var ledger = new ToolCallLedger(TimeProvider.System);

        await RunAsync(
            new CallRecordingAIFunction(new CallRecordingAIFunction(AIFunctionFactory.Create(() => "sunny", "lookup"))),
            ledger);

        ledger.Unclaimed().ShouldHaveSingleItem().IsSettled.ShouldBeTrue();
    }

    [Fact]
    public async Task In_the_composed_chain_a_refused_call_is_recorded_with_the_sentence_the_model_reads()
    {
        // The layer sits outside ExplainedFailure and the budget gate: it holds the refusal as the result, and the
        // exception under it stays with the layer that turned it into one.
        var ledger = new ToolCallLedger(TimeProvider.System);
        var ran = false;
        var spent = new AgentRunBudget { MaxTotalCost = 1m };
        spent.RecordUsage(0, 5m);

        var chain = (AIFunction)ToolWrapperChain.Compose(
            new TraconToolRegistration(AIFunctionFactory.Create(() => { ran = true; return "spoken"; }, "speak")),
            new ToolDescriptor { Name = "speak", Effect = ToolEffect.Read },
            new AllowAllToolAuthorizationHandler(),
            TestData.OpenToolPolicy(),
            NoOpToolArgumentsValidator.Instance,
            defaultTimeout: TimeSpan.FromSeconds(30),
            defaultMaxOutputBytes: null,
            attribution: null,
            NullLogger<AuthorizingAIFunction>.Instance,
            NullLogger<TimeoutAIFunction>.Instance,
            NullLogger<ValidatingAIFunction>.Instance);

        await RunAsync(chain, ledger, scope: Scope(ledger) with { Budget = spent }, toolName: "speak");

        ran.ShouldBeFalse();

        var call = ledger.Unclaimed().ShouldHaveSingleItem();

        call.IsSettled.ShouldBeTrue();
        call.Failure.ShouldBeNull();
        call.Result.ShouldBeOfType<string>().ShouldContain("The tool call did not start.", Case.Sensitive);
    }

    private static AgentRunScope Scope(ToolCallLedger? ledger)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            ToolCalls = ledger,
        };

    /// <summary>
    /// Runs one model turn that asks for <paramref name="function"/> and a second that answers, through the real
    /// tool-call loop, inside a run scope. The scope is set in this method's own body: an ambient value written in a
    /// helper does not flow back to its caller.
    /// </summary>
    private static async Task<ChatResponse> RunAsync(
        AIFunction function,
        ToolCallLedger? ledger,
        AgentRunScope? scope = null,
        string toolName = "lookup",
        CancellationToken cancellationToken = default)
    {
        var turn = 0;

        using var inner = new FakeChatClient(_ => ++turn == 1
            ? new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", toolName, arguments: null)]))
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, "done")));
        using var client = new FunctionInvokingChatClient(inner);

        TraconRunContext.SetCurrent(scope ?? Scope(ledger));

        try
        {
            return await client.GetResponseAsync("go", new ChatOptions { Tools = [function] }, cancellationToken);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }
}
