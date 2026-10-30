using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Tracon.Testing.TraconTestHost and this project's own host share a
// name, so the package is imported one type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A run that resumes after an approval decision and asks for a second
/// approval puts it in the mailbox, and the chain goes on.
/// </summary>
/// <remarks>
/// 🚨 Only the queued-run handler wrote <see cref="PendingApproval"/> rows. The
/// resumed run closed with <c>AwaitingApproval</c>, but nothing listed its
/// request: <c>GET /api/approvals/pending</c> answered an empty list and the
/// chain stopped there, with no way to answer it.
/// </remarks>
public sealed class ApprovalChainTests
{
    private const string AgentName = "chain-agent";
    private const string SessionId = "chain-session";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/tracon/api/approvals/pending", UriKind.Relative);

    [Fact]
    public async Task A_resumed_run_that_asks_for_a_second_approval_lists_it_and_the_chain_completes()
    {
        var refunds = 0;

        await using var host = await TraconTestHost.StartAsync(
            builder =>
            {
                builder
                .AddModelProvider(new FakeModelProvider("chain-model")
                    .CallsTool("cancel_order", new { orderId = "ORD-7" })
                    .CallsTool("refund_order", new { orderId = "ORD-7" })
                    .EchoesLastToolResult())
                .AddTool(
                    (Func<string, string>)(orderId => $"{orderId} canceled."),
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: static options => options.RequiresApproval = true)
                .AddTool(
                    (Func<string, string>)(orderId =>
                    {
                        Interlocked.Increment(ref refunds);

                        return $"{orderId} refunded.";
                    }),
                    name: "refund_order",
                    description: "Refunds an order.",
                    configure: static options => options.RequiresApproval = true)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "chain-model", Model = "chain-1" },
                    ToolNames = ["cancel_order", "refund_order"],
                });
                builder.UseScheduling(
                static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
            });

        var firstRunId = await QueueAsync(host);
        await host.WaitForRunStatusAsync(firstRunId, "AwaitingApproval");

        var first = await SinglePendingAsync(host);
        first.GetProperty("toolName").GetString().ShouldBe("cancel_order");
        await DecideAsync(host, first.GetProperty("id").GetGuid());

        // The resumed run calls the approved tool, and the model then asks for
        // the refund: the run closes AwaitingApproval again.
        var second = await WaitForSessionRunAsync(host, [firstRunId], RunStatus.AwaitingApproval);

        var secondApproval = await SinglePendingAsync(host);
        secondApproval.GetProperty("toolName").GetString().ShouldBe("refund_order");
        secondApproval.GetProperty("runId").GetGuid().ShouldBe(second.Id);
        await DecideAsync(host, secondApproval.GetProperty("id").GetGuid());

        var third = await WaitForSessionRunAsync(host, [firstRunId, second.Id], RunStatus.Completed);

        third.Status.ShouldBe(RunStatus.Completed);
        Volatile.Read(ref refunds).ShouldBe(1);
    }

    private static async Task<Guid> QueueAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "cancel and refund the order", SessionId = SessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<JsonElement> SinglePendingAsync(TraconTestHost host)
    {
        using var pending = await host.Client.GetAsync(PendingApprovals);

        return (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem().Clone();
    }

    private static async Task DecideAsync(TraconTestHost host, Guid approvalId)
    {
        using var decided = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        decided.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>Waits for a run of the session, other than <paramref name="known"/>, to settle.</summary>
    private static async Task<RunRecord> WaitForSessionRunAsync(TraconTestHost host, Guid[] known, RunStatus expected)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        var run = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = true }))
                .FirstOrDefault(record => !known.Contains(record.Id)),
            static record => record is { Status: RunStatus.AwaitingApproval or RunStatus.Completed or RunStatus.Failed },
            $"a new run of session '{SessionId}' to settle");

        run!.Status.ShouldBe(expected, run.Error?.Message);

        return run;
    }
}
