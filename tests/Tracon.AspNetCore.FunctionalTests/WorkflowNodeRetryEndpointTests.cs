using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A function node with a retry policy repeats only a failure that a retry can change.
/// </summary>
/// <remarks>
/// 🚨 F-338: the retry decision read only the error class, and
/// <see cref="RunErrorClass.ProviderError"/> also holds a permanent 4xx. A node that got
/// a 401 from <c>EnsureSuccessStatusCode()</c> ran <c>MaxAttempts</c> times. The cases run
/// through the real host, so the classifier is the one DI registers and the node is the
/// one <c>AddWorkflowFunction</c> wraps.
/// </remarks>
public sealed class WorkflowNodeRetryEndpointTests
{
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 1)]
    [InlineData(HttpStatusCode.NotFound, 1)]
    [InlineData(HttpStatusCode.ServiceUnavailable, 3)]
    public async Task A_node_is_retried_only_when_its_http_status_is_not_permanent(HttpStatusCode status, int expectedCalls)
    {
        var calls = 0;

        await using var host = await TraconTestHost.StartAsync(builder => builder
            .AddAgent(TestData.Definition("writer"))
            .UseWorkflows()
            .AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
                "call-api",
                _ => (_, _, _) =>
                {
                    Interlocked.Increment(ref calls);

                    // Exactly what HttpResponseMessage.EnsureSuccessStatusCode() throws.
                    throw new HttpRequestException(
                        $"Response status code does not indicate success: {(int)status} ({status}).",
                        inner: null,
                        status);
                },
                retryPolicy: new WorkflowNodeRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(1) }));

        using (var saved = await host.Client.PutAsJsonAsync("/tracon/api/workflows/calls", new WorkflowSaveRequest
        {
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "call-api", Kind = WorkflowNodeKind.Function },
            ],
        }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        }

        using (var response = await host.Client.PostAsJsonAsync(
                   "/tracon/api/workflows/calls/run",
                   new WorkflowRunHttpRequest { Message = "look up the shipment" }))
        {
            _ = await response.Content.ReadAsStringAsync();
        }

        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { Take = 20, OnlyRootRuns = false });
        var workflow = runs.Single(static run => run.Kind == RunKind.Workflow);

        Volatile.Read(ref calls).ShouldBe(expectedCalls);
        workflow.Status.ShouldBe(RunStatus.Failed);
    }
}
