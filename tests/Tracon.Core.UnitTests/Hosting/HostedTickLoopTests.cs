using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// What every background loop of the core package does when the call it makes on each tick fails or
/// never answers: the loop outlives the failure, makes the call again only when its timer fires (no hot
/// spin), and ends in bounded time when the host stops.
/// </summary>
/// <remarks>
/// <para>
/// The loops run on a manual clock. A tick happens when the test advances the clock, so the number of
/// calls the store sees is exactly the number of ticks the test gave, and a loop that retried on its own
/// would show as more calls than ticks. No call count depends on how fast the machine is.
/// </para>
/// <para>
/// The loops that live in other packages (workflow request expiry, MCP discovery) have the same test in
/// their own test projects; the inventory test in the functional test project names the file for each.
/// </para>
/// </remarks>
public sealed class HostedTickLoopTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopBound = TimeSpan.FromSeconds(10);

    public static TheoryData<string> Loops =>
    [
        nameof(RunHeartbeatWriter),
        nameof(RunReconciliationService),
        nameof(CanaryEvaluationService),
        nameof(ApprovalExpirationService),
        nameof(ModelProviderHealthBackgroundService),
        nameof(JobQueueDepthObserver),
        nameof(QuotaUsageObserver),
    ];

    [Theory]
    [MemberData(nameof(Loops))]
    public async Task A_failing_call_does_not_end_the_loop_and_is_made_again_only_on_the_next_tick(string loopName)
    {
        using var loop = Build(loopName);
        loop.Plan.Fail = call => string.Equals(call.Method, loop.Call, StringComparison.Ordinal)
            ? new IOException("the store is unreachable")
            : null;

        await loop.StartAsync();

        for (var tick = 1; tick <= 3; tick++)
        {
            loop.Clock.Advance(Interval);
            await WaitUntil.TrueAsync(
                () => loop.Plan.CountOf(loop.Call) >= loop.StartCalls + tick,
                $"{loopName} to make its call for tick {tick}");
        }

        // A hot spin shows here: nothing advanced the clock, so no call may follow the third one.
        await Task.Delay(150, TestContext.Current.CancellationToken); // delay: negative
        loop.Plan.CountOf(loop.Call).ShouldBe(loop.StartCalls + 3, $"{loopName} called the store without a tick");

        // The loop is alive: once the store answers, the next tick reaches it and nothing is left half-done.
        loop.Plan.Fail = null;
        loop.Clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => loop.Plan.CountOf(loop.Call) >= loop.StartCalls + 4,
            $"{loopName} to call the recovered store");

        await StopAsync(loop);
    }

    [Theory]
    [MemberData(nameof(Loops))]
    public async Task A_call_that_never_answers_does_not_keep_the_host_from_stopping(string loopName)
    {
        using var loop = Build(loopName);
        loop.Plan.Hang = call => string.Equals(call.Method, loop.Call, StringComparison.Ordinal);

        await loop.StartAsync();
        loop.Clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => loop.Plan.CountOf(loop.Call) >= 1,
            $"{loopName} to reach the call that hangs");

        await StopAsync(loop);
    }

    [Fact]
    public async Task A_provider_whose_health_check_fails_does_not_keep_the_providers_after_it_from_being_checked()
    {
        var clock = new ManualClock();
        var failing = new CheckedProvider("a-failing", new FaultPlan { Fail = static _ => new IOException("unreachable") });
        var healthy = new CheckedProvider("b-healthy", new FaultPlan());
        var options = new StaticOptionsMonitor<TraconOptions>(new TraconOptions
        {
            Health = new TraconHealthOptions { BackgroundInterval = Interval },
        });

        var service = new ModelProviderHealthBackgroundService(
            new ModelProviderHealthCache([failing, healthy], options, timeProvider: clock),
            options,
            new InMemorySingletonLeaseStore(),
            new StaticOptionsMonitor<SingletonExecutionOptions>(new SingletonExecutionOptions()),
            NullLogger<ModelProviderHealthBackgroundService>.Instance,
            clock);

        using var stopping = new CancellationTokenSource(TimeSpan.FromMinutes(2)); // cancel: bound

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the health loop to arm its timer");

        for (var tick = 1; tick <= 2; tick++)
        {
            clock.Advance(Interval);
            await WaitUntil.TrueAsync(
                () => healthy.Plan.CountOf(nameof(IModelProviderHealthCheck.CheckHealthAsync)) >= tick,
                $"the healthy provider to be checked on tick {tick}");
        }

        failing.Plan.CountOf(nameof(IModelProviderHealthCheck.CheckHealthAsync)).ShouldBeGreaterThanOrEqualTo(2);

        await service.StopAsync(stopping.Token).WaitAsync(StopBound, TestContext.Current.CancellationToken);
    }

    private static async Task StopAsync(Loop loop)
    {
        // A loop that does not end in bounded time hangs the host's shutdown; 10 seconds is a failure bound,
        // not a budget: an honest loop stops in milliseconds.
        await loop.Service.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(StopBound, TestContext.Current.CancellationToken);
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
            case nameof(RunHeartbeatWriter):
                {
                    var registry = new RunCancellationRegistry();
                    var source = new CancellationTokenSource();
                    disposables.Add(source);
                    disposables.Add(registry.Register(TraconId.NewId(), TraconId.NewId(), "default", source));

                    return new Loop(
                        new RunHeartbeatWriter(
                            FaultProxy<IRunStore>.Create(new InMemoryRunStore(), plan),
                            registry,
                            new StaticOptionsMonitor<RunReconciliationOptions>(Reconciliation()),
                            gate,
                            clock,
                            NullLogger<RunHeartbeatWriter>.Instance),
                        clock,
                        plan,
                        nameof(IRunStore.TouchHeartbeatAsync),
                        startCalls: 0,
                        disposables);
                }

            case nameof(RunReconciliationService):
                return new Loop(
                    new RunReconciliationService(
                        FaultProxy<IRunStore>.Create(new InMemoryRunStore(), plan),
                        new InMemorySingletonLeaseStore(),
                        new InMemoryJobStore(),
                        TestData.Registry(),
                        new StaticOptionsMonitor<RunReconciliationOptions>(Reconciliation()),
                        singleton,
                        new StaticOptionsMonitor<TraconRunContinuationOptions>(new TraconRunContinuationOptions()),
                        gate,
                        clock,
                        NullLogger<RunReconciliationService>.Instance),
                    clock,
                    plan,
                    nameof(IRunStore.ClaimOrphanedRunsAsync),
                    startCalls: 0,
                    disposables);

            case nameof(CanaryEvaluationService):
                return new Loop(
                    new CanaryEvaluationService(
                        FaultProxy<IExperimentStore>.Create(new InMemoryExperimentStore(), plan),
                        new InMemoryRunStore(),
                        new InMemoryAuditLog(),
                        new InMemorySingletonLeaseStore(),
                        metrics: null,
                        new StaticOptionsMonitor<CanaryOptions>(new CanaryOptions { AutoRollbackEnabled = true, ScanInterval = Interval }),
                        singleton,
                        gate,
                        clock,
                        NullLogger<CanaryEvaluationService>.Instance),
                    clock,
                    plan,
                    nameof(IExperimentStore.ListRunningWithCanaryAsync),
                    startCalls: 0,
                    disposables);

            case nameof(ApprovalExpirationService):
                return new Loop(
                    new ApprovalExpirationService(
                        FaultProxy<IPendingApprovalStore>.Create(new InMemoryPendingApprovalStore(), plan),
                        new InMemoryRunStore(),
                        new InMemorySingletonLeaseStore(),
                        new StaticOptionsMonitor<TraconApprovalOptions>(new TraconApprovalOptions { ScanInterval = Interval }),
                        singleton,
                        gate,
                        clock,
                        NullLogger<ApprovalExpirationService>.Instance),
                    clock,
                    plan,
                    nameof(IPendingApprovalStore.ExpireAsync),
                    startCalls: 0,
                    disposables);

            case nameof(ModelProviderHealthBackgroundService):
                {
                    var provider = new CheckedProvider("only", plan);
                    var options = new StaticOptionsMonitor<TraconOptions>(new TraconOptions
                    {
                        Health = new TraconHealthOptions { BackgroundInterval = Interval },
                    });

                    return new Loop(
                        new ModelProviderHealthBackgroundService(
                            new ModelProviderHealthCache([provider], options, timeProvider: clock),
                            options,
                            new InMemorySingletonLeaseStore(),
                            singleton,
                            NullLogger<ModelProviderHealthBackgroundService>.Instance,
                            clock),
                        clock,
                        plan,
                        nameof(IModelProviderHealthCheck.CheckHealthAsync),
                        startCalls: 0,
                        disposables);
                }

            case nameof(JobQueueDepthObserver):
                {
                    var meterFactory = new TestMeterFactory();
                    disposables.Add(meterFactory);

                    return new Loop(
                        new JobQueueDepthObserver(
                            FaultProxy<IJobStore>.Create(new InMemoryJobStore(), plan),
                            new StaticOptionsMonitor<TraconOptions>(new TraconOptions
                            {
                                Observability = new TraconObservabilityOptions
                                {
                                    EnableJobQueueDepthGauge = true,
                                    JobQueueDepthRefreshInterval = Interval,
                                },
                            }),
                            gate,
                            meterFactory,
                            timeProvider: clock,
                            logger: NullLogger<JobQueueDepthObserver>.Instance),
                        clock,
                        plan,
                        nameof(IJobStore.GetQueueDepthAsync),

                        // A gauge reads once when it starts, then once per tick.
                        startCalls: 1,
                        disposables);
                }

            case nameof(QuotaUsageObserver):
                {
                    var meterFactory = new TestMeterFactory();
                    disposables.Add(meterFactory);

                    return new Loop(
                        new QuotaUsageObserver(
                            new InMemoryQuotaStore(),
                            FaultProxy<ITenantStore>.Create(new InMemoryTenantStore(), plan),
                            new StaticOptionsMonitor<TraconOptions>(new TraconOptions
                            {
                                Observability = new TraconObservabilityOptions
                                {
                                    EnableQuotaUsageGauge = true,
                                    QuotaUsageRefreshInterval = Interval,
                                },
                            }),
                            new StaticOptionsMonitor<TraconQuotaOptions>(new TraconQuotaOptions()),
                            gate,
                            meterFactory,
                            clock,
                            NullLogger<QuotaUsageObserver>.Instance),
                        clock,
                        plan,
                        nameof(ITenantStore.ListAsync),
                        startCalls: 1,
                        disposables);
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(loopName), loopName, "unknown loop");
        }
    }

    private static RunReconciliationOptions Reconciliation()
        => new()
        {
            Enabled = true,
            ScanInterval = Interval,
            HeartbeatInterval = Interval,
            OrphanThreshold = TimeSpan.FromMinutes(5),
        };

    private sealed class Loop(
        IHostedService service,
        ManualClock clock,
        FaultPlan plan,
        string call,
        int startCalls,
        List<IDisposable> owned) : IDisposable
    {
        public IHostedService Service { get; } = service;

        public ManualClock Clock { get; } = clock;

        public FaultPlan Plan { get; } = plan;

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
            foreach (var item in owned)
            {
                item.Dispose();
            }
        }
    }

    private sealed class CheckedProvider(string name, FaultPlan plan) : IModelProvider, IModelProviderHealthCheck
    {
        public FaultPlan Plan { get; } = plan;

        public string Name { get; } = name;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public IChatClient CreateChatClient(ModelBinding binding) => throw new NotSupportedException();

        public async ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            await Plan.EnterAsync(nameof(CheckHealthAsync), cancellationToken).ConfigureAwait(false);

            return new ModelProviderHealth { ProviderName = Name, Status = ModelProviderHealthStatus.Healthy, CheckedAt = DateTimeOffset.UtcNow };
        }
    }
}
