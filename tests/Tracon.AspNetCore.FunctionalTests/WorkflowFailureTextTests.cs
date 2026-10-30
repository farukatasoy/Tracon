using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A failed workflow node reaches the client, the event table and the run row
/// as display-safe text only.
/// </summary>
/// <remarks>
/// The run row already carried <c>SafeErrorText</c>, but the
/// <c>ExecutorFailed</c> event of the same failure carried the exception's own
/// message — here a host and port an HTTP client put there — to the SSE stream,
/// the event table and every sink. The stream is an external response, so the
/// case runs through the real endpoint.
/// </remarks>
public sealed class WorkflowFailureTextTests
{
    private const string Secret = "media-worker:8080";

    [Fact]
    public async Task A_node_that_throws_streams_and_stores_no_raw_exception_text()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition("writer"))
                .UseWorkflows()
                .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                    "render",
                    _ => (_, _, _) => throw new HttpRequestException($"connect to {Secret} failed")));

        using (var saved = await host.Client.PutAsJsonAsync(
                   "/tracon/api/workflows/render-chain",
                   new WorkflowSaveRequest
                   {
                       Kind = WorkflowKind.Sequential,
                       Nodes =
                       [
                           new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                           new WorkflowNodeReference { Name = "render", Kind = WorkflowNodeKind.Function },
                       ],
                   }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        string stream;

        using (var response = await host.Client.PostAsJsonAsync(
                   "/tracon/api/workflows/render-chain/run",
                   new WorkflowRunHttpRequest { Message = "render the scene" }))
        {
            stream = await response.Content.ReadAsStringAsync();
        }

        stream.ShouldNotContain(Secret, Case.Sensitive);
        stream.ShouldContain(nameof(HttpRequestException), Case.Sensitive);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false }))
            .Single(static run => run.Kind == RunKind.Workflow);

        root.Status.ShouldBe(RunStatus.Failed);

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            (runEvent.Text ?? string.Empty).ShouldNotContain(Secret, Case.Sensitive);
            (runEvent.Payload ?? string.Empty).ShouldNotContain(Secret, Case.Sensitive);
        }
    }
}
