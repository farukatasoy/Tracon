using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-318: the workflow run, resume and respond endpoints queue the operation with
/// <c>Prefer: respond-async</c> and answer <c>202</c>; the run executes in the worker
/// and no connection holds it.
/// </summary>
/// <remarks>
/// Before this, the three endpoints only streamed (K-845): a client that stopped
/// reading cancelled the run, and a host that had to render for minutes kept a
/// connection open for the whole run.
/// </remarks>
public sealed class WorkflowAsyncRunTests
{
    [Fact]
    public async Task A_queued_run_answers_202_and_completes_in_the_worker()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });

        var runId = await AcceptedRunIdAsync(accepted);
        var body = System.Text.Json.JsonDocument.Parse(await accepted.Content.ReadAsStringAsync()).RootElement;

        body.GetProperty("jobId").GetGuid().ShouldBe(runId, "the job id is the run id, so the run is cancelled from the queue");
        body.GetProperty("location").GetString().ShouldBe($"/tracon/api/runs/{runId}");
        body.GetProperty("eventsLocation").GetString().ShouldBe($"/tracon/api/runs/{runId}/events");
        accepted.Headers.Location!.ToString().ShouldBe($"/tracon/api/runs/{runId}");
        accepted.Headers.GetValues("Preference-Applied").ShouldHaveSingleItem().ShouldBe("respond-async");

        await host.WaitForRunStatusAsync(runId, nameof(RunStatus.Completed));

        probe.FirstCalls.ShouldBe(1);
        probe.SecondCalls.ShouldBe(1);

        (await WaitForJobAsync(host, runId)).Status.ShouldBe(JobStatus.Completed);
    }

    /// <summary>
    /// Failure mode 3: the queued row names the workflow, its session and the caller
    /// before the worker starts, and keeps them after the worker's own write.
    /// </summary>
    [Fact]
    public async Task The_queued_row_names_the_workflow_session_and_caller_and_keeps_them_when_the_run_starts()
    {
        var probe = new RecoveryProbe();
        var attribution = new SettableAttribution { UserId = "ada", Labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["team"] = "audio" } };

        await using var host = await StartAsync(
            probe,
            runWorker: false,
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IRunAttributionContext>(attribution)));
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(
            host,
            $"/api/workflows/{Recoverable}/run",
            new WorkflowRunHttpRequest { Message = "episode 7", SessionId = "production-7" });
        var runId = await AcceptedRunIdAsync(accepted);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var queued = (await runs.GetRunAsync(runId)).ShouldNotBeNull();

        queued.Status.ShouldBe(RunStatus.Queued);
        queued.Kind.ShouldBe(RunKind.Workflow);
        queued.WorkflowName.ShouldBe(Recoverable);
        queued.AgentName.ShouldBe(Recoverable);
        queued.SessionId.ShouldBe("production-7");
        queued.UserId.ShouldBe("ada");
        queued.Labels.ShouldNotBeNull()["team"].ShouldBe("audio");

        // The worker sees no request: whoever reads the context there gets nothing.
        attribution.UserId = null;
        attribution.Labels = null;

        var job = await LeaseAsync(host, "worker", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, job, "worker");

        var finished = (await runs.GetRunAsync(runId)).ShouldNotBeNull();

        finished.Status.ShouldBe(RunStatus.Completed, finished.Error?.Message);
        finished.SessionId.ShouldBe("production-7");
        finished.UserId.ShouldBe("ada");
        finished.Labels.ShouldNotBeNull()["team"].ShouldBe("audio");
    }

    /// <summary>A run queued without a session gets one at once, so its row and any continuation share it.</summary>
    [Fact]
    public async Task A_queued_run_without_a_session_gets_one_before_the_202()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))
            .ShouldNotBeNull().SessionId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_queued_run_with_a_malformed_session_id_is_refused_with_400_and_queues_nothing()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var refused = await PostAsync(
            host,
            $"/api/workflows/{Recoverable}/run",
            new WorkflowRunHttpRequest { Message = "episode 7", SessionId = new string('s', WorkflowSessionId.MaxLength + 1) });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await JobsAsync(host)).ShouldBeEmpty();
        (await WorkflowRunsAsync(host)).ShouldBeEmpty();
    }

    [Fact]
    public async Task With_queuing_turned_off_the_preference_is_answered_with_501()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(
            probe,
            runWorker: false,
            configureServices: static services => services.Configure<TraconAsyncRunOptions>(static options => options.Enabled = false));
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var refused = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });

        refused.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
        (await JobsAsync(host)).ShouldBeEmpty();
        (await WorkflowRunsAsync(host)).ShouldBeEmpty();
    }

    /// <summary>
    /// Failure mode 11: the queue refuses the job. The row written before it must not
    /// stay Queued: no job would ever move it, and reconciliation leaves queued rows alone.
    /// </summary>
    [Fact]
    public async Task A_queue_that_refuses_the_job_does_not_leave_the_row_queued()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(
            probe,
            runWorker: false,
            configureServices: static services => services.AddSingleton<IJobStore>(
                static _ => new RefusingJobStore(new InMemoryJobStore())));
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        try
        {
            using var refused = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });

            refused.StatusCode.ShouldNotBe(HttpStatusCode.Accepted);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("the queue is unavailable", StringComparison.Ordinal))
        {
            // TestServer hands the server's unhandled exception to the client; a real
            // server answers 500. Either way the caller gets no 202.
        }

        var run = (await WorkflowRunsAsync(host)).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        probe.FirstCalls.ShouldBe(0);
    }

    /// <summary>A queued resume runs in the worker and opens a new run from the checkpoint.</summary>
    [Fact]
    public async Task A_queued_resume_answers_202_and_resumes_in_the_worker()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var first = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var sourceId = await AcceptedRunIdAsync(first);
        await host.WaitForRunStatusAsync(sourceId, nameof(RunStatus.Completed));

        using var resumed = await PostAsync(host, $"/api/workflows/runs/{sourceId}/resume", new WorkflowResumeHttpRequest());
        var resumedId = await AcceptedRunIdAsync(resumed);

        resumedId.ShouldNotBe(sourceId);
        await host.WaitForRunStatusAsync(resumedId, nameof(RunStatus.Completed));

        var row = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(resumedId)).ShouldNotBeNull();
        var source = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(sourceId)).ShouldNotBeNull();

        row.SessionId.ShouldBe(source.SessionId);
        row.WorkflowName.ShouldBe(Recoverable);
        (await WaitForJobAsync(host, resumedId)).Status.ShouldBe(JobStatus.Completed);
    }

    [Fact]
    public async Task A_queued_resume_of_an_unknown_run_is_404_and_of_a_run_without_checkpoints_is_400()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(
            probe,
            runWorker: false,
            configureTracon: static builder => builder.Services.Configure<TraconWorkflowOptions>(static options =>
            {
                options.ResumeInterruptedRuns = false;
                options.EnableCheckpointing = false;
            }));
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using (var unknown = await PostAsync(host, $"/api/workflows/runs/{Guid.NewGuid()}/resume", new WorkflowResumeHttpRequest()))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // A run that wrote no checkpoint (checkpointing off), run to completion in the stream.
        using var streamed = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" }, preferAsync: false);
        var frames = await SseReader.ReadAllAsync(await streamed.Content.ReadAsStreamAsync());
        var sourceId = System.Text.Json.JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();

        using var refused = await PostAsync(host, $"/api/workflows/runs/{sourceId}/resume", new WorkflowResumeHttpRequest());

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await JobsAsync(host)).ShouldBeEmpty();
    }

    /// <summary>
    /// 4-A: a queued run's job gets one lease for the run, one per continuation and a
    /// last one that closes the chain while recovery is on; otherwise the limit a
    /// queued agent run gets.
    /// </summary>
    [Theory]
    [InlineData(true, 2, 4)]
    [InlineData(true, 0, 2)]
    [InlineData(false, 2, 1)]
    public async Task The_jobs_attempt_limit_follows_the_recovery_setting(bool recovery, int maxResumeAttempts, int expected)
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, resumeInterruptedRuns: recovery, maxResumeAttempts: maxResumeAttempts, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        var job = (await host.Services.GetRequiredService<IJobStore>().GetAsync(TenantOf(host), runId)).ShouldNotBeNull();

        job.HandlerKey.ShouldBe(JobHandlerKeys.WorkflowRun);
        job.TargetName.ShouldBe(Recoverable);
        job.MaxAttempts.ShouldBe(expected);
    }

    /// <summary>A queue whose enqueue fails for the queued workflow run key.</summary>
    private sealed class RefusingJobStore(IJobStore inner) : IJobStore
    {
        public ValueTask<JobRecord> EnqueueAsync(JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
            => string.Equals(job.HandlerKey, JobHandlerKeys.WorkflowRun, StringComparison.Ordinal)
                ? throw new InvalidOperationException("the queue is unavailable")
                : inner.EnqueueAsync(job, items, cancellationToken);

        public ValueTask<JobRecord?> LeaseAsync(string owner, TimeSpan leaseDuration, IReadOnlyList<string>? lanes, CancellationToken cancellationToken = default)
            => inner.LeaseAsync(owner, leaseDuration, lanes, cancellationToken);

        public ValueTask RenewLeaseAsync(Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
            => inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

        public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
            => inner.MarkRunningAsync(jobId, owner, cancellationToken);

        public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
            => inner.CompleteAsync(completion, cancellationToken);

        public ValueTask ReleaseForRetryAsync(Guid jobId, string errorMessage, TimeSpan? retryAfter = null, CancellationToken cancellationToken = default)
            => inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);

        public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => inner.CancelAsync(tenantId, jobId, cancellationToken);

        public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, jobId, cancellationToken);

        public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
            => inner.QueryAsync(query, cancellationToken);

        public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
            => inner.ListItemsAsync(jobId, cancellationToken);

        public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
            => inner.ReportItemAsync(item, cancellationToken);

        public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
            => inner.GetQueueDepthAsync(cancellationToken);
    }
}
