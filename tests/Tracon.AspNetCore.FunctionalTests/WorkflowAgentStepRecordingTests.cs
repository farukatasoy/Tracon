using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.AgentStepHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The agent step of a workflow asks the agent for its whole answer instead of a stream (phase 211): the child run keeps
/// the messages, the usage and the cost it kept before, and a refusal still fails the workflow.
/// </summary>
/// <remarks>
/// <para>
/// The step never forwarded a delta to the workflow's feed, so the stream bought nothing; what it gave was the child row.
/// Measured before the change: an agent step's row held <c>run.started</c>, <c>message.delta</c> and <c>run.completed</c>,
/// the model's tokens and cost, and the input. The same step asked for the whole answer holds the same, with one
/// <c>message.completed</c> more and <c>IsStreaming = false</c>.
/// </para>
/// </remarks>
public sealed class WorkflowAgentStepRecordingTests
{
    [Theory]
    [InlineData(Stored)]
    [InlineData(Code)]
    public async Task The_steps_row_keeps_its_messages_usage_and_cost(string workflow)
    {
        var provider = PricedProvider().RespondsWith("the answer", inputTokens: 4, outputTokens: 6);
        await using var host = await StartAsync(provider);

        var run = await RunAsync(host, workflow);

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        var step = (await StepRowsAsync(host, run.Id)).ShouldHaveSingleItem();

        step.AgentName.ShouldBe(Agent);
        step.Status.ShouldBe(RunStatus.Completed);
        step.ModelId.ShouldBe(Model);

        var usage = step.Usage.ShouldNotBeNull();

        usage.InputTokens.ShouldBe(4);
        usage.OutputTokens.ShouldBe(6);
        step.Cost.ShouldNotBeNull().Total().ShouldBe(0.016m);

        // The tree reads the step's tokens and cost back into the workflow's row.
        run.TreeUsage.ShouldNotBeNull().TotalTokens.ShouldBe(10);

        var events = await EventsAsync(host, step.Id);

        events.ShouldContain("run.started", Case.Sensitive);
        events.ShouldContain("message.completed", Case.Sensitive);
        events.ShouldContain("the answer", Case.Sensitive);
        events.ShouldContain("run.completed", Case.Sensitive);
    }

    [Theory]
    [InlineData(Stored)]
    [InlineData(Code)]
    public async Task The_steps_input_is_recorded_with_the_message_it_was_given(string workflow)
    {
        var provider = PricedProvider().RespondsWith("the answer");
        await using var host = await StartAsync(provider);

        var run = await RunAsync(host, workflow);
        var step = (await StepRowsAsync(host, run.Id)).ShouldHaveSingleItem();

        using var input = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{step.Id}/input", UriKind.Relative));

        input.EnsureSuccessStatusCode();
        (await input.Content.ReadAsStringAsync()).ShouldContain("Episode 12", Case.Sensitive);
    }

    [Theory]
    [InlineData(Stored)]
    [InlineData(Code)]
    public async Task A_step_whose_tool_needs_approval_fails_the_workflow_instead_of_passing_the_refusal_on(string workflow)
    {
        var provider = PricedProvider().CallsTool("cancel_order", new { orderId = "ORD-7" }).EchoesLastToolResult();
        var cancelled = new int[1];

        await using var host = await StartAsync(
            provider,
            WriterAgent(format: null, "cancel_order"),
            builder => builder.AddTool(
                (Func<string, string>)(orderId =>
                {
                    Interlocked.Increment(ref cancelled[0]);

                    return $"{orderId} canceled.";
                }),
                name: "cancel_order",
                description: "Cancels an order.",
                configure: options => options.RequiresApproval = true));

        var run = await RunAsync(host, workflow);

        // A workflow step cannot wait for a tool approval; the refusal is the run's failure, not the next node's input.
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Message.ShouldContain("requires user approval", Case.Sensitive);
        cancelled[0].ShouldBe(0, "the tool ran although it needs approval");
    }
}
