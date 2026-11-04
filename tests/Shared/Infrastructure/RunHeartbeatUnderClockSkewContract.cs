using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Tests.Common;

/// <summary>
/// What the SQL run store does with a run that is alive when the node that closes orphaned runs reads a clock that runs
/// ahead of the node that runs it, written once for every SQL provider whose statements read the database server's clock.
/// </summary>
/// <remarks>
/// <para>
/// The run's node marks <c>runs.heartbeat_at</c> (<c>RunHeartbeatWriter</c> calls
/// <see cref="IRunStore.TouchHeartbeatAsync"/>), and the node that closes orphaned runs
/// (<c>RunReconciliationService</c>) asks <see cref="IRunStore.ClaimOrphanedRunsAsync"/> for the rows whose mark is older than
/// <see cref="RunReconciliationOptions.OrphanThreshold"/>. When the mark was the run's node's clock and the age was the scanning
/// node's clock, a beating run was closed as <c>Failed</c> by a node whose clock was ahead by more than the threshold minus the age
/// of the last beat: with the defaults (a beat every 30 seconds, a threshold of 5 minutes) between 4 and a half and 5 minutes.
/// Both now read the database server's clock, whoever scans.
/// </para>
/// <para>
/// The two services run for real, on the real SQL store, each with its own <see cref="TimeProvider"/>: the second one's wall clock
/// is shifted by the skew, its timers are the system's. The intervals are shortened so that a case ends in about a second; what is
/// measured is the shape (the threshold minus the age of the last beat), not the defaults.
/// </para>
/// <para>
/// <strong>Measured before the change</strong> (PostgreSQL 18 and SQL Server 2022, the same on both): the run stayed
/// <c>Running</c> with the scanning node 0, 240 and 299 seconds ahead and was closed <c>Failed</c> (error class Infrastructure) with it
/// 301 and 900 seconds ahead. The control (a run with no heartbeat at all) is closed at no skew, so the harness can see a closed
/// run. The scenarios state what a healthy cluster must do: a beating run stays <c>Running</c> whoever scans.
/// </para>
/// </remarks>
public abstract class RunHeartbeatUnderClockSkewContract
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan HeartbeatEvery = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan ScanEvery = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    /// <summary>Opens the stores of one migrated, empty schema.</summary>
    /// <returns>The run store, the single-executor lease store and the job store.</returns>
    protected abstract ValueTask<(IRunStore Runs, ISingletonLeaseStore Leases, IJobStore Jobs)> OpenAsync();

    /// <summary>
    /// A run that is beating is not closed by a node whose clock runs ahead.
    /// </summary>
    /// <param name="aheadSeconds">How far the scanning node's clock is ahead of the node that runs the run.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(240)]
    [InlineData(299)]
    [InlineData(301)]
    [InlineData(900)]
    public async Task A_run_that_keeps_its_heartbeat_is_not_closed_by_a_node_whose_clock_runs_ahead(int aheadSeconds)
    {
        var (runs, leases, jobs) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId) with { StartedAt = DateTimeOffset.UtcNow });

        var registry = new RunCancellationRegistry();
        using var cancellation = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, tenantId: null, cancellation);

        var status = await RunBothNodesAsync(runs, leases, jobs, registry, TimeSpan.FromSeconds(aheadSeconds), runId);

        Describe($"the run's node beats every {HeartbeatEvery.TotalMilliseconds} ms; the scanning node is ahead by {aheadSeconds}s and scans every {ScanEvery.TotalMilliseconds} ms for {Window.TotalSeconds}s", $"the run is {status}");

        status.ShouldBe(RunStatus.Running, "a run that is beating is alive on every clock");
    }

    /// <summary>
    /// The control: with no heartbeat at all the same scan closes a run that started longer ago than the threshold, so the
    /// scenario above can tell a closed run from one nobody looked at.
    /// </summary>
    [Fact]
    public async Task A_run_that_never_beats_is_closed_by_the_scan_the_other_scenarios_run()
    {
        var (runs, leases, jobs) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId) with { StartedAt = DateTimeOffset.UtcNow - Threshold - TimeSpan.FromMinutes(1) });

        var status = await RunBothNodesAsync(runs, leases, jobs, new RunCancellationRegistry(), TimeSpan.Zero, runId);

        Describe("no beat at all, started 6 minutes ago, scanning node on time", $"the run is {status}");

        status.ShouldBe(RunStatus.Failed);
    }

    private static async Task<RunStatus> RunBothNodesAsync(
        IRunStore runs,
        ISingletonLeaseStore leases,
        IJobStore jobs,
        RunCancellationRegistry registry,
        TimeSpan ahead,
        Guid runId)
    {
        var options = new StaticOptionsMonitor<RunReconciliationOptions>(new RunReconciliationOptions
        {
            Enabled = true,
            HeartbeatInterval = HeartbeatEvery,
            ScanInterval = ScanEvery,
            OrphanThreshold = Threshold,
        });
        var gate = new SchemaReadyGate([]);

        var writer = new RunHeartbeatWriter(runs, registry, options, gate, new SkewedTimeProvider(TimeSpan.Zero), NullLogger<RunHeartbeatWriter>.Instance);
        var sweeper = new RunReconciliationService(
            runs,
            leases,
            jobs,
            new EmptyToolRegistry(),
            options,
            new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions()),
            new StaticOptionsMonitor<TraconRunContinuationOptions>(new TraconRunContinuationOptions()),
            gate,
            new SkewedTimeProvider(ahead),
            NullLogger<RunReconciliationService>.Instance);

        await writer.StartAsync(CancellationToken.None);
        await sweeper.StartAsync(CancellationToken.None);

        try
        {
            // The status is read as soon as the run leaves Running, or when the window is over. A run that is still
            // Running at the end of the window is the answer of the scenarios that expect it to stay, not a failure.
            await WaitUntil.ValueAsync(
                async () => (await runs.GetRunAsync(runId))?.Status,
                status => status != RunStatus.Running,
                "the run to leave Running",
                Window);
        }
        catch (TimeoutException)
        {
            // The window is over and the run is still Running; the status is read below.
        }
        finally
        {
            await sweeper.StopAsync(CancellationToken.None);
            await writer.StopAsync(CancellationToken.None);
        }

        return (await runs.GetRunAsync(runId))!.Status;
    }

    /// <summary>Writes one measured row where a run with detailed output shows it.</summary>
    private static void Describe(string setup, string observed)
        => TestContext.Current.TestOutputHelper?.WriteLine($"[clock skew] run heartbeat | {setup} | {observed}");

    /// <summary>The system's timers and a wall clock shifted by a fixed amount.</summary>
    private sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + offset;
    }

    private sealed class EmptyToolRegistry : IToolRegistry
    {
        public IReadOnlyList<ToolDescriptor> List() => [];

        public bool TryGet(
            string name,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Microsoft.Extensions.AI.AIFunctionDeclaration? tool)
        {
            tool = null;

            return false;
        }
    }

    /// <summary>An options monitor that returns one value and never tracks changes.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
