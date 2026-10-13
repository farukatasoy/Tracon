using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// With recovery on, a job's time limit ends the workflow's work; it is not an
/// interruption that the next attempt continues (F-323, open question 13-A, failure
/// mode 29).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Phase 197 measured why this needs saying: recovery does not tell a time limit from
/// a crash. Treated as an interruption, the next attempt continued the run from its
/// last checkpoint, the limit applied per attempt, and the work ran on for
/// <c>MaxResumeAttempts</c> more attempts - the limit stopped nothing. The run the
/// limit stops now closes with the error type <c>job_max_duration_exceeded</c>, and
/// <see cref="WorkflowRecovery"/> plans such a run as <c>Stopped</c>.
/// </para>
/// <para>
/// Every lease here is the host's own worker - the deadline lives in the worker, and
/// a handler driven by hand would skip it (the phase 197 test trap).
/// </para>
/// </remarks>
public sealed class WorkflowJobDeadlineRecoveryTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task A_queued_run_stopped_by_its_jobs_limit_fails_the_job_at_once_and_is_not_continued()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(
            probe,
            maxResumeAttempts: 2,
            configureServices: static services => services.Configure<TraconSchedulingOptions>(
                static options => options.MaxDurationByHandlerKey[JobHandlerKeys.WorkflowRun] = Limit));
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        _ = probe.ArmHang(StepBehavior.HangUntilCancelled);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        var job = await WaitForJobAsync(host, runId);

        // Not left to the lease, not retried: the limit ended the work on attempt 1
        // of the 2 + MaxResumeAttempts the job was given.
        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith($"{JobErrorCodes.MaxDurationExceeded} (ref: ");
        job.Attempt.ShouldBe(1);
        job.MaxAttempts.ShouldBe(4);

        var run = (await WorkflowRunsAsync(host)).ShouldHaveSingleItem("no continuation may follow a run its limit stopped");

        run.Id.ShouldBe(runId);
        run.Status.ShouldBe(RunStatus.Canceled);
        run.Error.ShouldNotBeNull();
        run.Error.Type.ShouldBe("job_max_duration_exceeded");
        run.Error.Class.ShouldBe(RunErrorClass.Timeout);
        probe.SecondCalls.ShouldBe(1);
    }

    [Fact]
    public async Task A_batch_item_stopped_by_the_limit_fails_and_the_next_attempt_runs_only_the_items_left()
    {
        // MaxAttempts 3 with one continuation allowed: recovery is on and could
        // continue an interrupted item. The item the limit stopped is reported failed
        // instead; the attempt fails (open question 7-A) and the next one, under a
        // fresh limit, runs the item it never reached.
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, maxResumeAttempts: 1);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        _ = probe.ArmHang(StepBehavior.HangUntilCancelled);

        var queued = await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = TenantOf(host),
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = Recoverable,
                Items = ["episode 7", "episode 8"],
                MaxAttempts = 3,
                MaxDuration = Limit,
            },
            TestContext.Current.CancellationToken);

        var job = await WaitForJobAsync(host, queued.Id);

        job.Status.ShouldBe(JobStatus.Completed);
        job.Attempt.ShouldBe(2, "the attempt the limit stopped counts like any other");

        var items = await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(queued.Id, TestContext.Current.CancellationToken);

        items[0].Status.ShouldBe(JobItemStatus.Failed);
        items[0].Error.ShouldNotBeNull().ShouldContain("time limit");
        items[1].Status.ShouldBe(JobItemStatus.Completed);

        var stopped = (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(items[0].RunId!.Value, TestContext.Current.CancellationToken))!;

        stopped.Status.ShouldBe(RunStatus.Canceled);
        stopped.Error.ShouldNotBeNull().Type.ShouldBe("job_max_duration_exceeded");

        // Nothing continues the stopped run: one run per item, the second step called
        // once per item.
        (await host.Services.GetRequiredService<IRunStore>().QueryRunsAsync(
                new RunQuery { ContinuedFromRunId = stopped.Id, Kind = RunKind.Workflow, OnlyRootRuns = false, Take = 10 },
                TestContext.Current.CancellationToken))
            .ShouldBeEmpty();
        (await WorkflowRunsAsync(host)).Count.ShouldBe(2);
        probe.SecondCalls.ShouldBe(2);
    }

    [Fact]
    public async Task When_the_limit_stops_the_last_pending_item_the_job_fails_at_once_with_the_code()
    {
        // Found in the sample app (manual case 7): with one item, the stopped item was
        // reported failed, the attempt was released, and the next lease found nothing to
        // run - the job ended "All of the job's items failed." without the code, one
        // pointless lease later.
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, maxResumeAttempts: 1);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        _ = probe.ArmHang(StepBehavior.HangUntilCancelled);

        var queued = await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = TenantOf(host),
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = Recoverable,
                Items = ["episode 7"],
                MaxAttempts = 3,
                MaxDuration = Limit,
            },
            TestContext.Current.CancellationToken);

        var job = await WaitForJobAsync(host, queued.Id);

        job.Status.ShouldBe(JobStatus.Failed);
        job.Attempt.ShouldBe(1);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith($"{JobErrorCodes.MaxDurationExceeded} (ref: ");

        var item = (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(queued.Id, TestContext.Current.CancellationToken)).Single();

        item.Status.ShouldBe(JobItemStatus.Failed);
        (await WorkflowRunsAsync(host)).ShouldHaveSingleItem().Error.ShouldNotBeNull().Type.ShouldBe("job_max_duration_exceeded");
    }

    [Fact]
    public async Task On_the_jobs_last_attempt_the_limit_fails_the_job_with_the_code()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, maxResumeAttempts: 1);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        _ = probe.ArmHang(StepBehavior.HangUntilCancelled);

        var queued = await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = TenantOf(host),
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = Recoverable,
                Items = ["episode 7", "episode 8"],
                MaxAttempts = 1,
                MaxDuration = Limit,
            },
            TestContext.Current.CancellationToken);

        var job = await WaitForJobAsync(host, queued.Id);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith($"{JobErrorCodes.MaxDurationExceeded} (ref: ");

        var items = await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(queued.Id, TestContext.Current.CancellationToken);

        items[0].Status.ShouldBe(JobItemStatus.Failed);
        items[1].Status.ShouldBe(JobItemStatus.Pending, "the attempt ended at the limit before it reached the second item");
    }
}
