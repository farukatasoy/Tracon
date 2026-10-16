using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-398: the first function node of a node list runs once per run.
/// </summary>
/// <remarks>
/// <para>
/// Measured on Microsoft Agent Framework 1.20.0 (the tests pass on 1.25.0, re-run 2026-10-16):
/// the engine sends a <c>TurnToken</c> to the
/// first node after the run's message, on every start and every resume. A first node that
/// takes <c>object</c> accepted both and ran once with the <c>List&lt;ChatMessage&gt;</c> and
/// once with the token, so a node that writes to an outside system or calls a paid service
/// did that work twice.
/// </para>
/// <para>
/// A node list does not need the token to start, but a resume does: without it a resume from
/// a checkpoint with nothing left to run waited for the run's deadline (measured, ten
/// minutes). The token stays, and a first node that takes <c>object</c> is refused.
/// </para>
/// </remarks>
public sealed class WorkflowEntryFunctionRunsOnceTests
{
    [Fact]
    public async Task A_first_node_that_takes_object_is_refused_at_save_naming_the_turn_token()
    {
        var calls = new List<string>();

        await using var host = await StartAsync(calls);

        using var response = await SaveAsync(host, "loose-start", Function("loose"), Agent("writer"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString()!;

        detail.ShouldContain("'loose'", Case.Sensitive);
        detail.ShouldContain("turn token", Case.Sensitive);
        detail.ShouldContain("run twice", Case.Sensitive);
    }

    [Fact]
    public async Task A_stored_chain_whose_first_node_takes_object_fails_before_the_node_runs()
    {
        // A definition stored before the rule, or before a deployment widened the
        // function's input type: the compiler applies the same rule.
        var calls = new List<string>();

        await using var host = await StartAsync(calls);

        await host.Services.GetRequiredService<IWorkflowDefinitionStore>().SaveAsync(
            host.Services.GetRequiredService<ITenantContext>().TenantId,
            new WorkflowDefinition
            {
                Name = "stale-loose",
                Kind = WorkflowKind.Sequential,
                Nodes = [new WorkflowNodeReference { Name = "loose", Kind = WorkflowNodeKind.Function }],
            });

        var (runId, _) = await RunAsync(host, "stale-loose");
        var record = await GetRunAsync(host, runId);

        record.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull().Message.ShouldContain("'loose'", Case.Sensitive);

        lock (calls)
        {
            calls.ShouldBeEmpty("the node must not run, not even once");
        }
    }

    [Fact]
    public async Task A_first_node_runs_once_on_start_and_not_again_when_a_later_step_is_resumed()
    {
        var calls = new List<string>();

        await using var host = await StartAsync(calls);

        using (var saved = await SaveAsync(host, "strict-start", Function("strict"), Agent("writer")))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        var (runId, _) = await RunAsync(host, "strict-start");

        (await GetRunAsync(host, runId)).Status.ShouldBe(RunStatus.Completed);

        lock (calls)
        {
            calls.ShouldBe(["strict"], "one run, one call");
        }

        // The checkpoint written after the first node: the resume runs the agent step
        // again, and the token it sends reaches the first node too.
        var checkpoints = await host.Services.GetRequiredService<IWorkflowCheckpointStore>()
            .ListByRunAsync(host.Services.GetRequiredService<ITenantContext>().TenantId, runId);

        checkpoints.Count.ShouldBeGreaterThanOrEqualTo(2);

        using var resumed = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/runs/{runId}/resume",
            new WorkflowResumeHttpRequest { CheckpointId = checkpoints[0].CheckpointId });

        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await resumed.Content.ReadAsStreamAsync());
        var resumedId = JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();

        (await GetRunAsync(host, resumedId)).Status.ShouldBe(RunStatus.Completed);

        lock (calls)
        {
            calls.ShouldBe(["strict"], "the resume must not run the first node again");
        }
    }

    private static Task<TraconTestHost> StartAsync(List<string> calls)
        => TraconTestHost.StartAsync(builder => builder
            .AddAgent(TestData.Definition("writer"))
            .UseWorkflows()
            .AddWorkflowFunction<object, List<ChatMessage>>(
                "loose",
                _ => (input, _, _) =>
                {
                    // The side effect a real node would have: one write per call.
                    lock (calls)
                    {
                        calls.Add("loose");
                    }

                    return new ValueTask<List<ChatMessage>>(
                        input as List<ChatMessage> ?? [new ChatMessage(ChatRole.User, "not a message")]);
                })
            .AddWorkflowFunction<IEnumerable<ChatMessage>, List<ChatMessage>>(
                "strict",
                _ => (messages, _, _) =>
                {
                    lock (calls)
                    {
                        calls.Add("strict");
                    }

                    return new ValueTask<List<ChatMessage>>([.. messages]);
                }));

    private static object Agent(string name) => new { name, kind = "Agent" };

    private static object Function(string name) => new { name, kind = "Function" };

    private static Task<HttpResponseMessage> SaveAsync(TraconTestHost host, string name, params object[] nodes)
        => host.Client.PutAsJsonAsync($"/tracon/api/workflows/{name}", new { kind = "Sequential", nodes });

    private static async Task<(Guid RunId, IReadOnlyList<SseFrame> Frames)> RunAsync(TraconTestHost host, string name)
    {
        using var response = await host.Client.PostAsJsonAsync(
            $"/tracon/api/workflows/{name}/run",
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        return (JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid(), frames);
    }

    private static async Task<RunRecord> GetRunAsync(TraconTestHost host, Guid runId)
        => (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();
}
