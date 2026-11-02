using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// A reconciliation scan claims every orphaned run in one call and then decides, run by
/// run, whether a continuation follows. A run whose decision throws must not decide the
/// fate of the runs claimed after it.
/// </summary>
/// <remarks>
/// The claim already closed every claimed row as Failed, so a run the scan skips is not
/// orphaned any more and no later scan looks at it: its continuation is lost for good.
/// The tick is driven on a manual clock, so the scan runs exactly when the test says.
/// </remarks>
public sealed class RunReconciliationRowIsolationTests
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_run_whose_continuation_cannot_be_opened_does_not_stop_the_runs_claimed_after_it()
    {
        var clock = new ManualClock();
        var inner = new InMemoryRunStore();
        var jobs = new InMemoryJobStore();

        // Two session-bound runs, both long past the orphan threshold.
        var first = await StartOrphanAsync(inner, clock, "session-a");
        var second = await StartOrphanAsync(inner, clock, "session-b");

        // The first continuation row any scan tries to write fails; every later write goes through.
        // Which run is claimed first is the store's business, so the test names no run.
        var plan = new FaultPlan
        {
            Fail = static call => call is { Method: nameof(IRunStore.StartRunAsync), MethodIndex: 1 }
                ? new InvalidOperationException("the store blinked")
                : null,
        };
        var runs = FaultProxy<IRunStore>.Create(inner, plan);

        using var service = CreateService(runs, jobs, clock);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitForTimerAsync(clock);

        clock.Advance(ScanInterval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IRunStore.ClaimOrphanedRunsAsync)) >= 1,
            "the first scan to claim the orphans");

        // A second scan can only start after the first one returned, so its claim proves the first is over.
        clock.Advance(ScanInterval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IRunStore.ClaimOrphanedRunsAsync)) >= 2,
            "the second scan to start");

        await service.StopAsync(TestContext.Current.CancellationToken);

        // One continuation row was refused, the other run must have been continued anyway.
        var continuations = await jobs.QueryAsync(new JobQuery { TenantId = "default" });
        continuations.Count(static job => string.Equals(job.HandlerKey, JobHandlerKeys.RunContinuation, StringComparison.Ordinal)).ShouldBe(
            1,
            "the run claimed after the failing one must still be continued");

        (await inner.GetRunAsync(first))!.Status.ShouldBe(RunStatus.Failed);
        (await inner.GetRunAsync(second))!.Status.ShouldBe(RunStatus.Failed);
    }

    private static RunReconciliationService CreateService(IRunStore runs, InMemoryJobStore jobs, TimeProvider clock)
        => new(
            runs,
            new InMemorySingletonLeaseStore(),
            jobs,
            TestData.Registry(),
            new StaticOptionsMonitor<RunReconciliationOptions>(new RunReconciliationOptions
            {
                Enabled = true,
                ScanInterval = ScanInterval,
                OrphanThreshold = TimeSpan.FromMinutes(5),
                HeartbeatInterval = TimeSpan.FromSeconds(30),
            }),
            new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions()),
            new StaticOptionsMonitor<TraconRunContinuationOptions>(new TraconRunContinuationOptions { Enabled = true }),
            new SchemaReadyGate([]),
            clock,
            NullLogger<RunReconciliationService>.Instance);

    private static async Task<Guid> StartOrphanAsync(InMemoryRunStore store, ManualClock clock, string sessionId)
    {
        var runId = TraconId.NewId();

        await store.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = clock.GetUtcNow().AddMinutes(-10),
            SessionId = sessionId,
        });

        return runId;
    }

    private static Task WaitForTimerAsync(ManualClock clock)
        => WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the scan loop to arm its timer");
}
