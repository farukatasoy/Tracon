using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// The clock a host registers with the container is the clock the two hosted services of orphaned-run reconciliation
/// run on: the heartbeat writer's timer and mark, and the scan's timer.
/// </summary>
/// <remarks>
/// <para>
/// Both services take their clock as an optional constructor parameter and the container builds them. A parameter that
/// is never filled (a registration that stops passing the clock, a constructor change that no longer asks for it) leaves
/// the service on the system clock without a compile error: the heartbeat then marks runs with one time while the store
/// ages them with another, or the scan never fires on a clock a consumer controls. The store's own clock is covered by
/// <see cref="Storage.RunStoreClockRegistrationTests"/>; these tests go through the registration and the services.
/// </para>
/// </remarks>
public sealed class RunReconciliationClockRegistrationTests
{
    private const string Tenant = "tenant-x";

    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Scan = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The heartbeat fires when the registered clock reaches its interval, and the mark it writes is that clock's time:
    /// the store reads the run as fresh until a threshold after the beat, and as orphaned right after.
    /// </summary>
    [Fact]
    public async Task The_heartbeat_writer_beats_and_marks_on_the_registered_clock()
    {
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var writer = provider.GetRequiredService<RunHeartbeatWriter>();
        var runId = await StartRunAsync(runs, clock.GetUtcNow(), Tenant);

        using var cancellation = new CancellationTokenSource();
        using var registration = provider.GetRequiredService<IRunCancellationRegistry>().Register(runId, runId, Tenant, cancellation);

        // A request written on the row comes back on the next beat and reaches the registered run: the one signal that a
        // beat happened, so the test does not have to guess when the writer finished.
        (await runs.RequestCancellationAsync(runId, Tenant, clock.GetUtcNow(), TestContext.Current.CancellationToken)).ShouldBeTrue();

        await writer.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Heartbeat), "the writer to arm its timer on the registered clock");

        clock.Advance(Heartbeat);

        await WaitUntil.TrueAsync(() => cancellation.IsCancellationRequested, "the beat to reach the registered run");
        await writer.StopAsync(TestContext.Current.CancellationToken);

        // The run beat at start + 30 s. A mark of any earlier time (the start, which is what a run that never beat is
        // aged from) is old enough to close at the first probe.
        clock.Advance(Threshold - TimeSpan.FromSeconds(1));

        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a run that beat 30 s after its start is fresh until the threshold after that beat");

        clock.Advance(TimeSpan.FromSeconds(2));

        (await runs.ClaimOrphanedRunsAsync(Threshold, 10, TestContext.Current.CancellationToken))
            .ShouldHaveSingleItem().Id.ShouldBe(runId);
    }

    /// <summary>
    /// The scan fires when the registered clock reaches its interval and ages runs by that clock: it closes a run that is
    /// old on that clock and leaves one that is not, then closes the second one once the threshold has passed.
    /// </summary>
    [Fact]
    public async Task The_reconciliation_scan_fires_and_ages_runs_on_the_registered_clock()
    {
        var clock = new ManualClock();
        await using var provider = Build(clock);
        var runs = provider.GetRequiredService<IRunStore>();
        var scan = provider.GetRequiredService<RunReconciliationService>();
        var start = clock.GetUtcNow();
        var old = await StartRunAsync(runs, start - TimeSpan.FromMinutes(10), tenant: null);
        var fresh = await StartRunAsync(runs, start, tenant: null);

        await scan.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Contains(Scan), "the scan to arm its timer on the registered clock");

        clock.Advance(Scan);

        // The old run closing is the signal that the scan ran; the fresh one was in the same pass.
        await WaitUntil.TrueAsync(async () => await StatusAsync(runs, old) == RunStatus.Failed, "the scan to close the run that was old");
        (await StatusAsync(runs, fresh)).ShouldBe(RunStatus.Running, "a run that started a minute ago on the registered clock is not old");

        clock.Advance(Threshold);

        await WaitUntil.TrueAsync(async () => await StatusAsync(runs, fresh) == RunStatus.Failed, "the scan to close the run once the threshold passed");
        await scan.StopAsync(TestContext.Current.CancellationToken);
    }

    // A run of the default tenant, which is the one a read through the container's store looks in.
    private static async Task<Guid> StartRunAsync(IRunStore runs, DateTimeOffset startedAt, string? tenant)
    {
        var runId = TraconId.NewId();

        await runs.StartRunAsync(
            new RunStartInfo { RunId = runId, AgentName = "clock-agent", StartedAt = startedAt, TenantId = tenant },
            TestContext.Current.CancellationToken);

        return runId;
    }

    private static async Task<RunStatus?> StatusAsync(IRunStore runs, Guid runId)
        => (await runs.GetRunAsync(runId, TestContext.Current.CancellationToken))?.Status;

    private static ServiceProvider Build(TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddTracon();
        services.Configure<RunReconciliationOptions>(options =>
        {
            options.Enabled = true;
            options.HeartbeatInterval = Heartbeat;
            options.OrphanThreshold = Threshold;
            options.ScanInterval = Scan;
        });

        return services.BuildServiceProvider();
    }
}
