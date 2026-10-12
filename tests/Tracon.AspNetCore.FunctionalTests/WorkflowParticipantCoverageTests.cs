using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run proves, before its first model call, that every participant can be
/// bound.
/// </summary>
/// <remarks>
/// 🚨 F-294: participants resolve lazily, one step at a time. Without the check
/// at the start of the run, the third step's missing required parameter
/// surfaced only after the first two steps had spent their tokens.
/// </remarks>
public sealed class WorkflowParticipantCoverageTests
{
    [Fact]
    public async Task A_participants_uncovered_required_parameter_fails_the_run_with_no_model_call()
    {
        var provider = WorkflowParameterBindingTests.Provider();

        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            static builder => builder.AddAgent(WorkflowParameterBindingTests.Definition(
                "stylist",
                "Style the script as {{style}}.",
                new AgentParameter { Name = "style", Kind = AgentParameterKind.Text, Required = true })));

        // The workflow supplies 'tone' only. 'writer' is covered; 'stylist'
        // needs 'style' and nothing provides it.
        await WorkflowParameterBindingTests.SaveWorkflowAsync(
            host,
            agentNames: ["writer", "stylist"],
            workflowParameters: [WorkflowParameterBindingTests.Tone(required: true)]);

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error.Message.ShouldContain("agent 'stylist'", Case.Sensitive);
        run.Error.Message.ShouldContain("'style'", Case.Sensitive);
        run.Error.Class.ShouldNotBeNull("a failed row carries its error class");

        // Zero provider calls: the first step (writer) never ran either.
        provider.Requests.ShouldBeEmpty();
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery { OnlyRootRuns = false }))
            .ShouldNotContain(candidate => candidate.ParentRunId == run.Id, "no step started");
    }

    [Fact]
    public async Task A_participant_whose_required_parameter_has_a_default_is_covered()
    {
        var provider = WorkflowParameterBindingTests.Provider();

        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            static builder => builder.AddAgent(WorkflowParameterBindingTests.Definition(
                "stylist",
                "Style the script as {{style}}.",
                new AgentParameter { Name = "style", Kind = AgentParameterKind.Text, Required = true, DefaultValue = "dry" })));

        await WorkflowParameterBindingTests.SaveWorkflowAsync(
            host,
            agentNames: ["writer", "stylist"],
            workflowParameters: [WorkflowParameterBindingTests.Tone(required: true)]);

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
        provider.Requests.Select(static request => request.Options!.Instructions)
            .ShouldContain("Style the script as dry.", StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_function_node_participant_is_not_looked_up_as_an_agent()
    {
        var provider = WorkflowParameterBindingTests.Provider();

        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            static builder => builder.AddWorkflowFunction<List<Microsoft.Extensions.AI.ChatMessage>, List<Microsoft.Extensions.AI.ChatMessage>>(
                "pass",
                static _ => (messages, _, _) => new ValueTask<List<Microsoft.Extensions.AI.ChatMessage>>(messages)));

        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/podcast",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "pass", Kind = WorkflowNodeKind.Function },
                ],
                Parameters = [WorkflowParameterBindingTests.Tone(required: true)],
            });

        saved.EnsureSuccessStatusCode();

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);
    }
}
