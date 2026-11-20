using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// The same alias reason as ApprovalResumeHandoffTests: a blanket `using Tracon.Testing;` collides with this project's
// TraconTestHost.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The mailbox that applies a decided tool approval marks the answer in the session before the tool runs, as the HTTP path does.
/// </summary>
/// <remarks>
/// <para>
/// A request that applies an answer over HTTP writes the answer's identity into the session before the run starts, so that
/// the same answer sent again is refused with <c>409</c> while the run that applied it has not saved the session. The
/// approval-resume job did not: it fed the answer to the agent and saved the session only when the turn ended. The session
/// held the request as pending for the whole time the approved tool ran, and a client that answered the same request over
/// HTTP ran the tool a second time, whether the worker was still running it or had died in it.
/// </para>
/// <para>
/// The mailbox's own re-lease was never the hole: the job has one attempt, and a second lease ends with
/// <c>attempts-exhausted</c> before any handler runs.
/// </para>
/// </remarks>
public sealed class ApprovalResumeAnswerMarkTests
{
    private const string AgentName = "mailbox-mark-agent";
    private const string SessionId = "mailbox-mark-session";
    private const string SessionSaveFailed = "tracon.job.session-save-failed";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/tracon/api/approvals/pending", UriKind.Relative);

    [Fact]
    public async Task An_approval_the_mailbox_is_applying_is_refused_over_HTTP_and_the_tool_runs_once()
    {
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var host = await StartAsync(
            async (orderId, cancellationToken) =>
            {
                // Only the first call waits: a second call (the defect) returns at once and shows itself in the count.
                if (Interlocked.Increment(ref calls) == 1)
                {
                    started.TrySetResult();
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                return $"{orderId} canceled.";
            });

        Guid resumedRunId;

        try
        {
            var sourceRunId = await QueueAsync(host);

            await host.WaitForRunStatusAsync(sourceRunId, "AwaitingApproval");

            var (approvalId, requestId) = await PendingApprovalAsync(host);

            await DecideAsync(host, approvalId);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            // The mailbox's run is inside the approved tool now. The same answer, sent over HTTP, must not start a second one.
            using var again = await AnswerOverHttpAsync(host, requestId);

            again.StatusCode.ShouldBe(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            calls.ShouldBe(1, "the approval was applied twice: the mailbox's run and the HTTP answer each ran the tool");
            (await PendingRequestIdsAsync(host)).ShouldBeEmpty("the answer was applied: the console must not offer the same request again");

            resumedRunId = await ResumedRunIdAsync(host, sourceRunId);
        }
        finally
        {
            release.TrySetResult();
        }

        await host.WaitForRunStatusAsync(resumedRunId, "Completed");
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_approval_whose_mailbox_worker_died_in_the_tool_is_refused_over_HTTP_and_the_tool_does_not_run_again()
    {
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var teardown = new CancellationTokenSource();
        Task? doomed = null;

        await using var host = await StartAsync(
            async (orderId, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    started.TrySetResult();

                    // A worker that died in the tool: it ignores the run's token and never comes back.
                    await Task.Delay(Timeout.Infinite, teardown.Token).ConfigureAwait(false); // delay: simulated
                }

                return $"{orderId} canceled.";
            },
            runWorker: false);

        try
        {
            // The host's own worker is off: this test plays every worker, so the first turn runs by hand too.
            var sourceRunId = await QueueAsync(host);
            var first = await QueuedWorkflowHarness.LeaseAsync(host, "first-worker", TimeSpan.FromMinutes(1));

            await QueuedWorkflowHarness.RunAttemptAsync(host, first, "first-worker");
            (await host.WaitForRunStatusAsync(sourceRunId, "AwaitingApproval")).ShouldBe("AwaitingApproval");

            var (approvalId, requestId) = await PendingApprovalAsync(host);

            await DecideAsync(host, approvalId);

            // The mailbox's worker takes a short lease nobody renews and hangs in the approved tool.
            var resume = await QueuedWorkflowHarness.LeaseAsync(host, "doomed-worker", TimeSpan.FromMilliseconds(400));

            doomed = Task.Run(() => QueuedWorkflowHarness.RunAttemptAsync(host, resume, "doomed-worker"), CancellationToken.None);

            await Task.WhenAny(started.Task, doomed).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            started.Task.IsCompleted.ShouldBeTrue("the mailbox's run did not reach the approved tool");

            // The lease runs out; the next worker finds the job past its one attempt and fails it without running it.
            var finished = await QueuedWorkflowHarness.FinishWithWorkerAsync(host, resume.Id);

            finished.Status.ShouldBe(JobStatus.Failed);
            finished.ErrorMessage.ShouldNotBeNull().ShouldStartWith("tracon.job.attempts-exhausted");

            using var again = await AnswerOverHttpAsync(host, requestId);

            again.StatusCode.ShouldBe(HttpStatusCode.Conflict, await again.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            calls.ShouldBe(1, "the approval was applied twice: the tool ran in the worker that died and again for the HTTP answer");
            (await PendingRequestIdsAsync(host)).ShouldBeEmpty("the answer was applied: the console must not offer the same request again");
        }
        finally
        {
            await teardown.CancelAsync();

            if (doomed is not null)
            {
                try
                {
                    await doomed.WaitAsync(TimeSpan.FromSeconds(30));
                }
                catch (Exception exception) when (exception is OperationCanceledException or TraconException or TimeoutException)
                {
                    // A hung attempt ends by cancellation; what it does then is no longer under test.
                }
            }
        }
    }

    [Fact]
    public async Task A_mailbox_whose_answer_mark_cannot_be_saved_fails_before_the_tool_runs_and_closes_its_run()
    {
        var calls = 0;

        // The first turn opens the session (TryCreateAsync); only the resume writes it again (TryUpdateAsync).
        await using var host = await StartAsync(
            (orderId, _) =>
            {
                Interlocked.Increment(ref calls);

                return Task.FromResult($"{orderId} canceled.");
            },
            leaseDuration: TimeSpan.FromMilliseconds(900),
            inject: static services => services.ThrowFrom<ISessionStore>(
                nameof(ISessionStore.TryUpdateAsync),
                static () => new TimeoutException("Injected: the session store did not answer in time.")));

        var sourceRunId = await QueueAsync(host);

        await host.WaitForRunStatusAsync(sourceRunId, "AwaitingApproval");

        var (approvalId, _) = await PendingApprovalAsync(host);

        await DecideAsync(host, approvalId);

        var resumedRunId = await ResumedRunIdAsync(host, sourceRunId);
        var job = await QueuedWorkflowHarness.WaitForJobAsync(host, resumedRunId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(SessionSaveFailed);
        calls.ShouldBe(0, "an answer that could not be marked ran the tool: the answer could be applied a second time");
        (await host.WaitForTerminalRunAsync(resumedRunId)).Status.ShouldBe(RunStatus.Failed);
    }

    private static Task<TraconTestHost> StartAsync(
        Func<string, CancellationToken, Task<string>> tool,
        bool runWorker = true,
        TimeSpan? leaseDuration = null,
        Action<IServiceCollection>? inject = null)
        => TraconTestHost.StartAsync(builder =>
        {
            builder
                .AddModelProvider(new FakeModelProvider("mark-model")
                    .CallsTool("cancel_order", new { orderId = "ORD-7" })
                    .EchoesLastToolResult())
                .AddTool(
                    tool,
                    name: "cancel_order",
                    description: "Cancels an order.",
                    configure: static options => options.RequiresApproval = true)
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Give a short answer.",
                    Model = new ModelBinding { Provider = "mark-model", Model = "mark-1" },
                    ToolNames = ["cancel_order"],
                });

            inject?.Invoke(builder.Services);
            builder.UseScheduling(options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(20);
                options.RunWorker = runWorker;

                if (leaseDuration is { } lease)
                {
                    options.LeaseDuration = lease;
                }
            });
        });

    /// <summary>Queues the first turn, which ends at the approval.</summary>
    private static async Task<Guid> QueueAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "cancel the order", SessionId = SessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<(Guid ApprovalId, string RequestId)> PendingApprovalAsync(TraconTestHost host)
    {
        using var pending = await host.Client.GetAsync(PendingApprovals, TestContext.Current.CancellationToken);
        var approval = (await TraconTestHost.ReadJsonAsync(pending)).EnumerateArray().ShouldHaveSingleItem();

        return (approval.GetProperty("id").GetGuid(), approval.GetProperty("requestId").GetString()!);
    }

    private static async Task DecideAsync(TraconTestHost host, Guid approvalId)
    {
        using var decided = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true },
            TestContext.Current.CancellationToken);

        decided.StatusCode.ShouldBe(HttpStatusCode.OK, await decided.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>The run the decision queued: the one run of the session that is not the source.</summary>
    private static async Task<Guid> ResumedRunIdAsync(TraconTestHost host, Guid sourceRunId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        var resumed = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(
                    new RunQuery { SessionId = SessionId, OnlyRootRuns = false },
                    TestContext.Current.CancellationToken))
                .FirstOrDefault(record => record.Id != sourceRunId),
            static record => record is not null,
            "the resumed run to be created");

        return resumed!.Id;
    }

    /// <summary>Answers the request over the management API, on the buffered branch that can answer with a status code.</summary>
    private static async Task<HttpResponseMessage> AnswerOverHttpAsync(TraconTestHost host, string requestId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest
            {
                SessionId = SessionId,
                Approvals = [new ToolApprovalDecision { RequestId = requestId, Approved = true }],
            }),
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>The requests of the session that still wait for an answer, as <c>GET /api/sessions/{id}</c> reports them.</summary>
    private static async Task<string[]> PendingRequestIdsAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/sessions/{SessionId}", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = await TraconTestHost.ReadJsonAsync(response);

        detail.TryGetProperty("pendingRequestIds", out var pending).ShouldBeTrue("the session detail names the requests that wait for an answer");

        return [.. pending.EnumerateArray().Select(static id => id.GetString()!)];
    }
}
