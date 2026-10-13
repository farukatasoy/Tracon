using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-321: a workflow run a queued job was running when its worker stopped is
/// continued from its latest checkpoint when the job is leased again.
/// </summary>
/// <remarks>
/// <para>
/// Before this, a workflow job leased again ran its item from the beginning: every
/// step finished before the crash ran again (a second model call, a second render),
/// and the run the crash left behind stayed <see cref="RunStatus.Running"/>. The job
/// owns the run now; reconciliation still never resumes a workflow (K-909).
/// </para>
/// <para>
/// The crash is played in-process (<see cref="QueuedWorkflowHarness.StartDoomedAttemptAsync"/>):
/// a worker leases the job, its step hangs and ignores the run's token, and its lease is
/// not renewed. The cases run through the container-built handler and the real stores,
/// because the boundary is the store (the marker, the chain, the checkpoint) and the DI
/// factory that hands the handler its setting. The process-level proof with
/// <c>SIGKILL</c> is <c>TwoProcessWorkflowResumeTests</c>.
/// </para>
/// </remarks>
public sealed class WorkflowRecoveryTests
{
    private static readonly TimeSpan DeadLease = TimeSpan.FromMilliseconds(300);

    /// <summary>Failure mode 14: a step that finished before the crash is not called again.</summary>
    [Fact]
    public async Task A_queued_run_interrupted_in_its_second_step_is_continued_without_running_the_first_again()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var headId = await AcceptedRunIdAsync(accepted);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        job.Attempt.ShouldBe(2);

        await RunAttemptAsync(host, job, "survivor");

        probe.FirstCalls.ShouldBe(1, "the first step finished before the crash; its checkpoint covers it");
        probe.SecondCalls.ShouldBe(2, "the step that was running when the worker died runs again");

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(2);

        var head = runs.Single(run => run.Id == headId);
        var continuation = runs.Single(run => run.Id != headId);

        head.Status.ShouldBe(RunStatus.Failed);
        head.Error.ShouldNotBeNull().Type.ShouldBe("interrupted");
        head.Error.Message.ShouldContain(continuation.Id.ToString(), Case.Sensitive);

        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);
        continuation.ContinuedFromRunId.ShouldBe(headId);
        continuation.SessionId.ShouldBe(head.SessionId);
        continuation.WorkflowName.ShouldBe(Recoverable);

        // The chain is walked forward over HTTP too.
        using var forward = await host.Client.GetAsync($"/tracon/api/runs?continuedFromRunId={headId}");
        (await TraconTestHost.ReadJsonAsync(forward)).EnumerateArray()
            .Select(static run => run.GetProperty("id").GetGuid())
            .ShouldBe([continuation.Id]);
    }

    /// <summary>
    /// Failure mode 20: the continuation runs under the values the run recorded. A
    /// continuation on the defaults would change its steps' instructions halfway.
    /// </summary>
    [Fact]
    public async Task A_continuation_runs_under_the_recorded_parameter_values_not_the_default()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        await EnqueueBatchAsync(host, """{"message":"episode 7","parameters":{"tone":"formal"}}""");

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, job, "survivor");

        probe.Tones.ShouldBe(["formal", "formal"]);

        // A continuation from the checkpoint, not the item run again: its values
        // came from what the interrupted run recorded, and it recorded them in turn.
        probe.FirstCalls.ShouldBe(1);

        var item = (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(job.Id)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Completed, item.Error);

        var continuation = (await WorkflowRunsAsync(host)).Single(static run => run.ContinuedFromRunId is not null);
        var recorded = await host.Services.GetRequiredService<IRunInputStore>().GetAsync(TenantOf(host), continuation.Id);

        recorded.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("tone", "formal");
    }

    /// <summary>
    /// The batch path: the run id is on the item before the run starts, and the item
    /// is reported with the run that finished it. The item's session id is the
    /// session of every run in the chain (197.3).
    /// </summary>
    [Fact]
    public async Task A_batch_item_carries_its_run_while_pending_and_reports_the_continuation_that_finished_it()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var jobId = await EnqueueBatchAsync(host, """{"message":"episode 7","sessionId":"production-7"}""");
        var store = host.Services.GetRequiredService<IJobStore>();

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        // The container built the handler with the setting on: the marker is there.
        var marked = (await store.ListItemsAsync(jobId)).ShouldHaveSingleItem();
        marked.Status.ShouldBe(JobItemStatus.Pending);
        marked.RunId.ShouldNotBeNull();

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, job, "survivor");

        var runs = await WorkflowRunsAsync(host);
        var continuation = runs.Single(run => run.ContinuedFromRunId == marked.RunId);

        var item = (await store.ListItemsAsync(jobId)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Completed, item.Error);
        item.RunId.ShouldBe(continuation.Id);

        runs.ShouldAllBe(static run => run.SessionId == "production-7");
        probe.FirstCalls.ShouldBe(1);
    }

    /// <summary>
    /// Failure mode 13: a run that finished before its job recorded it is not run again
    /// when the job is leased again.
    /// </summary>
    [Fact]
    public async Task A_run_that_finished_before_its_item_was_reported_is_not_run_again()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var jobId = await EnqueueBatchAsync(host, "episode 7");
        var store = host.Services.GetRequiredService<IJobStore>();

        // The first attempt does all the work, then dies on the write that records it.
        var first = await LeaseAsync(host, "doomed-worker", DeadLease);

        await Should.ThrowAsync<InvalidOperationException>(() => RunAttemptAsync(
            host,
            first,
            "doomed-worker",
            report: (result, ct) => result.Status == JobItemStatus.Pending
                ? store.ReportItemAsync(result, ct)
                : throw new InvalidOperationException("the process died before the report landed")));

        var second = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, second, "survivor");

        probe.FirstCalls.ShouldBe(1);
        probe.SecondCalls.ShouldBe(1);

        var run = (await WorkflowRunsAsync(host)).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);

        var item = (await store.ListItemsAsync(jobId)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Completed);
        item.RunId.ShouldBe(run.Id);
    }

    /// <summary>
    /// Failure mode 15: past <c>MaxResumeAttempts</c> continuations the item fails, the
    /// last run is closed, and no further run opens.
    /// </summary>
    [Fact]
    public async Task The_chain_stops_at_the_limit_and_the_item_fails_naming_it()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 1);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var jobId = await EnqueueBatchAsync(host, "episode 7");

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        // The continuation dies in the same step.
        await StartDoomedAttemptAsync(host, probe, DeadLease, waitForCheckpoint: false);

        var third = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, third, "survivor");

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(2, "one continuation is the limit");
        runs.ShouldAllBe(static run => run.Status == RunStatus.Failed);
        runs[^1].Error.ShouldNotBeNull().Message.ShouldContain("MaxResumeAttempts", Case.Sensitive);

        var item = (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(jobId)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Failed);
        item.Error.ShouldNotBeNull().ShouldContain("MaxResumeAttempts", Case.Sensitive);

        probe.SecondCalls.ShouldBe(2);
    }

    /// <summary>
    /// Failure mode 15: a continuation that died before it wrote a checkpoint of its own
    /// is continued from the checkpoint of the run before it, not from the beginning.
    /// </summary>
    [Fact]
    public async Task A_continuation_with_no_checkpoint_of_its_own_resumes_from_the_one_before_it()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 2);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        await EnqueueBatchAsync(host, "episode 7");

        await StartDoomedAttemptAsync(host, probe, DeadLease);
        await StartDoomedAttemptAsync(host, probe, DeadLease, waitForCheckpoint: false);

        var third = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, third, "survivor");

        probe.FirstCalls.ShouldBe(1, "every continuation resumed from the first step's checkpoint");
        probe.SecondCalls.ShouldBe(3);

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(3);
        runs[1].ContinuedFromRunId.ShouldBe(runs[0].Id);
        runs[2].ContinuedFromRunId.ShouldBe(runs[1].Id);
        runs[2].Status.ShouldBe(RunStatus.Completed, runs[2].Error?.Message);
    }

    /// <summary>
    /// Failure mode 12: with the setting off (the default) nothing changes: no marker,
    /// the item runs again from the beginning, and no run continues another.
    /// </summary>
    [Fact]
    public async Task With_recovery_off_an_interrupted_item_runs_again_from_the_beginning_as_before()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, resumeInterruptedRuns: false, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var jobId = await EnqueueBatchAsync(host, "episode 7");
        var store = host.Services.GetRequiredService<IJobStore>();

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        (await store.ListItemsAsync(jobId)).ShouldHaveSingleItem().RunId.ShouldBeNull("no marker while recovery is off");

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, job, "survivor");

        probe.FirstCalls.ShouldBe(2, "the item ran again from the beginning");

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(2);
        runs.ShouldAllBe(static run => run.ContinuedFromRunId == null);
        runs[0].Status.ShouldBe(RunStatus.Running, "nobody closes the interrupted run while recovery is off");
        (await store.ListItemsAsync(jobId)).ShouldHaveSingleItem().Status.ShouldBe(JobItemStatus.Completed);
    }

    /// <summary>
    /// Failure mode 18: a graceful shutdown closes the run it stops as Canceled and leaves
    /// the job to its lease; the next worker continues it. Two hosts share one SQLite
    /// database, the way a deploy replaces a process.
    /// </summary>
    [Fact]
    public async Task A_run_stopped_by_a_graceful_shutdown_is_continued_by_the_next_worker()
    {
        using var database = new TempSqliteDatabase("workflow-graceful-resume");
        var probe = new RecoveryProbe();
        await using var lifetime = probe;

        Guid headId;
        Guid jobId;

        await using (var first = await StartOnDatabaseAsync(probe, database))
        {
            await SaveRecoverableAsync(first);

            var reached = probe.ArmHang(StepBehavior.HangUntilCancelled);

            using var accepted = await PostAsync(first, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
            headId = await AcceptedRunIdAsync(accepted);
            jobId = headId;

            await reached.WaitAsync(TimeSpan.FromSeconds(30));
            await WaitUntil.TrueAsync(
                async () => (await first.Services.GetRequiredService<IWorkflowCheckpointStore>()
                    .ListByRunAsync(TenantOf(first), headId)).Count > 0,
                "the first super-step's checkpoint");
        }

        await using var second = await StartOnDatabaseAsync(probe, database);

        var stopped = await second.Services.GetRequiredService<IRunStore>().GetRunAsync(headId);
        stopped.ShouldNotBeNull().Status.ShouldBe(RunStatus.Canceled, "a graceful shutdown closes the run it stops");

        var job = await WaitForJobAsync(second, jobId);
        job.Status.ShouldBe(JobStatus.Completed, job.ErrorMessage);
        job.Attempt.ShouldBe(2);

        var continuation = (await WorkflowRunsAsync(second)).Single(run => run.ContinuedFromRunId == headId);
        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);

        probe.FirstCalls.ShouldBe(1);
        probe.SecondCalls.ShouldBe(2);
    }

    /// <summary>
    /// Failure mode 19: a checkpoint written by another Microsoft Agent Framework version
    /// cannot be read. The continuation fails once, naming why, and nothing loops: the
    /// item is reported failed and no further continuation opens.
    /// </summary>
    [Fact]
    public async Task A_checkpoint_the_engine_cannot_read_fails_the_continuation_once_and_does_not_loop()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 3);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var jobId = await EnqueueBatchAsync(host, "episode 7");
        var store = host.Services.GetRequiredService<IJobStore>();

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var head = (await WorkflowRunsAsync(host)).ShouldHaveSingleItem();

        // The newest checkpoint of the interrupted run, as another engine version wrote it.
        await host.Services.GetRequiredService<IWorkflowCheckpointStore>().CreateAsync(new WorkflowCheckpointRecord
        {
            Id = TraconId.NewId(),
            TenantId = head.TenantId!,
            SessionId = head.SessionId!,
            CheckpointId = TraconId.NewId().ToString("n"),
            RunId = head.Id,
            CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1),
            State = JsonDocument.Parse("""{"unreadable":true}""").RootElement.Clone(),
            StateSchemaVersion = 1,
            StateMafVersion = "0.0.1-other",
        });

        var second = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, second, "survivor");

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(2);

        var continuation = runs.Single(run => run.ContinuedFromRunId == head.Id);
        continuation.Status.ShouldBe(RunStatus.Failed);
        continuation.Error.ShouldNotBeNull().Message.ShouldContain("cannot be resumed", Case.Sensitive);

        var item = (await store.ListItemsAsync(jobId)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Failed);

        // A later lease finds nothing pending: no loop.
        await store.ReleaseForRetryAsync(jobId, "test: lease the job again");
        var third = await LeaseAsync(host, "another-worker", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, third, "another-worker");

        (await WorkflowRunsAsync(host)).Count.ShouldBe(2);
        probe.FirstCalls.ShouldBe(1);
    }

    private static async Task<Guid> EnqueueBatchAsync(TraconTestHost host, string item)
        => (await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(new JobRequest
        {
            TenantId = TenantOf(host),
            HandlerKey = JobHandlerKeys.Workflow,
            TargetName = Recoverable,
            Items = [item],
        })).Id;

    private static Task<TraconTestHost> StartOnDatabaseAsync(RecoveryProbe probe, TempSqliteDatabase database)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                Register(builder, probe, resumeInterruptedRuns: true, maxResumeAttempts: 1);
            },
            configureServices: static services => services.Configure<TraconSchedulingOptions>(static options =>
            {
                options.PollInterval = TimeSpan.FromMilliseconds(50);
                options.LeaseDuration = TimeSpan.FromSeconds(2);
            }));
}
