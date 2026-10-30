using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.AgentStepHost;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The structured-response repair runs for an agent step of a workflow: a step whose model answers with JSON that
/// does not fit its schema is asked again, up to <c>MaxRepairAttempts</c>, before the workflow moves on.
/// </summary>
/// <remarks>
/// <para>
/// The repair is a decorator of the agent's whole response. A step that asked for a stream never reached it: a model that
/// answered with a broken JSON fell through to the next node as it was. The step asks for the whole answer now, and the
/// settings the agent run has (<c>Tracon:StructuredResponse:Enabled</c> and <c>MaxRepairAttempts</c>) apply to it too.
/// </para>
/// </remarks>
public sealed class WorkflowAgentStepRepairTests
{
    private static readonly AgentResponseFormat Format = new()
    {
        Kind = AgentResponseFormatKind.JsonSchema,
        Schema = JsonDocument.Parse("""{"type":"object","properties":{"answer":{"type":"integer"}},"required":["answer"]}""").RootElement.Clone(),
        SchemaName = "answer",
    };

    [Theory]
    [InlineData(Stored)]
    [InlineData(Code)]
    public async Task A_json_answer_that_does_not_fit_is_asked_again_and_the_workflow_goes_on(string workflow)
    {
        var provider = PricedProvider()
            .RespondsWith("not valid json", inputTokens: 20, outputTokens: 5)
            .RespondsWith("""{"answer":42}""", inputTokens: 8, outputTokens: 2);

        await using var host = await StartAsync(provider, WriterAgent(Format), configureServices: Repairing(maxRepairAttempts: 2));

        var run = await RunAsync(host, workflow);

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        provider.Requests.Count.ShouldBe(2);

        var step = (await StepRowsAsync(host, run.Id)).ShouldHaveSingleItem();
        var events = await EventsAsync(host, step.Id);

        events.ShouldContain("StructuredResponseRejected", Case.Sensitive);
        events.ShouldContain("StructuredResponseRepairAttempted", Case.Sensitive);

        // Neither turn's tokens were lost: the step's row holds the sum.
        step.Usage.ShouldNotBeNull().InputTokens.ShouldBe(28);
        step.Usage.OutputTokens.ShouldBe(7);
    }

    [Theory]
    [InlineData(Stored)]
    [InlineData(Code)]
    public async Task A_step_that_cannot_be_repaired_fails_the_workflow_with_the_class_of_a_rejected_answer(string workflow)
    {
        var provider = PricedProvider().RespondsWith("not valid json", "still not valid json", "still not valid json");

        await using var host = await StartAsync(provider, WriterAgent(Format), configureServices: Repairing(maxRepairAttempts: 2));

        var run = await RunAsync(host, workflow);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Class.ShouldBe(RunErrorClass.StructuredResponseInvalid);

        // MaxRepairAttempts = 2 allows three model calls: the original turn and two repairs.
        provider.Requests.Count.ShouldBe(3);
    }

    private static Action<IServiceCollection> Repairing(int maxRepairAttempts)
        => services => services.Configure<TraconStructuredResponseOptions>(options =>
        {
            options.Enabled = true;
            options.MaxRepairAttempts = maxRepairAttempts;
        });
}
