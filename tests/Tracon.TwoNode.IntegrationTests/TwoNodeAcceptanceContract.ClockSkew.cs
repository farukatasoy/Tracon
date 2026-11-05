using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using Tracon.TwoNode.IntegrationTests.Infrastructure;

namespace Tracon.TwoNode.IntegrationTests;

/// <summary>
/// Behaviour 8: two nodes whose wall clocks are an hour away from each other, and from the database, still agree on who
/// holds a job and on which runs are alive.
/// </summary>
/// <remarks>
/// <para>
/// A lease and a heartbeat are the database server's to date: the statements read its clock and the caller's reading of the
/// time is not used. The store contracts prove that with one store and a clock they move; this proves it with two real hosts
/// that each have a clock of their own, a worker, a heartbeat writer and a scan. Before the statements read the server's
/// clock, a lease taken on a node whose clock was behind ran out at once on a node whose clock was ahead, and a run that was
/// beating looked orphaned to a scan whose clock was ahead of the run's node by more than the threshold.
/// </para>
/// <para>
/// The nodes' timers and monotonic clocks are the system's; only the wall clock is shifted.
/// </para>
/// </remarks>
public abstract partial class TwoNodeAcceptanceContract
{
    private const string ClockSkewHandlerKey = "test.clock-skew";

    private static readonly TimeSpan HourAhead = TimeSpan.FromHours(1);

    /// <summary>
    /// The node whose clock is an hour behind takes the job and works on it for longer than its lease; the node whose clock
    /// is an hour ahead asks for work all the while. The job is run once, by the node that took it, and closed once.
    /// </summary>
    /// <remarks>
    /// The job is queued on the node that is behind: its due time is that node's clock, which is in the past for the
    /// database. The node that is ahead does not ask for a job before the first attempt has started: the other way round
    /// the lease of the node that is ahead is never over for the node that is behind, and the scenario would prove nothing.
    /// Every ask the test counts therefore comes while the other node holds the job.
    /// </remarks>
    /// <returns>A task that completes when the scenario is checked.</returns>
    [Fact]
    public async Task A_job_is_run_once_when_the_two_nodes_clocks_are_an_hour_either_side_of_the_database()
    {
        var probe = new LeaseLossProbe();

        await using var pair = await TwoNodeCluster.StartAsync(
            NewDatabase(),
            configure: static builder => builder.AddJobHandler<ClockSkewHandler>(ClockSkewHandlerKey),
            configureA: builder =>
            {
                builder.Services.AddSingleton(probe);
                probe.Install(builder.Services, leaseGate: probe.FirstAttemptRunning);
                builder.Services.AddSingleton<TimeProvider>(new SkewedTimeProvider(HourAhead));
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(200));
            },
            configureB: builder =>
            {
                builder.Services.AddSingleton(probe);
                probe.Install(builder.Services);
                builder.Services.AddSingleton<TimeProvider>(new SkewedTimeProvider(-HourAhead));
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(100));
            },
            configureServices: static services => services.Configure<TraconSchedulingOptions>(static options => options.LeaseDuration = TimeSpan.FromSeconds(2)));

        var jobs = pair.B.Services.GetRequiredService<IJobStore>();
        var job = await pair.B.Services.GetRequiredService<IJobDispatcher>().EnqueueAsync(
            new JobRequest
            {
                TenantId = "default",
                HandlerKey = ClockSkewHandlerKey,
                TargetName = "clock-skew",
                MaxAttempts = 3,
            },
            TestContext.Current.CancellationToken);

        var holder = await probe.FirstAttemptRunning.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        // The job stays with its node for longer than its lease is long (a lease of two seconds is renewed every two thirds of
        // a second), and the node that is ahead asks for a job over and over in that time: its reading of the lease is the
        // one that would have shown it over. A second execution ends a wait too: it is the failure this scenario looks for,
        // and it is better read from the assertion below than from a wait that ran out.
        await WaitUntil.TrueAsync(
            () => probe.RenewalsBy(holder) >= 4 || probe.Executions.Count > 1,
            "the lease to be renewed past the length of one lease");

        await WaitUntil.TrueAsync(
            () => probe.PollsOtherThan(holder) >= 3 || probe.Executions.Count > 1,
            "the other node to ask for a job while the lease is held");

        probe.Executions.Select(static execution => $"{execution.Owner}:{execution.Attempt}").ShouldBe([$"{holder}:1"], "the job was handed to a second node while the first held it");

        var held = (await jobs.GetAsync("default", job.Id, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        held.Status.ShouldBe(JobStatus.Running);
        held.Attempt.ShouldBe(1);
        held.LeaseOwner.ShouldBe(holder);

        probe.ReleaseHandler.TrySetResult();

        var finished = await WaitUntil.ValueAsync(
            async () => await jobs.GetAsync("default", job.Id, TestContext.Current.CancellationToken),
            static current => current?.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Pending,
            "the node that holds the job to close it");

        finished.ShouldNotBeNull().Status.ShouldBe(JobStatus.Completed);
        finished.Attempt.ShouldBe(1);
        finished.ErrorMessage.ShouldBeNull();

        await probe.WaitForAsync(holder, "complete", applied: true);
        probe.Applied().ShouldBe([$"{holder}:1:complete"]);
        probe.Executions.Select(static execution => $"{execution.Owner}:{execution.Attempt}").ShouldBe([$"{holder}:1"]);
    }

    /// <summary>
    /// A run that its node keeps alive is not closed as orphaned by a node whose clock is an hour ahead and scans all the
    /// while.
    /// </summary>
    /// <remarks>
    /// The scanning node's pass is seen by the closing of a run that nobody runs and that is older than the threshold: that run
    /// is claimed in the same statement that would claim the live one. The other node does not scan, so the scanning node is
    /// the only one that could close it.
    /// </remarks>
    /// <returns>A task that completes when the scenario is checked.</returns>
    [Fact]
    public async Task A_run_that_keeps_beating_is_not_closed_by_a_scan_on_a_node_whose_clock_is_an_hour_ahead()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var database = NewDatabase();

        await using var pair = await TwoNodeCluster.StartAsync(
            database,
            configure: builder => builder
                .AddModelProvider(hanging)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } }),
            configureA: static builder => builder.Services.Configure<RunReconciliationOptions>(static options => options.ScanInterval = TimeSpan.FromHours(1)),
            configureB: builder =>
            {
                builder.Services.AddSingleton<TimeProvider>(new SkewedTimeProvider(HourAhead));
                builder.Services.Configure<RunReconciliationOptions>(static options => options.ScanInterval = TimeSpan.FromMilliseconds(200));
            },
            reconciliation: true);

        var running = StartDirectRun(pair.A);
        var runId = await WaitForRunAsync(pair.A);
        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        try
        {
            // A run nobody runs, started ten minutes ago on the database's own time: the scan closes it, and says it has run.
            var orphanId = TraconId.NewId();

            await pair.B.Services.GetRequiredService<IRunStore>().StartRunAsync(
                new RunStartInfo
                {
                    RunId = orphanId,
                    AgentName = AgentName,
                    TenantId = "default",
                    StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
                },
                TestContext.Current.CancellationToken);

            // A run is marked when it starts, so the row of the run nobody runs is made the one an older node left.
            await database.ClearRunHeartbeatAsync(orphanId, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

            await pair.B.WaitForRunStatusAsync(orphanId, nameof(RunStatus.Failed));

            (await ReadRunStatusAsync(pair.A, runId)).ShouldBe(nameof(RunStatus.Running), "the scan on the node whose clock is ahead closed a run that was beating");

            // The run is still the node's: it stops when it is told to.
            using var response = await pair.B.Client.PostAsync(CancelUri(runId), content: null, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            await pair.A.WaitForRunStatusAsync(runId, nameof(RunStatus.Canceled));

            hanging.Calls.ShouldBe(1);
        }
        finally
        {
            await DrainAsync(running);
        }
    }

    /// <summary>
    /// A run that a node whose clock is an hour BEHIND has just started is not closed as orphaned by a scan on another node
    /// before its first beat.
    /// </summary>
    /// <remarks>
    /// Aged by the time its node wrote when it started it, the new run read as an hour old to the scan and was closed at once:
    /// measured on PostgreSQL 18, three runs of four were closed before they had beaten once. A run is marked when it starts, by
    /// the database server's clock. The scan's pass is seen by the closing of a run that nobody runs, as in the scenario above.
    /// </remarks>
    /// <returns>A task that completes when the scenario is checked.</returns>
    [Fact]
    public async Task A_run_started_by_a_node_whose_clock_is_an_hour_behind_is_not_closed_before_its_first_beat()
    {
        var hanging = new HangingModelProvider("hanging", respectsCancellation: true);
        var database = NewDatabase();

        await using var pair = await TwoNodeCluster.StartAsync(
            database,
            configure: builder => builder
                .AddModelProvider(hanging)
                .AddAgent(TestData.Definition() with { Model = new ModelBinding { Provider = hanging.Name, Model = "hanging-1" } }),
            configureA: builder =>
            {
                builder.Services.AddSingleton<TimeProvider>(new SkewedTimeProvider(-HourAhead));
                builder.Services.Configure<RunReconciliationOptions>(static options => options.ScanInterval = TimeSpan.FromHours(1));

                // The first beat is two minutes off (the threshold is five): the run has to survive the scans before it.
                builder.Services.Configure<RunReconciliationOptions>(static options => options.HeartbeatInterval = TimeSpan.FromMinutes(2));
            },
            configureB: static builder => builder.Services.Configure<RunReconciliationOptions>(static options => options.ScanInterval = TimeSpan.FromMilliseconds(200)),
            reconciliation: true);

        var running = StartDirectRun(pair.A);
        var runId = await WaitForRunAsync(pair.A);
        await hanging.Started.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        try
        {
            // A run nobody runs, started ten minutes ago: the scan closes it, which shows that the scan has passed.
            var orphanId = TraconId.NewId();

            await pair.B.Services.GetRequiredService<IRunStore>().StartRunAsync(
                new RunStartInfo
                {
                    RunId = orphanId,
                    AgentName = AgentName,
                    TenantId = "default",
                    StartedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10),
                },
                TestContext.Current.CancellationToken);

            await database.ClearRunHeartbeatAsync(orphanId, DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10));

            await pair.B.WaitForRunStatusAsync(orphanId, nameof(RunStatus.Failed));

            (await ReadRunStatusAsync(pair.A, runId)).ShouldBe(nameof(RunStatus.Running), "the scan closed a run that had not had time to beat, because the clock of the node that started it is an hour behind");
        }
        finally
        {
            await DrainAsync(running);
        }
    }

    private static async Task<string?> ReadRunStatusAsync(TraconTestHost host, Guid runId)
    {
        using var response = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}", UriKind.Relative), TestContext.Current.CancellationToken);

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("status").GetString();
    }

    /// <summary>Holds the job until the test lets it finish, and tells the probe which node took it.</summary>
    private sealed class ClockSkewHandler(LeaseLossProbe probe) : IJobHandler
    {
        public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            probe.Started(context.Job.LeaseOwner!, context.Job.Attempt);

            await probe.ReleaseHandler.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The system's timers and monotonic clock, and a wall clock shifted by a fixed amount.</summary>
    /// <param name="offset">How far the wall clock is from the system's.</param>
    private sealed class SkewedTimeProvider(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + offset;
    }
}
