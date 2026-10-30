using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The workflow run, resume and respond endpoints queue the operation with
/// <c>Prefer: respond-async</c> and answer <c>202</c>; the run executes in the worker
/// and no connection holds it.
/// </summary>
/// <remarks>
/// Before this, the three endpoints only streamed: a client that stopped
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
                static _ => new RefusingJobStore(new InMemoryJobStore(), JobHandlerKeys.WorkflowRun)));
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

    /// <summary>
    /// A queued resume keeps the definition version the run recorded, and one that names a version carries it
    /// through the job payload to the worker.
    /// </summary>
    /// <remarks>
    /// The stamp on the resumed row is the observable: it is the version the worker compiled. If the
    /// endpoint did not write <c>definitionVersion</c> into the payload, or the handler did not hand
    /// it to the engine, the row would name version 1 whatever the request said.
    /// </remarks>
    [Fact]
    public async Task A_queued_resume_compiles_the_recorded_version_and_one_that_names_a_version_carries_it_to_the_worker()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var first = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var sourceId = await AcceptedRunIdAsync(first);
        await host.WaitForRunStatusAsync(sourceId, nameof(RunStatus.Completed));

        // The operator edits the workflow while the run could still be resumed (a description is enough: the
        // stamp, not the behavior, is what names the version).
        using (var edited = await host.Client.PutAsJsonAsync(
                   $"/tracon/api/workflows/{Recoverable}",
                   RecoverableDefinition() with { Description = "Edited after the run." }))
        {
            edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runs = host.Services.GetRequiredService<IRunStore>();

        (await runs.GetRunAsync(sourceId)).ShouldNotBeNull().AgentVersion.ShouldBe(1);

        using var recorded = await PostAsync(host, $"/api/workflows/runs/{sourceId}/resume", new WorkflowResumeHttpRequest());
        var recordedId = await AcceptedRunIdAsync(recorded);
        await host.WaitForRunStatusAsync(recordedId, nameof(RunStatus.Completed));

        (await runs.GetRunAsync(recordedId)).ShouldNotBeNull().AgentVersion.ShouldBe(1, "no version was named: the worker resumed the recorded one");

        using var named = await PostAsync(
            host,
            $"/api/workflows/runs/{sourceId}/resume",
            new WorkflowResumeHttpRequest { DefinitionVersion = 2 });
        var namedId = await AcceptedRunIdAsync(named);
        await host.WaitForRunStatusAsync(namedId, nameof(RunStatus.Completed));

        (await runs.GetRunAsync(namedId)).ShouldNotBeNull().AgentVersion.ShouldBe(2, "the version the request named reached the worker through the job");
    }

    [Fact]
    public async Task A_queued_resume_naming_a_version_the_workflow_does_not_have_is_400_and_queues_no_job()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var first = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var sourceId = await AcceptedRunIdAsync(first);
        await host.WaitForRunStatusAsync(sourceId, nameof(RunStatus.Completed));

        var jobsBefore = (await JobsAsync(host)).Count;

        using var refused = await PostAsync(
            host,
            $"/api/workflows/runs/{sourceId}/resume",
            new WorkflowResumeHttpRequest { DefinitionVersion = 9 });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await refused.Content.ReadAsStringAsync());
        (await JobsAsync(host)).Count.ShouldBe(jobsBefore, "the refusal came before a job was queued");
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
}
