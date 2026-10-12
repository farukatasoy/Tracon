using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// K-269: Tracon.Testing.TraconTestHost and this project's own host share a
// name, so the package is imported one type at a time.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The session save ends a queued turn (F-133), but a store error on it after
/// the model has answered is not a failure of the turn (F-367).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The handlers that end a queued turn saved the session inside the
/// handler's own failure path. One store error after the model had answered
/// failed the job while its run said <c>Completed</c> (one attempt), or
/// released the job and called the model a second time (more attempts).
/// </para>
/// <para>
/// The save is now tried again inside the lease window. Past it, the job fails
/// with <c>tracon.job.session-save-failed</c> and the handler does not run
/// again. The cases run through the real host: the failure crosses the store,
/// the handler and the worker.
/// </para>
/// </remarks>
public sealed class QueuedTurnSessionSaveTests
{
    private const string AgentName = "save-agent";
    private const string SessionId = "save-session";
    private const string SessionSaveFailed = "tracon.job.session-save-failed";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);
    private static readonly Uri PendingApprovals = new("/tracon/api/approvals/pending", UriKind.Relative);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_session_save_that_fails_once_neither_fails_the_queued_run_nor_calls_the_model_again(int maxAttempts)
    {
        var provider = new FakeModelProvider("save-model").RespondsWith("done");

        await using var host = await StartAsync(provider, nameof(ISessionStore.TryCreateAsync), failures: 1, maxAttempts);

        var runId = await QueueAsync(host);
        var job = await WaitForTerminalJobAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
        provider.Requests.Count.ShouldBe(1, "the model answered once; the failed save ran the turn again");
        job.Attempt.ShouldBe(1);
        (await RunAsync(host, runId)).Status.ShouldBe(RunStatus.Completed);
        (await host.Services.GetRequiredService<ISessionStore>().GetAsync(SessionId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_session_store_that_keeps_failing_fails_the_job_with_a_code_and_never_calls_the_model_again()
    {
        var provider = new FakeModelProvider("save-model").RespondsWith("done");

        // A short lease keeps the window short: the save is tried again until
        // the lease the worker can prove runs out, not for five minutes.
        await using var host = await StartAsync(
            provider,
            nameof(ISessionStore.TryCreateAsync),
            failures: int.MaxValue,
            maxAttempts: 3,
            leaseDuration: TimeSpan.FromMilliseconds(900));

        var runId = await QueueAsync(host);
        var job = await WaitForTerminalJobAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Failed);
        provider.Requests.Count.ShouldBe(1, "a turn whose session could not be saved was run again");
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(SessionSaveFailed);
        job.Attempt.ShouldBe(1);
        (await RunAsync(host, runId)).Status.ShouldBe(RunStatus.Completed);

        // One failure, one error entry: the reference the job carries names it.
        var reference = job.ErrorMessage[(job.ErrorMessage.IndexOf("(ref: ", StringComparison.Ordinal) + 6)..^1];

        host.Logs.Entries
            .Count(entry => entry.StartsWith("Error ", StringComparison.Ordinal) &&
                            entry.Contains($"(ref: {reference})", StringComparison.Ordinal))
            .ShouldBe(1);
    }

    /// <summary>
    /// The save that precedes a pending approval runs inside
    /// <c>BeforePendingApprovalIsPublished</c>; a store error there closed the
    /// run as <c>Failed</c> before anyone could answer the approval.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_session_save_that_fails_once_before_an_approval_is_listed_is_tried_again(int maxAttempts)
    {
        var provider = ApprovalProvider();

        await using var host = await StartAsync(
            provider, nameof(ISessionStore.TryCreateAsync), failures: 1, maxAttempts, approvalTool: true);

        var runId = await QueueAsync(host);
        var job = await WaitForTerminalJobAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
        (await RunAsync(host, runId)).Status.ShouldBe(RunStatus.AwaitingApproval);
        provider.Requests.Count.ShouldBe(1);

        using var pending = await host.Client.GetAsync(PendingApprovals);
        (await TraconTestHost.ReadJsonAsync(pending))
            .EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("runId").GetGuid().ShouldBe(runId);
    }

    /// <summary>
    /// The approval-resume job runs with one attempt: a store error on its
    /// save failed the job while the resumed run had completed.
    /// </summary>
    [Fact]
    public async Task A_session_save_that_fails_once_does_not_fail_an_approval_resume()
    {
        var provider = ApprovalProvider();

        // The first turn opens the session (TryCreateAsync); the resume writes
        // it again (TryUpdateAsync), so only the resume's save fails.
        await using var host = await StartAsync(
            provider, nameof(ISessionStore.TryUpdateAsync), failures: 1, maxAttempts: 1, approvalTool: true);

        var sourceRunId = await QueueAsync(host);
        await host.WaitForRunStatusAsync(sourceRunId, "AwaitingApproval");

        using (var pending = await host.Client.GetAsync(PendingApprovals))
        {
            var approvalId = (await TraconTestHost.ReadJsonAsync(pending))
                .EnumerateArray().ShouldHaveSingleItem().GetProperty("id").GetGuid();

            using var decided = await host.Client.PostAsJsonAsync(
                new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
                new ApprovalDecisionRequest { Approved = true });

            decided.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();

        var resumed = await WaitUntil.ValueAsync(
            async () => (await runs.QueryRunsAsync(new RunQuery { SessionId = SessionId, OnlyRootRuns = false }))
                .FirstOrDefault(record => record.Id != sourceRunId),
            static record => record is not null,
            "the resumed run to be created");

        var job = await WaitForTerminalJobAsync(host, resumed!.Id);

        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
        (await RunAsync(host, resumed.Id)).Status.ShouldBe(RunStatus.Completed);
        provider.Requests.Count.ShouldBe(2);
    }

    private static FakeModelProvider ApprovalProvider()
        => new FakeModelProvider("save-model")
            .CallsTool("cancel_order", new { orderId = "ORD-7" })
            .EchoesLastToolResult();

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        string failingMember,
        int failures,
        int maxAttempts,
        TimeSpan? leaseDuration = null,
        bool approvalTool = false)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(provider)
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "save-model", Model = "save-1" },
                        ToolNames = approvalTool ? ["cancel_order"] : [],
                    });

                if (approvalTool)
                {
                    builder.AddTool(
                        (Func<string, string>)(orderId => $"{orderId} canceled."),
                        name: "cancel_order",
                        description: "Cancels an order.",
                        configure: static options => options.RequiresApproval = true);
                }

                // Called through configureTracon, so Tracon's own session store
                // is already registered and the proxy wraps the real one.
                builder.Services.ThrowFrom<ISessionStore>(
                    failingMember,
                    static () => new TimeoutException("Injected: the session store did not answer in time."),
                    failures);
            },
            configureServices: services =>
            {
                services.UseScheduling(options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(20);

                    if (leaseDuration is { } lease)
                    {
                        options.LeaseDuration = lease;
                    }
                });

                services.Configure<TraconAsyncRunOptions>(options => options.MaxAttempts = maxAttempts);
            });

    private static async Task<Guid> QueueAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "cancel the order", SessionId = SessionId }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private static async Task<RunRecord> RunAsync(TraconTestHost host, Guid runId)
        => (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull();

    /// <summary>A queued run's job shares the run's id (AgentEndpoints.RunQueuedAsync).</summary>
    private static async Task<JobRecord> WaitForTerminalJobAsync(TraconTestHost host, Guid jobId)
    {
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenantId = host.Services.GetRequiredService<ITenantContext>().TenantId;

        var job = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(tenantId, jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled,
            $"job {jobId} to finish");

        return job!;
    }
}
