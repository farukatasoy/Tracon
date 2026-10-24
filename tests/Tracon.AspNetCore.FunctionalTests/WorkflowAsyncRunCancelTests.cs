using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure mode 4: a queued workflow run is cancelled through the run cancel endpoint,
/// before the worker starts it and while it runs, and a cancelled run is not continued.
/// </summary>
/// <remarks>
/// <c>POST /api/runs/{id}/cancel</c> cancels a queued row through the job queue, and
/// that only works because the job id is the run id (K-305). A cancelled run is not an
/// interruption: recovery must not continue it when its job is leased again.
/// </remarks>
public sealed class WorkflowAsyncRunCancelTests
{
    [Fact]
    public async Task A_queued_run_cancelled_before_the_worker_starts_it_never_runs()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        using var cancelled = await host.Client.PostAsync($"/tracon/api/runs/{runId}/cancel", content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancelled.Content.ReadAsStringAsync());

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Canceled);
        (await host.Services.GetRequiredService<IJobStore>().GetAsync(TenantOf(host), runId)).ShouldNotBeNull().Status.ShouldBe(JobStatus.Cancelled);

        // A worker would never lease a cancelled job; nothing is ready to lease.
        (await host.Services.GetRequiredService<IJobStore>().LeaseAsync(new JobLeaseRequest { Owner = "worker", LeaseDuration = TimeSpan.FromMinutes(5) })).ShouldBeNull();
        probe.FirstCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_queued_run_leased_and_then_cancelled_does_not_start()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        // The worker holds the job and has marked it Running; the run is still
        // Queued when the cancel arrives, before the handler starts it.
        var job = await LeaseAsync(host, "worker", TimeSpan.FromMinutes(5));
        (await host.Services.GetRequiredService<IJobStore>().MarkRunningAsync(job.Id, "worker")).ShouldBeTrue();

        using var cancelled = await host.Client.PostAsync($"/tracon/api/runs/{runId}/cancel", content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        await RunAttemptAsync(host, job, "worker", markRunning: false);

        probe.FirstCalls.ShouldBe(0);
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId)).ShouldNotBeNull().Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task A_running_queued_run_cancelled_through_the_endpoint_ends_Canceled_and_is_not_continued()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, maxResumeAttempts: 3);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var reached = probe.ArmHang(StepBehavior.HangUntilCancelled);

        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });
        var runId = await AcceptedRunIdAsync(accepted);

        await reached.WaitAsync(TimeSpan.FromSeconds(30));

        using var cancelled = await host.Client.PostAsync($"/tracon/api/runs/{runId}/cancel", content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.Accepted, await cancelled.Content.ReadAsStringAsync());

        await host.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));

        // The run ended on its own: the job fails at once, with no retry that
        // recovery could mistake for an interruption.
        var job = await WaitForJobAsync(host, runId);
        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(WorkflowRunJobHandler.RunFailedCode);
        job.Attempt.ShouldBe(1);

        (await WorkflowRunsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(runId);
    }
}
