using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A queued workflow run that an interruption stopped is continued on the budget of the chain, not on a fresh one
/// (F-531): the continuation starts from what the interrupted run spent, and the steps that run before it are not
/// counted twice.
/// </summary>
/// <remarks>
/// <para>
/// The crash is played the way <see cref="WorkflowRecoveryTests"/> plays it: a worker leases the job, its second step
/// hangs and ignores the run's token, and its lease is not renewed. What the interrupted run spent beyond tokens is in
/// its items, not in its row: the process that kept the total is gone, and the recovery closes the row without one.
/// </para>
/// </remarks>
public sealed class WorkflowRecoveryBudgetTests
{
    private static readonly TimeSpan DeadLease = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_continuation_is_refused_at_the_step_the_interrupted_run_had_spent_the_ceiling_in()
    {
        // 0.7 in the first step, 0.5 in the second before the process died in it: 1.2 against a ceiling of 1.
        var probe = new RecoveryProbe { FirstAmount = 0.7m, SecondAmount = 0.5m };

        await using var host = await StartWithCeilingAsync(probe, ceiling: 1m);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var headId = await EnqueueAsync(host);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));

        // A workflow run that ends Failed fails the job without a retry: the same run would be refused again.
        await Should.ThrowAsync<NonRetryableJobException>(async () => await RunAttemptAsync(host, job, "survivor"));

        var continuation = (await WorkflowRunsAsync(host)).Single(run => run.Id != headId);

        continuation.ContinuedFromRunId.ShouldBe(headId);
        continuation.Status.ShouldBe(RunStatus.Failed);

        var error = continuation.Error.ShouldNotBeNull();

        error.Type.ShouldBe(TraconRunBudgetExceededException.RunBudgetExceededErrorType);
        error.Message.ShouldContain("Could not start function node 'recover-second'", Case.Sensitive);
        error.Message.ShouldContain("cost budget is exhausted (1.200000/1.000000)", Case.Sensitive);
        probe.SecondCalls.ShouldBe(1, "the step the interrupted run reached started again on a budget of zero");
    }

    [Fact]
    public async Task A_continuation_below_the_ceiling_carries_what_the_interrupted_run_spent_and_goes_on()
    {
        var probe = new RecoveryProbe { FirstAmount = 0.3m, SecondAmount = 0.2m };

        await using var host = await StartWithCeilingAsync(probe, ceiling: 1m);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var headId = await EnqueueAsync(host);

        await StartDoomedAttemptAsync(host, probe, DeadLease);

        var job = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, job, "survivor");

        var continuation = (await WorkflowRunsAsync(host)).Single(run => run.Id != headId);

        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);

        // The doomed attempt read 0.3 + 0.2. The continuation started from the 0.5 the interrupted run had
        // reported, and its second step reported 0.2 again: 0.7. Started from zero it would read 0.2.
        probe.CostAtSecond.ShouldBe([0.5m, 0.7m]);
        probe.FirstCalls.ShouldBe(1, "the first step is not run again; its amount is carried once, from the run that spent it");
    }

    [Fact]
    public async Task A_queued_resume_that_was_interrupted_is_continued_from_its_own_link_and_carries_the_whole_chain()
    {
        // H ran to its end (0.3 in the first step, 0.2 in the second). An operator queued a resume of H from its first
        // checkpoint: R. R's process died in the second step, after it reported 0.2 again. The recovery opens C,
        // linked to R, and R is linked to H: the chain C carries is R and H.
        var probe = new RecoveryProbe { FirstAmount = 0.3m, SecondAmount = 0.2m };

        await using var host = await StartWithCeilingAsync(probe, ceiling: 100m);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var head = await EnqueueAsync(host);

        await RunAttemptAsync(host, await LeaseAsync(host, "first-worker", TimeSpan.FromMinutes(5)), "first-worker");

        (await WorkflowRunsAsync(host)).Single().Status.ShouldBe(RunStatus.Completed);

        var checkpoint = (await host.Services.GetRequiredService<IWorkflowCheckpointStore>().ListByRunAsync(TenantOf(host), head))[0].CheckpointId;

        using var accepted = await PostAsync(
            host,
            $"/api/workflows/runs/{head}/resume",
            new WorkflowResumeHttpRequest { CheckpointId = checkpoint });

        var resume = await AcceptedRunIdAsync(accepted);

        // R starts from H's first checkpoint, so it has none of its own when it hangs in the same step.
        await StartDoomedAttemptAsync(host, probe, DeadLease, waitForCheckpoint: false);

        var survivor = await LeaseAsync(host, "survivor", TimeSpan.FromMinutes(5));
        await RunAttemptAsync(host, survivor, "survivor");

        var runs = await WorkflowRunsAsync(host);
        var interrupted = runs.Single(run => run.Id == resume);
        var continuation = runs.Single(run => run.Id != head && run.Id != resume);

        interrupted.ContinuedFromRunId.ShouldBe(head, "the operator's resume names the run it resumes");
        interrupted.Status.ShouldBe(RunStatus.Failed);
        interrupted.Error.ShouldNotBeNull().Type.ShouldBe("interrupted");
        continuation.ContinuedFromRunId.ShouldBe(resume, "the recovery continues the resume, not the run the resume resumed");
        continuation.Status.ShouldBe(RunStatus.Completed, continuation.Error?.Message);

        // H read 0.5 at its second step; R started from H's 0.5 and reported 0.2 (0.7); C started from R and H: R's
        // 0.2 (read from its items, its row holds no total) and H's 0.5, and reported 0.2 again.
        probe.CostAtSecond.ShouldBe([0.5m, 0.7m, 0.9m]);
    }

    private static Task<TraconTestHost> StartWithCeilingAsync(RecoveryProbe probe, decimal ceiling)
        => StartAsync(
            probe,
            runWorker: false,
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.Pricing.Currency = "USD";
                options.AgentGraph.MaxTotalCost = ceiling;
            }));

    private static async Task<Guid> EnqueueAsync(TraconTestHost host)
    {
        using var accepted = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" });

        return await AcceptedRunIdAsync(accepted);
    }
}
