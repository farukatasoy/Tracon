using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-341: a node list whose node cannot take what the node before it passes on is
/// refused when it is saved, and again when a run starts.
/// </summary>
/// <remarks>
/// <para>
/// Measured on Microsoft Agent Framework 1.20.0 (the tests pass on 1.25.0, re-run 2026-10-16):
/// a message no handler of the next node
/// takes is dropped without an error. The next node never runs and the run ends
/// <c>Completed</c> with no output. A first node that cannot take the run's message
/// (<c>List&lt;ChatMessage&gt;</c>) runs nothing at all. Delivery is by assignability:
/// a node that takes <c>IEnumerable&lt;ChatMessage&gt;</c> or <c>object</c> receives a
/// <c>List&lt;ChatMessage&gt;</c>.
/// </para>
/// <para>
/// The site promised that "a shape that could not run is rejected at write time"; the
/// save accepted these chains with <c>200</c>.
/// </para>
/// </remarks>
public sealed class WorkflowNodeTypeChainTests
{
    [Fact]
    public async Task A_node_that_cannot_take_the_previous_nodes_output_is_refused_at_save_naming_both()
    {
        await using var host = await StartAsync();

        using var response = await SaveAsync(host, "broken", Agent("writer"), Function("shout"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("'shout'", Case.Sensitive);
        detail.ShouldContain("'writer'", Case.Sensitive);
        detail.ShouldContain("System.String", Case.Sensitive);
        detail.ShouldContain("List", Case.Sensitive);
    }

    [Fact]
    public async Task A_first_node_that_cannot_take_the_run_message_is_refused_at_save()
    {
        await using var host = await StartAsync();

        using var response = await SaveAsync(host, "broken-start", Function("shout"), Agent("writer"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("'shout'", Case.Sensitive);
        detail.ShouldContain("first", Case.Sensitive);
    }

    [Fact]
    public async Task A_node_that_takes_a_wider_type_is_accepted_and_receives_the_message()
    {
        await using var host = await StartAsync();

        using (var saved = await SaveAsync(host, "wide", Agent("writer"), Function("count")))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var (record, frames) = await RunAsync(host, "wide");

        record.Status.ShouldBe(RunStatus.Completed);
        frames.ShouldContain(static frame => frame.Data.Contains("messages: 1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stored_chain_that_does_not_fit_fails_the_run_instead_of_completing_with_no_output()
    {
        // A definition stored before the save-time check, or before a deployment
        // changed a function's types: the compiler applies the same rule.
        await using var host = await StartAsync();

        await host.Services.GetRequiredService<IWorkflowDefinitionStore>().SaveAsync(
            host.Services.GetRequiredService<ITenantContext>().TenantId,
            new WorkflowDefinition
            {
                Name = "stale",
                Kind = WorkflowKind.Sequential,
                Nodes =
                [
                    new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                    new WorkflowNodeReference { Name = "shout", Kind = WorkflowNodeKind.Function },
                ],
            });

        var (record, _) = await RunAsync(host, "stale");

        record.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull().Message.ShouldContain("'shout'", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition("writer"))
            .UseWorkflows()
            .AddWorkflowFunction<string, string>(
                "shout",
                static _ => static (input, _, _) => new ValueTask<string>(input.ToUpperInvariant()))
            .AddWorkflowFunction<IEnumerable<ChatMessage>, string>(
                "count",
                static _ => static (messages, _, _) => new ValueTask<string>($"messages: {messages.Count()}")));

    private static object Agent(string name) => new { name, kind = "Agent" };

    private static object Function(string name) => new { name, kind = "Function" };

    private static Task<HttpResponseMessage> SaveAsync(TraconTestHost host, string name, params object[] nodes)
        => host.Client.PutAsJsonAsync($"/tracon/api/workflows/{name}", new { kind = "Sequential", nodes });

    private static async Task<(RunRecord Record, IReadOnlyList<SseFrame> Frames)> RunAsync(TraconTestHost host, string name)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{name}/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());
        var runId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
        var record = await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId);

        return (record.ShouldNotBeNull(), frames);
    }
}
