using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A function node reads the running workflow's parameter values.
/// </summary>
/// <remarks>
/// MAF's <c>IWorkflowContext</c> has no way to seed a graph's initial
/// state, so the values travel in the run's own ambient scope. An
/// <c>AsyncLocal</c> that does not flow into the executor body makes the read
/// return nothing, and only the real MAF graph can show it: a unit test that
/// calls the extension on a bare context sees the scope the test itself set.
/// </remarks>
public sealed class WorkflowFunctionParameterTests
{
    [Fact]
    public async Task A_function_node_reads_the_final_values_inside_the_real_graph()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        IReadOnlyDictionary<string, string>? seen = null;

        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            builder => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "narrate",
                _ => (messages, context, _) =>
                {
                    seen = context.GetRunParameters();

                    return new ValueTask<List<ChatMessage>>(messages);
                }));

        await SaveAsync(host);

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest
        {
            Message = "Episode 12",
            Parameters = WorkflowParameterBindingTests.Values(("tone", "formal")),
        });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        seen.ShouldNotBeNull("the function node ran but could not see the run's values");
        seen.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => $"{pair.Key}={pair.Value}")
            .ShouldBe(["tone=formal", "voice=alloy"]);
    }

    [Fact]
    public async Task A_function_node_in_a_workflow_with_no_parameters_reads_an_empty_set()
    {
        var provider = WorkflowParameterBindingTests.Provider();
        IReadOnlyDictionary<string, string>? seen = null;

        await using var host = await WorkflowParameterBindingTests.StartAsync(
            provider,
            builder => builder
                .AddAgent(WorkflowParameterBindingTests.Definition("plain", "Be plain."))
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "narrate",
                    _ => (messages, context, _) =>
                    {
                        seen = context.GetRunParameters();

                        return new ValueTask<List<ChatMessage>>(messages);
                    }));

        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/podcast",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "plain", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "narrate", Kind = WorkflowNodeKind.Function },
                ],
            });

        saved.EnsureSuccessStatusCode();

        var run = await WorkflowParameterBindingTests.RunAsync(host, "podcast", new WorkflowRunHttpRequest { Message = "Episode 12" });

        run.Status.ShouldBe(RunStatus.Completed, run.Error?.Message);

        // Empty, not an exception: the run is open, it just carries no values.
        seen.ShouldNotBeNull().Count.ShouldBe(0);
    }

    private static async Task SaveAsync(TraconTestHost host)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            "/tracon/api/workflows/podcast",
            new WorkflowSaveRequest
            {
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "narrate", Kind = WorkflowNodeKind.Function },
                ],
                Parameters =
                [
                    WorkflowParameterBindingTests.Tone(required: true),
                    new AgentParameter { Name = "voice", Kind = AgentParameterKind.Text, DefaultValue = "alloy" },
                ],
            });

        saved.EnsureSuccessStatusCode();
    }
}
