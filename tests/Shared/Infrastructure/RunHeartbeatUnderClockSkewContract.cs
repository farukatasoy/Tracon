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
/// The two services run for real, on the real SQL store, each with its own <see cref="TimeProvider"/>: the scanning node's wall
/// clock is shifted ahead (its service and its run store read the same shifted clock) and the run's node's clock behind, their
/// timers are the system's. The run's node hands no time to <see cref="IRunStore.TouchHeartbeatAsync"/>
/// (the store marks the row with the database server's clock), so a node whose clock is behind by more than the threshold still keeps its run alive; the
/// scanning node's store cuts the threshold from the database server's clock, not from its own. The intervals are shortened so that a case ends in about a second; what is
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
    /// Opens another run store over the schema <see cref="OpenAsync"/> opened: the store of the node that scans, whose
    /// application clock is <paramref name="clock"/>.
    /// </summary>
    /// <param name="clock">The application clock of the node that scans.</param>
    /// <returns>The run store.</returns>
    protected abstract IRunStore OpenRunStore(TimeProvider clock);

    /// <summary>
    /// Writes the row an older node writes when it starts a run: the run is <c>Running</c>, it has never beaten (no
    /// <c>heartbeat_at</c>) and it started at <paramref name="startedAt"/> on that node's clock.
    /// </summary>
    /// <param name="runId">The run, already started.</param>
    /// <param name="startedAt">When the older node started it, on its own clock.</param>
    /// <returns>The completion task.</returns>
    protected abstract ValueTask ClearHeartbeatAsync(Guid runId, DateTimeOffset startedAt);

    /// <summary>
    /// A run that is beating is not closed, whatever the clocks of the node that runs it and of the node that scans read.
    /// </summary>
    /// <param name="aheadSeconds">How far the scanning node's clock is ahead of the database server's.</param>
    /// <param name="behindSeconds">How far the clock of the node that runs the run is behind the database server's.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(240, 0)]
    [InlineData(299, 0)]
    [InlineData(301, 0)]
    [InlineData(900, 0)]
    [InlineData(0, 600)]
    [InlineData(301, 600)]
    public async Task A_run_that_keeps_its_heartbeat_is_not_closed_whatever_the_clocks_of_the_two_nodes_read(int aheadSeconds, int behindSeconds)
    {
        var (runs, leases, jobs) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId) with { StartedAt = DateTimeOffset.UtcNow });

        var registry = new RunCancellationRegistry();
        using var cancellation = new CancellationTokenSource();
        using var registration = registry.Register(runId, runId, tenantId: null, cancellation);

        var status = await RunBothNodesAsync(runs, leases, jobs, registry, TimeSpan.FromSeconds(aheadSeconds), TimeSpan.FromSeconds(-behindSeconds), runId);

        Describe($"the run's node, {behindSeconds}s behind, beats every {HeartbeatEvery.TotalMilliseconds} ms; the scanning node is ahead by {aheadSeconds}s and scans every {ScanEvery.TotalMilliseconds} ms for {Window.TotalSeconds}s", $"the run is {status}");

        status.ShouldBe(RunStatus.Running, "a run that is beating is alive on every clock");
    }

    /// <summary>
    /// The control: a run an older node wrote, with no heartbeat at all, is closed by the same scan once it started longer ago
    /// than the threshold, so the scenario above can tell a closed run from one nobody looked at. A run this version starts
    /// is marked when it starts (the scenarios below), so the row has to be written by hand, as the older node wrote it.
    /// </summary>
    [Fact]
    public async Task A_run_an_older_node_wrote_without_a_heartbeat_is_closed_by_the_scan_the_other_scenarios_run()
    {
        var (runs, leases, jobs) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId));
        await ClearHeartbeatAsync(runId, DateTimeOffset.UtcNow - Threshold - TimeSpan.FromMinutes(1));

        var status = await RunBothNodesAsync(runs, leases, jobs, new RunCancellationRegistry(), TimeSpan.Zero, TimeSpan.Zero, runId);

        Describe("a row with no mark, started 6 minutes ago, scanning node on time", $"the run is {status}");

        status.ShouldBe(RunStatus.Failed, "a row with no mark is aged by when it started, so that a run an older node left behind does not stay Running for ever");
    }

    /// <summary>
    /// A run that is running is marked by the database server's clock the moment it starts, so a run whose node reads a clock
    /// behind the database's is not closed by the first scan before its first beat. Aged by <c>started_at</c>, which is the
    /// node's own clock, a node an hour slow would have its new run read as an hour old.
    /// </summary>
    /// <param name="nodeBehindSeconds">How far the clock of the node that starts the run is behind the database server's.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    [InlineData(600)]
    [InlineData(3600)]
    public async Task A_run_is_not_closed_before_its_first_beat_because_its_node_reads_a_clock_behind_the_database(int nodeBehindSeconds)
    {
        var (runs, _, _) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId) with { StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(nodeBehindSeconds) });

        var claimed = await runs.ClaimOrphanedRunsAsync(Threshold, 10);

        Describe($"a run started by a node {nodeBehindSeconds}s behind, no beat yet, then a scan with a threshold of 5 minutes", $"{claimed.Count} run(s) closed");

        claimed.ShouldBeEmpty("the run was marked when it started, and the mark is the database server's clock");
        (await runs.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Running);
    }

    /// <summary>
    /// The control of the scenario above: the mark a run starts with ages like any other. A run that starts and never beats is
    /// closed once the threshold has passed since the start, so the first mark is not a way to keep a run alive for ever.
    /// </summary>
    [Fact]
    public async Task A_run_that_starts_and_never_beats_is_closed_once_the_threshold_has_passed_since_it_started()
    {
        var (runs, _, _) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId));

        // The mark is older than the threshold from the next statement on; the wait is the time the threshold has to pass.
        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken); // delay: product

        var claimed = await runs.ClaimOrphanedRunsAsync(TimeSpan.FromMilliseconds(200), 10);

        Describe("a run that started 400 ms ago and never beat, then a scan with a threshold of 200 ms", $"{claimed.Count} run(s) closed");

        claimed.Select(run => run.Id).ShouldBe([runId]);
    }

    /// <summary>
    /// A run that was queued has no mark (the job queue owns it, and a sweep does not touch a queued row that never beat). It
    /// is marked when it starts running, the second start of the same run, and by the database server's clock like any start.
    /// </summary>
    [Fact]
    public async Task A_queued_run_is_not_marked_until_it_starts_running_and_is_then_marked_when_it_does()
    {
        var (runs, _, _) = await OpenAsync();
        var runId = TraconId.NewId();
        var nodeBehind = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);

        await runs.StartRunAsync(TestData.Run(runId) with { Status = RunStatus.Queued, StartedAt = nodeBehind });

        (await runs.ClaimOrphanedRunsAsync(Threshold, 10)).ShouldBeEmpty("a queued run that never beat is not swept");

        await runs.StartRunAsync(TestData.Run(runId) with { Status = RunStatus.Running, StartedAt = nodeBehind });

        var claimed = await runs.ClaimOrphanedRunsAsync(Threshold, 10);

        Describe("queued, then started running by a node 10 minutes behind, then a scan with a threshold of 5 minutes", $"{claimed.Count} run(s) closed");

        claimed.ShouldBeEmpty("the run was marked when it started running, not aged by the node's own start");
        (await runs.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Running);
    }

    /// <summary>
    /// A start of a run that was already marked leaves the mark as it was: a second start (the job worker running a run the
    /// HTTP request queued, or the same run started again) does not make a run that stopped beating look alive.
    /// </summary>
    [Fact]
    public async Task A_second_start_of_a_run_leaves_the_mark_it_already_has()
    {
        var (runs, _, _) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId));

        // The mark is stamped by the database server's clock and cannot be backdated; the claim below reads the same
        // clock, so only the server's own time passing can age it past the threshold.
        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken); // delay: product

        await runs.StartRunAsync(TestData.Run(runId));

        var claimed = await runs.ClaimOrphanedRunsAsync(TimeSpan.FromMilliseconds(200), 10);

        Describe("a run marked 400 ms ago and started again, then a scan with a threshold of 200 ms", $"{claimed.Count} run(s) closed");

        claimed.Select(run => run.Id).ShouldBe([runId], "the second start did not mark the run again");
    }

    /// <summary>
    /// The mark is the database server's clock, not the clock of the node that writes it: a heartbeat written through a store
    /// whose node reads a clock an hour ahead is still an ordinary mark, so a run that stops beating is closed once the
    /// threshold has passed. A mark taken from the node's clock would sit an hour in the future and keep the run alive for an
    /// hour. This is the store on its own, with no node between the caller and the statement.
    /// </summary>
    [Fact]
    public async Task A_heartbeat_written_through_a_store_whose_node_reads_a_clock_an_hour_ahead_is_still_aged_by_the_database()
    {
        var (runs, _, _) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId));
        await OpenRunStore(ManualClock.OverSystemClock(TimeSpan.FromHours(1))).TouchHeartbeatAsync([runId]);

        // The mark is older than the threshold from the next statement on; the wait is the time the threshold has to pass.
        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken); // delay: product

        var claimed = await runs.ClaimOrphanedRunsAsync(TimeSpan.FromMilliseconds(200), 10);

        Describe("one beat written by a node an hour ahead, then a scan 400 ms later with a threshold of 200 ms", $"{claimed.Count} run(s) closed");

        claimed.Select(run => run.Id).ShouldBe([runId], "the mark is the database server's clock, so the clock of the node that wrote it cannot keep the run alive");
    }

    /// <summary>
    /// A threshold of zero or less is refused by the store: zero would count a mark older than the database server's clock
    /// at the call and a negative value every mark, and so close a run that was marked a moment ago while it is beating.
    /// </summary>
    /// <param name="staleAfterSeconds">The threshold, in seconds.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public async Task A_threshold_of_zero_or_less_is_refused_and_closes_no_run(int staleAfterSeconds)
    {
        var (runs, _, _) = await OpenAsync();
        var runId = TraconId.NewId();

        await runs.StartRunAsync(TestData.Run(runId) with { StartedAt = DateTimeOffset.UtcNow });
        await runs.TouchHeartbeatAsync([runId]);

        // The mark is older than the database server's clock from the next statement on; the wait only covers a clock tick.
        await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken); // delay: product

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await runs.ClaimOrphanedRunsAsync(TimeSpan.FromSeconds(staleAfterSeconds), 10));

        (await runs.GetRunAsync(runId))!.Status.ShouldBe(RunStatus.Running);
    }

    private async Task<RunStatus> RunBothNodesAsync(
        IRunStore runs,
        ISingletonLeaseStore leases,
        IJobStore jobs,
        RunCancellationRegistry registry,
        TimeSpan ahead,
        TimeSpan writerOffset,
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

        // The node that scans reads its clock ahead in its store too: the store decides what is old.
        var scanClock = ManualClock.OverSystemClock(ahead);

        var writer = new RunHeartbeatWriter(runs, registry, options, gate, ManualClock.OverSystemClock(writerOffset), NullLogger<RunHeartbeatWriter>.Instance);
        var sweeper = new RunReconciliationService(
            OpenRunStore(scanClock),
            leases,
            jobs,
            new EmptyToolRegistry(),
            options,
            new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions()),
            new StaticOptionsMonitor<TraconRunContinuationOptions>(new TraconRunContinuationOptions()),
            gate,
            scanClock,
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
