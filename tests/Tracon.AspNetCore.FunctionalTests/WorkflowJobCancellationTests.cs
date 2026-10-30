using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A cancelled workflow job stops the workflow run it is running (failure mode 21),
/// through the real worker's cancel watch.
/// </summary>
/// <remarks>
/// <para>
/// Before that change, the run of a cancelled <c>tracon.workflow</c> job ran to its end:
/// the handler asked <see cref="JobContext.IsCancelledAsync"/> only between items, and
/// the token the run was given fell only when the worker shut down. The run is now
/// cancelled within a poll interval and ends <c>Canceled</c>; the job stays
/// <c>Cancelled</c>.
/// </para>
/// <para>
/// A queued single run cancelled through <c>POST /api/runs/{id}/cancel</c> is
/// <c>WorkflowAsyncRunCancelTests</c>; this class cancels the JOB.
/// </para>
/// </remarks>
public sealed class WorkflowJobCancellationTests
{
    [Fact]
    public async Task Cancelling_a_running_batch_job_cancels_its_workflow_run()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, resumeInterruptedRuns: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var reached = probe.ArmHang(StepBehavior.HangUntilCancelled);
        var job = await host.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = TenantOf(host),
                HandlerKey = JobHandlerKeys.Workflow,
                TargetName = Recoverable,
                Items = ["episode 7", "episode 8"],
                MaxAttempts = 1,
            },
            TestContext.Current.CancellationToken);

        await reached.WaitAsync(TimeSpan.FromSeconds(30));

        var running = (await WorkflowRunsAsync(host)).ShouldHaveSingleItem();

        using var cancelled = await host.Client.PostAsync($"/tracon/api/jobs/{job.Id}/cancel", content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        await host.WaitForRunStatusAsync(running.Id, nameof(RunStatus.Canceled));

        var settled = await WaitForJobAsync(host, job.Id);

        settled.Status.ShouldBe(JobStatus.Cancelled);

        // The second item never started, and the first was not reported: a cancelled
        // job's items stay as they were.
        probe.SecondCalls.ShouldBe(1);
        (await host.Services.GetRequiredService<IJobStore>().ListItemsAsync(job.Id, TestContext.Current.CancellationToken))
            .ShouldAllBe(static item => item.Status == JobItemStatus.Pending);
        (await WorkflowRunsAsync(host)).ShouldHaveSingleItem().Error.ShouldBeNull("a cancel the operator asked for carries no reason");
    }

    [Fact]
    public async Task Cancelling_a_queued_runs_job_cancels_the_run_and_it_is_not_continued()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, maxResumeAttempts: 2);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var reached = probe.ArmHang(StepBehavior.HangUntilCancelled);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        await reached.WaitAsync(TimeSpan.FromSeconds(30));

        // The job's id is the run's id.
        using var cancelled = await host.Client.PostAsync($"/tracon/api/jobs/{runId}/cancel", content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent, await cancelled.Content.ReadAsStringAsync());

        await host.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));

        (await WaitForJobAsync(host, runId)).Status.ShouldBe(JobStatus.Cancelled);
        (await WorkflowRunsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(runId);
    }
}
