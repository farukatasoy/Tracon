using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-321: the job's LAST lease reaches its handler, and the handler closes the work
/// the interruptions left behind.
/// </summary>
/// <remarks>
/// <para>
/// The worker fails a lease past the job's attempt limit before any handler runs.
/// A queued run's job used to get one attempt for the run and one per continuation,
/// so the lease that should have found the chain at its limit never reached the
/// handler: the last continuation stayed <see cref="RunStatus.Running"/>, a
/// continuation opened and never started stayed <see cref="RunStatus.Queued"/>, and
/// with no continuation allowed nothing detected the interruption at all. A batch
/// job's own limit (three by default) cut the chain the same way once two or more
/// continuations were allowed.
/// </para>
/// <para>
/// Every lease before the last is played by hand, as in <c>WorkflowRecoveryTests</c>;
/// the last one goes through the real worker (<see cref="QueuedWorkflowHarness.FinishWithWorkerAsync"/>),
/// because its attempt check is the boundary under test.
/// </para>
/// </remarks>
public sealed class WorkflowRecoveryAttemptLimitTests
{
    private const string NotContinuedCode = "tracon.job.workflow-run-not-continued";

    private static readonly TimeSpan DeadLease = TimeSpan.FromMilliseconds(300);

    /// <summary>One continuation allowed, two interruptions: the third lease closes the chain.</summary>
    [Fact]
    public async Task A_queued_run_interrupted_past_its_limit_is_closed_Failed_by_the_jobs_last_lease()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 1);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var headId = await AcceptedRunIdAsync(accepted);

        await StartDoomedAttemptAsync(host, probe, DeadLease);
        await StartDoomedAttemptAsync(host, probe, DeadLease, waitForCheckpoint: false);

        var job = await FinishWithWorkerAsync(host, headId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(NotContinuedCode);

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(2, "one continuation is the limit");
        runs.ShouldAllBe(static run => run.Status == RunStatus.Failed);
        runs.ShouldAllBe(static run => run.Error != null && run.Error.Type == "interrupted");
        runs[^1].Error!.Message.ShouldContain("MaxResumeAttempts", Case.Sensitive);

        probe.SecondCalls.ShouldBe(2, "the last lease closes the chain and runs no step");
    }

    /// <summary>
    /// <c>MaxResumeAttempts = 0</c>: nothing continues, but the lease after the
    /// interruption still finds the run and closes it.
    /// </summary>
    [Fact]
    public async Task With_no_continuation_allowed_the_lease_after_the_interruption_closes_the_run_Failed()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 0);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var headId = await AcceptedRunIdAsync(accepted);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await FinishWithWorkerAsync(host, headId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(NotContinuedCode);

        var run = (await WorkflowRunsAsync(host)).ShouldHaveSingleItem();
        run.Id.ShouldBe(headId);
        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe("interrupted");
        run.Error.Message.ShouldContain("MaxResumeAttempts", Case.Sensitive);

        probe.SecondCalls.ShouldBe(1);
    }

    /// <summary>
    /// A lease opened the continuation's row and its process stopped before the engine
    /// started it. That lease counts like any interrupted one: the row is closed, not
    /// left <see cref="RunStatus.Queued"/>.
    /// </summary>
    [Fact]
    public async Task A_continuation_whose_process_stopped_before_it_started_counts_as_an_interruption()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 1);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var headId = await AcceptedRunIdAsync(accepted);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        // The second lease writes the continuation's row, as the handler does before
        // the engine starts it, and dies there.
        await LeaseAsync(host, "dies-after-opening", DeadLease);

        var runStore = host.Services.GetRequiredService<IRunStore>();
        var head = (await runStore.GetRunAsync(headId)).ShouldNotBeNull();
        var openedId = TraconId.NewId();

        await runStore.StartRunAsync(new RunStartInfo
        {
            RunId = openedId,
            AgentName = Recoverable,
            Kind = RunKind.Workflow,
            WorkflowName = Recoverable,
            Status = RunStatus.Queued,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = head.TenantId,
            SessionId = head.SessionId,
            IsStreaming = true,
            ContinuedFromRunId = headId,
        });

        var job = await FinishWithWorkerAsync(host, headId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(NotContinuedCode);

        var runs = await WorkflowRunsAsync(host);
        runs.Select(static run => run.Id).ShouldBe([headId, openedId]);
        runs.ShouldAllBe(static run => run.Status == RunStatus.Failed);
        runs.ShouldAllBe(static run => run.Error != null && run.Error.Type == "interrupted");
        runs[0].Error!.Message.ShouldContain(openedId.ToString(), Case.Sensitive);

        probe.SecondCalls.ShouldBe(1, "the opened continuation never started, and none starts after it");
    }

    /// <summary>
    /// Two continuations allowed, a batch job with the default limit of three attempts:
    /// the third lease is the last, and a continuation it started would have nothing
    /// left to close it if it were interrupted too. It closes the chain instead.
    /// </summary>
    [Fact]
    public async Task A_batch_jobs_last_attempt_closes_an_interrupted_item_instead_of_continuing_it()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 2);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var jobId = await EnqueueBatchAsync(host, "episode 7", maxAttempts: null);

        await StartDoomedAttemptAsync(host, probe, DeadLease);
        await StartDoomedAttemptAsync(host, probe, DeadLease, waitForCheckpoint: false);

        // A continuation started on the last lease would hang here, under a worker
        // that renews its lease; nothing would ever close it.
        _ = probe.ArmHang(StepBehavior.Hang);

        await FinishWithWorkerAsync(host, jobId);

        var item = (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(jobId)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Failed);
        item.Error.ShouldNotBeNull().ShouldContain("last attempt", Case.Sensitive);

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(2);
        runs.ShouldAllBe(static run => run.Status == RunStatus.Failed);
        runs.ShouldAllBe(static run => run.Error != null && run.Error.Type == "interrupted");
        item.RunId.ShouldBe(runs[^1].Id);

        probe.SecondCalls.ShouldBe(2);
    }

    /// <summary>The same chain gets its second continuation when the job has the attempts for it.</summary>
    [Fact]
    public async Task A_batch_job_with_two_more_attempts_than_continuations_gets_every_continuation()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false, maxResumeAttempts: 2);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var jobId = await EnqueueBatchAsync(host, "episode 7", maxAttempts: 4);

        await StartDoomedAttemptAsync(host, probe, DeadLease);
        await StartDoomedAttemptAsync(host, probe, DeadLease, waitForCheckpoint: false);

        await FinishWithWorkerAsync(host, jobId);

        var item = (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(jobId)).ShouldHaveSingleItem();
        item.Status.ShouldBe(JobItemStatus.Completed, item.Error);

        var runs = await WorkflowRunsAsync(host);
        runs.Count.ShouldBe(3);
        runs[^1].Status.ShouldBe(RunStatus.Completed, runs[^1].Error?.Message);
        item.RunId.ShouldBe(runs[^1].Id);

        probe.FirstCalls.ShouldBe(1);
        probe.SecondCalls.ShouldBe(3);
    }

    private static async Task<Guid> EnqueueBatchAsync(TraconTestHost host, string item, int? maxAttempts)
        => (await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(new JobRequest
        {
            TenantId = TenantOf(host),
            HandlerKey = JobHandlerKeys.Workflow,
            TargetName = Recoverable,
            Items = [item],
            MaxAttempts = maxAttempts,
        })).Id;
}
