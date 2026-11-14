using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// The same alias reason as ConcurrentApprovalDecisionTests.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An approval that a run applied before its caller left cannot be applied a second time, on the management API.
/// </summary>
/// <remarks>
/// <para>
/// The answer to an approval is applied when the run starts, and the session that records it is saved when the run ends. A
/// caller that closes the stream while the approved tool runs cancels the run before the save, so the stored history still
/// held the request as pending and the same answer, sent again, ran the tool a second time. The request that applies an
/// answer now writes its identity into the session before the run starts, and a request that repeats it is refused with
/// <c>409</c> before the stream begins.
/// </para>
/// </remarks>
public sealed class ApprovalAnsweredAfterDisconnectTests
{
    private const string AgentName = "approval-disconnect-agent";
    private const string SessionId = "approval-disconnect-session";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task An_approval_whose_caller_left_while_the_tool_ran_is_refused_when_it_is_sent_again_and_the_tool_does_not_run_twice()
    {
        var executions = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await TraconTestHost.StartAsync(builder =>
        {
            builder
                .AddModelProvider(new FakeModelProvider("approval-model")
                    .CallsTool("cancel_order", new { orderId = "ORD-7" })
                    .EchoesLastToolResult())
                .AddTool(
                    (Func<string, CancellationToken, Task<string>>)(async (orderId, cancellationToken) =>
                    {
                        if (Interlocked.Increment(ref executions) == 1)
                        {
                            started.TrySetResult();

                            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false); // delay: simulated
                        }

                        return $"{orderId} canceled.";
                    }),
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: static options => options.RequiresApproval = true)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "approval-model", Model = "approval-1" },
                    ToolNames = ["cancel_order"],
                });
        });

        using var first = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "cancel the order", SessionId = SessionId },
            TestContext.Current.CancellationToken);

        var requestId = await ReadApprovalRequestIdAsync(first);

        executions.ShouldBe(0, "the tool waits for the approval");

        // The caller answers over a stream and goes away while the approved tool is running.
        using (var answer = AnswerRequest(requestId, buffered: false))
        {
            var response = await host.Client.SendAsync(answer, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            response.Dispose();
        }

        var runs = host.Services.GetRequiredService<IRunStore>();

        await WaitUntil.TrueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId }, TestContext.Current.CancellationToken))
                .Any(static run => run.Status == RunStatus.Canceled),
            "the run of the caller that left to be recorded as canceled");

        executions.ShouldBe(1);

        using var again = AnswerRequest(requestId, buffered: true);
        using var repeated = await host.Client.SendAsync(again, TestContext.Current.CancellationToken);

        repeated.StatusCode.ShouldBe(HttpStatusCode.Conflict, await repeated.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        executions.ShouldBe(1, "an approval is applied once, whatever became of the run that applied it");
    }

    private static HttpRequestMessage AnswerRequest(string requestId, bool buffered)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest
            {
                SessionId = SessionId,
                Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true }],
            }),
        };

        if (buffered)
        {
            // A request with an idempotency key takes the buffered branch, the one that can answer with a status code.
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        }

        return request;
    }

    private static async Task<string> ReadApprovalRequestIdAsync(HttpResponseMessage response)
    {
        string? id = null;

        await foreach (var frame in SseReader.ReadAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken)))
        {
            if (!string.Equals(frame.Event, "approvals", StringComparison.Ordinal))
            {
                continue;
            }

            id = JsonDocument.Parse(frame.Data).RootElement.EnumerateArray()
                .Select(static request => request.GetProperty("requestId").GetString())
                .FirstOrDefault();
            break;
        }

        id.ShouldNotBeNullOrEmpty("The run did not return a pending approval request to answer.");

        return id!;
    }
}
