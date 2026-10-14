using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A workflow that takes one answer per request (phase 208): two identical answers start one
/// run, a different answer is refused with 409, and an answer whose run stopped before it
/// finished can be given again. <see cref="WorkflowResponseMode.Branch"/> keeps opening a new
/// run for every answer.
/// </summary>
/// <remarks>
/// The races are real requests against the real endpoint, sent together with
/// <see cref="Task.WhenAll(Task[])"/>: the decision is made by the answer store's claim, and only
/// the HTTP boundary shows whether the endpoint asks it before anything starts.
/// </remarks>
public sealed class WorkflowSingleResponseTests
{
    [Fact]
    public async Task Two_identical_streamed_answers_at_once_start_one_run_and_the_second_follows_it()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var before = (await WorkflowRunsAsync(host)).Count;

        var responses = await Task.WhenAll(
            RespondAsync(host, sourceId, new { requestId, approved = true }),
            RespondAsync(host, sourceId, new { requestId, approved = true }));

        var first = await FramesAsync(responses[0]);
        var second = await FramesAsync(responses[1]);

        // One run, announced by both streams; the follower reads it to its end.
        RunIdOf(first).ShouldBe(RunIdOf(second));
        (await WorkflowRunsAsync(host)).Count.ShouldBe(before + 1);
        second[^1].Event.ShouldBe("done");
        second.ShouldContain(static frame => frame.Event == "event" && frame.Data.Contains("\"WorkflowOutput\"", StringComparison.Ordinal));

        (await host.WaitForTerminalRunAsync(RunIdOf(first))).Status.ShouldBe(RunStatus.Completed);
        (await RecordsAsync(host, sourceId)).ShouldHaveSingleItem().ResponseRunId.ShouldBe(RunIdOf(first));

        await AuditNamesOnlyTheStartedRunAsync(host, RunIdOf(first));

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Two_identical_queued_answers_with_different_keys_get_the_same_202_and_queue_one_job()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false);
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        var responses = await Task.WhenAll(
            QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true }, idempotencyKey: "first-click"),
            QueuedWorkflowHarness.PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true }, idempotencyKey: "second-click"));

        var bodies = new List<JsonElement>();

        foreach (var response in responses)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            bodies.Add(await TraconTestHost.ReadJsonAsync(response));
        }

        bodies[1].GetProperty("runId").GetGuid().ShouldBe(bodies[0].GetProperty("runId").GetGuid());
        bodies[1].GetProperty("jobId").GetGuid().ShouldBe(bodies[0].GetProperty("jobId").GetGuid());
        responses[1].Headers.Location.ShouldBe(responses[0].Headers.Location);
        (await QueuedWorkflowHarness.JobsAsync(host)).ShouldHaveSingleItem();

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Two_different_answers_at_once_start_one_run_and_the_other_gets_409_that_names_neither()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);
        var before = (await WorkflowRunsAsync(host)).Count;

        var responses = await Task.WhenAll(
            RespondAsync(host, sourceId, new { requestId, approved = true }),
            RespondAsync(host, sourceId, new { requestId, approved = false }));

        var started = responses.Single(static response => response.StatusCode == HttpStatusCode.OK);
        var refused = responses.Single(static response => response.StatusCode != HttpStatusCode.OK);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var runId = RunIdOf(await FramesAsync(started));
        var problemText = await refused.Content.ReadAsStringAsync();
        var problem = JsonDocument.Parse(problemText).RootElement;

        problem.GetProperty("title").GetString().ShouldBe("Request already answered");

        // The run the other answer started is not named, and neither is that answer.
        problemText.ShouldNotContain(runId.ToString(), Case.Insensitive);
        problemText.ShouldNotContain("approved", Case.Insensitive);
        problemText.ShouldNotContain("true", Case.Insensitive);
        problemText.ShouldNotContain("false", Case.Insensitive);

        (await WorkflowRunsAsync(host)).Count.ShouldBe(before + 1);
        await AuditNamesOnlyTheStartedRunAsync(host, runId);

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Branch_mode_still_opens_a_branch_for_every_answer()
    {
        // K-130 is the default and stays byte for byte: every answer opens its own run. The only
        // new thing is that each one is recorded and audited.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, BranchFlow);

        using var yes = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var yesRun = RunIdOf(await FramesAsync(yes));

        using var no = await RespondAsync(host, sourceId, new { requestId, approved = false });
        var noRun = RunIdOf(await FramesAsync(no));

        noRun.ShouldNotBe(yesRun);
        (await host.WaitForTerminalRunAsync(yesRun)).Status.ShouldBe(RunStatus.Completed);
        (await host.WaitForTerminalRunAsync(noRun)).Status.ShouldBe(RunStatus.Completed);

        var records = await RecordsAsync(host, sourceId);

        records.Select(static record => record.ResponseRunId).ShouldBe([yesRun, noRun], ignoreOrder: true);
        records.ShouldAllBe(static record => record.ClaimScope == null);

        var entries = await RespondEntriesAsync(host);

        entries.Count.ShouldBe(2);
        entries.ShouldAllBe(static entry => entry.After!.Contains("\"mode\":\"Branch\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_answer_whose_queued_run_was_cancelled_before_it_started_starts_the_work_again()
    {
        // The first answer was accepted and its run never reached the workflow: the job was
        // cancelled while it waited in the queue. The same answer must not be locked out.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false);
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var queued = await QueuedWorkflowHarness.PostAsync(
            host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var abandonedRun = await QueuedWorkflowHarness.AcceptedRunIdAsync(queued);

        using var cancelled = await host.Client.PostAsync(new Uri($"/tracon/api/runs/{abandonedRun}/cancel", UriKind.Relative), null);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancelled.Content.ReadAsStringAsync());

        using var again = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var frames = await FramesAsync(again);
        var restarted = RunIdOf(frames);

        restarted.ShouldNotBe(abandonedRun);
        frames.ShouldContain(static frame => frame.Data.Contains("\"approved\"", StringComparison.Ordinal) && frame.Data.Contains("WorkflowOutput", StringComparison.Ordinal));
        (await host.WaitForTerminalRunAsync(restarted)).Status.ShouldBe(RunStatus.Completed);

        var records = await RecordsAsync(host, sourceId);

        records.Single(record => record.ResponseRunId == abandonedRun).Status.ShouldBe(WorkflowResponseStatus.Abandoned);
        records.Single(record => record.ResponseRunId == restarted).Status.ShouldBe(WorkflowResponseStatus.Delivered);

        var entries = await RespondEntriesAsync(host);

        entries.Count.ShouldBe(2);
        entries.ShouldContain(entry => entry.After!.Contains("\"redrive\":true", StringComparison.Ordinal) &&
                                       entry.After.Contains(restarted.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_follower_that_disconnects_does_not_cancel_the_run_it_follows()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, SlowFlow);

        // The first answer's run parks in its last step; its stream stays open.
        var firstTask = RespondAsync(host, sourceId, new { requestId, approved = true });

        await harness.Parked.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // The follower reads the first frame and goes away.
        using (var followerAbort = new CancellationTokenSource())
        {
            using var follower = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: followerAbort.Token);

            follower.StatusCode.ShouldBe(HttpStatusCode.OK, await ProblemTextAsync(follower));

            await using var stream = await follower.Content.ReadAsStreamAsync(followerAbort.Token);
            await using var frames = SseReader.ReadAsync(stream, followerAbort.Token).GetAsyncEnumerator(followerAbort.Token);

            (await frames.MoveNextAsync()).ShouldBeTrue();
            frames.Current.Event.ShouldBe("run");

            await followerAbort.CancelAsync();
        }

        harness.Gate.SetResult();

        using var first = await firstTask;
        var runId = RunIdOf(await FramesAsync(first));

        (await host.WaitForTerminalRunAsync(runId)).Status.ShouldBe(RunStatus.Completed);
    }

    [Fact]
    public async Task A_completed_answer_whose_delivery_was_not_recorded_is_followed_not_started_again()
    {
        // The engine's Delivered write is best-effort. When it is lost the record stays
        // Accepted; the run completed, so the same answer must follow it, not run the
        // side effects again.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: static services =>
            services.AddSingleton<IWorkflowResponseStore>(static provider =>
                new UndeliverableResponseStore(new InMemoryWorkflowResponseStore(provider.GetRequiredService<ITenantContext>()))));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var first = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var runId = RunIdOf(await FramesAsync(first));

        (await host.WaitForTerminalRunAsync(runId)).Status.ShouldBe(RunStatus.Completed);
        (await RecordsAsync(host, sourceId)).ShouldHaveSingleItem().Status.ShouldBe(WorkflowResponseStatus.Accepted);

        var before = (await WorkflowRunsAsync(host)).Count;

        using var again = await RespondAsync(host, sourceId, new { requestId, approved = true });

        RunIdOf(await FramesAsync(again)).ShouldBe(runId);
        (await WorkflowRunsAsync(host)).Count.ShouldBe(before);
    }

    [Fact]
    public async Task An_interrupted_answer_whose_continuation_is_running_is_followed()
    {
        // A queued answer interrupted by a restart continues under a new run (the recovery
        // chain). The same answer must follow the work in flight - the chain's last run - and
        // must not count the interrupted head as a stopped answer.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(runWorker: false);
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var queued = await QueuedWorkflowHarness.PostAsync(
            host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });
        var head = await QueuedWorkflowHarness.AcceptedRunIdAsync(queued);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var tenant = QueuedWorkflowHarness.TenantOf(host);
        var continuation = Guid.NewGuid();

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = head,
            Status = RunStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = new RunError { Type = "interrupted", Message = "The process stopped." },
            TenantId = tenant,
        });

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = continuation,
            AgentName = SingleFlow,
            Kind = RunKind.Workflow,
            WorkflowName = SingleFlow,
            Status = RunStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
            SessionId = (await runs.GetRunAsync(sourceId))!.SessionId,
            ContinuedFromRunId = head,
        });

        var before = (await WorkflowRunsAsync(host)).Count;

        using (var abort = new CancellationTokenSource())
        {
            using var follower = await RespondAsync(host, sourceId, new { requestId, approved = true }, cancellationToken: abort.Token);

            follower.StatusCode.ShouldBe(HttpStatusCode.OK, await ProblemTextAsync(follower));

            await using var stream = await follower.Content.ReadAsStreamAsync(abort.Token);
            await using var frames = SseReader.ReadAsync(stream, abort.Token).GetAsyncEnumerator(abort.Token);

            (await frames.MoveNextAsync()).ShouldBeTrue();
            JsonDocument.Parse(frames.Current.Data).RootElement.GetProperty("runId").GetGuid().ShouldBe(continuation);

            await abort.CancelAsync();
        }

        // The queued follower gets the 202 the first answer got.
        using var queuedAgain = await QueuedWorkflowHarness.PostAsync(
            host, $"/api/workflows/runs/{sourceId}/respond", new { requestId, approved = true });

        (await QueuedWorkflowHarness.AcceptedRunIdAsync(queuedAgain)).ShouldBe(head);
        (await WorkflowRunsAsync(host)).Count.ShouldBe(before);
    }

    [Fact]
    public async Task A_caller_the_run_gate_denies_gets_403_and_never_learns_the_request_was_answered()
    {
        var gate = new SwitchableRunGate();
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: services =>
            services.AddSingleton<IRunAuthorizationHandler>(gate));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var first = await RespondAsync(host, sourceId, new { requestId, approved = true });
        await FramesAsync(first);

        gate.Deny = true;

        using var denied = await RespondAsync(host, sourceId, new { requestId, approved = false });

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var body = await denied.Content.ReadAsStringAsync();

        body.ShouldNotContain("answered", Case.Insensitive);
        (await RespondEntriesAsync(host)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_resumed_source_answered_again_follows_the_first_answer_and_a_different_answer_conflicts()
    {
        // The claim is the session's: a plain resume republishes the request under the same id
        // in a new run, and that run is not a way around the one answer.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var first = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var answered = RunIdOf(await FramesAsync(first));

        using var resume = await host.Client.PostAsync(new Uri($"/tracon/api/workflows/runs/{sourceId}/resume", UriKind.Relative), null);
        var resumedId = RunIdOf(await FramesAsync(resume));

        (await RequestsAsync(host, resumedId)).ShouldHaveSingleItem().ShouldBe(requestId);

        using var same = await RespondAsync(host, resumedId, new { requestId, approved = true });
        RunIdOf(await FramesAsync(same)).ShouldBe(answered);

        using var different = await RespondAsync(host, resumedId, new { requestId, approved = false });
        different.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_code_overload_shows_the_mode_in_the_catalog()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();

        using var listed = await host.Client.GetAsync(new Uri("/tracon/api/workflows", UriKind.Relative));
        var catalog = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ToList();

        catalog.Single(static item => string.Equals(item.GetProperty("name").GetString(), SingleFlow, StringComparison.Ordinal))
            .GetProperty("responseMode").GetString().ShouldBe("SingleAnswer");
        catalog.Single(static item => string.Equals(item.GetProperty("name").GetString(), BranchFlow, StringComparison.Ordinal))
            .GetProperty("responseMode").GetString().ShouldBe("Branch");
    }

    /// <summary>
    /// Every answer that started a run is audited, and only that one names a run that exists.
    /// </summary>
    /// <remarks>
    /// Two answers that both find the request unclaimed both write their fail-closed entry
    /// before they claim, and the loser's entry stays as an answer that was tried - the way a
    /// lost race of two approval decisions leaves its entry. A follower that found the claim
    /// writes nothing. So: one or two entries, exactly one of them naming a run that exists.
    /// </remarks>
    private static async Task AuditNamesOnlyTheStartedRunAsync(TraconTestHost host, Guid startedRun)
    {
        var entries = await RespondEntriesAsync(host);
        var runs = host.Services.GetRequiredService<IRunStore>();

        entries.Count.ShouldBeInRange(1, 2);

        var named = entries
            .Select(static entry => JsonDocument.Parse(entry.After!).RootElement.GetProperty("responseRunId").GetGuid())
            .ToList();

        named.ShouldContain(startedRun);

        foreach (var other in named.Where(run => run != startedRun))
        {
            (await runs.GetRunAsync(other)).ShouldBeNull("an attempted answer's entry names a run that never started");
        }
    }

    /// <summary>An answer store whose delivery write always fails, like a lost database write.</summary>
    private sealed class UndeliverableResponseStore(IWorkflowResponseStore inner) : IWorkflowResponseStore
    {
        public ValueTask<WorkflowResponseClaim> AddAsync(WorkflowResponseRecord record, CancellationToken cancellationToken = default)
            => inner.AddAsync(record, cancellationToken);

        public ValueTask<WorkflowResponseRecord?> FindClaimAsync(string claimScope, string requestId, CancellationToken cancellationToken = default)
            => inner.FindClaimAsync(claimScope, requestId, cancellationToken);

        public ValueTask<bool> ReplaceAsync(Guid abandonedResponseRunId, WorkflowResponseRecord replacement, CancellationToken cancellationToken = default)
            => inner.ReplaceAsync(abandonedResponseRunId, replacement, cancellationToken);

        public ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the answer store is unreachable");

        public ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(Guid sourceRunId, CancellationToken cancellationToken = default)
            => inner.ListBySourceRunAsync(sourceRunId, cancellationToken);

        public ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default)
            => inner.GetAsync(responseRunId, cancellationToken);
    }

    /// <summary>A run gate that allows until told to deny.</summary>
    private sealed class SwitchableRunGate : IRunAuthorizationHandler
    {
        private volatile bool _deny;

        public bool Deny
        {
            get => _deny;
            set => _deny = value;
        }

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(RunAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(_deny ? RunAuthorizationResult.Deny("not yours") : RunAuthorizationResult.Allow());

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => new(RunAuthorizationResult.Allow());
    }
}
