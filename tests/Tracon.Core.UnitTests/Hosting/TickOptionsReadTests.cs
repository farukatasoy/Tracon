using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Core.UnitTests.Scheduling;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// A background loop whose options cannot be read on a tick skips that tick and keeps its timer: it does not end,
/// and the work it leaves undone is done by the first tick that can read them.
/// </summary>
/// <remarks>
/// <para>
/// A host that adds a configuration source that reloads makes the options monitor recompute on every change. A value
/// the options validation refuses makes the monitor throw <see cref="Microsoft.Extensions.Options.OptionsValidationException"/>
/// on every read until a valid value replaces it. A loop that read its options at the top of the tick, outside any
/// <c>try</c>, let that exception end its loop, and <see cref="BackgroundService"/> reports an exception from
/// <c>ExecuteAsync</c> to the host, which stops the application by default.
/// </para>
/// <para>
/// The loops run on a manual clock, so a tick happens when the test advances it, and a store call that follows an
/// unreadable tick is a loop that read its options some other way.
/// </para>
/// </remarks>
public sealed class TickOptionsReadTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FailureBound = TimeSpan.FromSeconds(5);

    public static TheoryData<string> Loops =>
    [
        nameof(RunReconciliationService),
        nameof(ApprovalExpirationService),
        nameof(JobWorkerBackgroundService),
        nameof(JobQueueDepthObserver),
    ];

    [Theory]
    [MemberData(nameof(Loops))]
    public async Task A_tick_whose_options_cannot_be_read_is_skipped_and_the_loop_lives_on(string loopName)
    {
        using var loop = Build(loopName);

        await loop.StartAsync();

        loop.Clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => loop.Plan.CountOf(loop.Call) >= loop.StartCalls + 1,
            $"{loopName} to make its call on a tick that can read its options");

        var callsBefore = loop.Plan.CountOf(loop.Call);

        loop.Options.Invalid = true;

        for (var tick = 1; tick <= 3; tick++)
        {
            var refusedBefore = loop.Options.RefusedReads;

            loop.Clock.Advance(Interval);
            await WaitUntil.TrueAsync(
                () => loop.Options.RefusedReads > refusedBefore,
                $"{loopName} to try its options on tick {tick}",
                FailureBound);
        }

        loop.Service.ExecuteTask!.IsCompleted.ShouldBeFalse($"{loopName} ended when its options could not be read");
        loop.Plan.CountOf(loop.Call).ShouldBe(callsBefore, $"{loopName} did its work on a tick that could not read its options");

        loop.Options.Invalid = false;
        loop.Clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => loop.Plan.CountOf(loop.Call) > callsBefore,
            $"{loopName} to work again once its options could be read");

        await loop.Service.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_job_leased_on_a_tick_runs_on_the_options_that_tick_read()
    {
        // The options are read once on the tick and travel with the job. A job that read them again when it started
        // was leased, and then failed to start if they became unreadable in between: nothing ran it and nothing gave it back,
        // so it stayed leased until the lease ran out.
        var clock = new ManualClock();
        var plan = new FaultPlan();
        var options = new SwitchableOptionsMonitor<TraconSchedulingOptions>(
            new TraconSchedulingOptions { MaxConcurrentJobs = 1, PollInterval = Interval });
        var store = new InMemoryJobStore();
        var handler = new CountingHandler();
        using var host = TestJobHandlerHost.For((JobHandlerKeys.AgentBatch, handler));

        // The options are readable when the tick starts and unreadable by the time the job has been leased.
        plan.OnCall = call =>
        {
            if (string.Equals(call.Method, nameof(IJobStore.LeaseAsync), StringComparison.Ordinal))
            {
                options.Invalid = true;
            }
        };

        var id = await JobAttemptProbes.EnqueueAsync(store, "counting");

        using var worker = new JobWorkerBackgroundService(
            FaultProxy<IJobStore>.Create(store, plan),
            new InMemoryJobScheduleStore(),
            host.Registry,
            host.Scopes,
            options,
            new SchemaReadyGate([]),
            new NotDraining(),
            timeProvider: clock,
            logger: NullLogger<JobWorkerBackgroundService>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the worker to arm its poll timer");

        clock.Advance(Interval);

        await handler.Called.WaitAsync(FailureBound, TestContext.Current.CancellationToken);

        var job = await JobAttemptProbes.WaitForAsync(store, id, JobStatus.Completed);

        job.Status.ShouldBe(JobStatus.Completed);

        await worker.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_continuation_option_that_cannot_be_read_skips_the_scan_before_it_closes_any_run()
    {
        // The scan closes the runs it claims before it decides, one by one, whether to continue them. An option read
        // after the claim that fails leaves every claimed run closed with no decision made, and no later scan looks at
        // those rows again. The option is therefore read before the claim, with the scan's own.
        var clock = new ManualClock();
        var plan = new FaultPlan();
        var continuation = new SwitchableOptionsMonitor<TraconRunContinuationOptions>(new TraconRunContinuationOptions());

        using var service = new RunReconciliationService(
            FaultProxy<IRunStore>.Create(new InMemoryRunStore(), plan),
            new InMemorySingletonLeaseStore(),
            new InMemoryJobStore(),
            TestData.Registry(),
            new StaticOptionsMonitor<RunReconciliationOptions>(new RunReconciliationOptions
            {
                Enabled = true,
                ScanInterval = Interval,
                HeartbeatInterval = Interval,
                OrphanThreshold = TimeSpan.FromMinutes(5),
            }),
            new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions()),
            continuation,
            new SchemaReadyGate([]),
            clock,
            NullLogger<RunReconciliationService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the scan to arm its timer");

        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IRunStore.ClaimOrphanedRunsAsync)) >= 1,
            "the scan to claim on a tick that can read its options");

        var claimsBefore = plan.CountOf(nameof(IRunStore.ClaimOrphanedRunsAsync));

        continuation.Invalid = true;
        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => continuation.RefusedReads > 0,
            "the scan to try its continuation options",
            FailureBound);

        // The refused read happens before the claim, so no claim follows it.
        await Task.Delay(100, TestContext.Current.CancellationToken); // delay: negative
        plan.CountOf(nameof(IRunStore.ClaimOrphanedRunsAsync)).ShouldBe(claimsBefore, "the scan claimed runs it could not decide about");
        service.ExecuteTask!.IsCompleted.ShouldBeFalse();

        continuation.Invalid = false;
        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IRunStore.ClaimOrphanedRunsAsync)) > claimsBefore,
            "the scan to claim again once its options could be read");

        await service.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
    }

    private static Loop Build(string loopName)
    {
        var clock = new ManualClock();
        var plan = new FaultPlan();
        var disposables = new List<IDisposable>();
        var singleton = new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions());
        var gate = new SchemaReadyGate([]);

        switch (loopName)
        {
            case nameof(RunReconciliationService):
                {
                    var options = new SwitchableOptionsMonitor<RunReconciliationOptions>(new RunReconciliationOptions
                    {
                        Enabled = true,
                        ScanInterval = Interval,
                        HeartbeatInterval = Interval,
                        OrphanThreshold = TimeSpan.FromMinutes(5),
                    });

                    return new Loop(
                        new RunReconciliationService(
                            FaultProxy<IRunStore>.Create(new InMemoryRunStore(), plan),
                            new InMemorySingletonLeaseStore(),
                            new InMemoryJobStore(),
                            TestData.Registry(),
                            options,
                            singleton,
                            new StaticOptionsMonitor<TraconRunContinuationOptions>(new TraconRunContinuationOptions()),
                            gate,
                            clock,
                            NullLogger<RunReconciliationService>.Instance),
                        clock,
                        plan,
                        options,
                        nameof(IRunStore.ClaimOrphanedRunsAsync),
                        startCalls: 0,
                        disposables);
                }

            case nameof(ApprovalExpirationService):
                {
                    var options = new SwitchableOptionsMonitor<TraconApprovalOptions>(new TraconApprovalOptions { ScanInterval = Interval });

                    return new Loop(
                        new ApprovalExpirationService(
                            FaultProxy<IPendingApprovalStore>.Create(new InMemoryPendingApprovalStore(), plan),
                            new InMemoryRunStore(),
                            new InMemorySingletonLeaseStore(),
                            options,
                            singleton,
                            gate,
                            clock,
                            NullLogger<ApprovalExpirationService>.Instance),
                        clock,
                        plan,
                        options,
                        nameof(IPendingApprovalStore.ExpireAsync),
                        startCalls: 0,
                        disposables);
                }

            case nameof(JobWorkerBackgroundService):
                {
                    var options = new SwitchableOptionsMonitor<TraconSchedulingOptions>(
                        new TraconSchedulingOptions { MaxConcurrentJobs = 1, PollInterval = Interval });
                    var host = TestJobHandlerHost.For();
                    disposables.Add(host);

                    return new Loop(
                        new JobWorkerBackgroundService(
                            FaultProxy<IJobStore>.Create(new InMemoryJobStore(), plan),
                            new InMemoryJobScheduleStore(),
                            host.Registry,
                            host.Scopes,
                            options,
                            gate,
                            new NotDraining(),
                            timeProvider: clock,
                            logger: NullLogger<JobWorkerBackgroundService>.Instance),
                        clock,
                        plan,
                        options,
                        nameof(IJobStore.LeaseAsync),
                        startCalls: 0,
                        disposables);
                }

            case nameof(JobQueueDepthObserver):
                {
                    var meterFactory = new TestMeterFactory();
                    disposables.Add(meterFactory);

                    var options = new SwitchableOptionsMonitor<TraconOptions>(new TraconOptions
                    {
                        Observability = new TraconObservabilityOptions
                        {
                            EnableJobQueueDepthGauge = true,
                            JobQueueDepthRefreshInterval = Interval,
                        },
                    });

                    return new Loop(
                        new JobQueueDepthObserver(
                            FaultProxy<IJobStore>.Create(new InMemoryJobStore(), plan),
                            options,
                            gate,
                            meterFactory,
                            timeProvider: clock,
                            logger: NullLogger<JobQueueDepthObserver>.Instance),
                        clock,
                        plan,
                        options,
                        nameof(IJobStore.GetQueueDepthAsync),

                        // A gauge reads once when it starts, then once per tick.
                        startCalls: 1,
                        disposables);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(loopName), loopName, "unknown loop");
        }
    }

    private sealed class Loop(
        BackgroundService service,
        ManualClock clock,
        FaultPlan plan,
        ISwitchableOptions options,
        string call,
        int startCalls,
        List<IDisposable> owned) : IDisposable
    {
        public BackgroundService Service { get; } = service;

        public ManualClock Clock { get; } = clock;

        public FaultPlan Plan { get; } = plan;

        public ISwitchableOptions Options { get; } = options;

        public string Call { get; } = call;

        public int StartCalls { get; } = startCalls;

        public async Task StartAsync()
        {
            await Service.StartAsync(TestContext.Current.CancellationToken);

            // The timer is armed once the loop has passed its schema gate and its start-up work.
            await WaitUntil.TrueAsync(() => Clock.Pending.Count > 0, "the loop to arm its timer");
            await WaitUntil.TrueAsync(() => Plan.CountOf(Call) >= StartCalls, "the loop to make its start-up calls");
        }

        public void Dispose()
        {
            Service.Dispose();

            foreach (var item in owned)
            {
                item.Dispose();
            }
        }
    }

    private sealed class NotDraining : ITraconDrainState
    {
        public bool IsDraining => false;
    }

    private sealed class CountingHandler : IJobHandler
    {
        private readonly TaskCompletionSource _called = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Called => _called.Task;

        public ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
        {
            _called.TrySetResult();

            return ValueTask.CompletedTask;
        }
    }
}
