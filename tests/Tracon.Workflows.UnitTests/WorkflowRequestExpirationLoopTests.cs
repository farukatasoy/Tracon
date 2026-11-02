using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The timer loop of <see cref="WorkflowRequestExpirationService"/> when the inbox it scans fails or never
/// answers: the loop survives, calls the inbox again only on the next tick, and ends in bounded time.
/// </summary>
/// <remarks>
/// The loop runs on a manual clock: a tick happens when the test advances the clock, so a call count above the
/// tick count is a loop that retried by itself. One scan is covered by
/// <see cref="WorkflowRequestExpirationServiceTests"/>.
/// </remarks>
public sealed class WorkflowRequestExpirationLoopTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task A_failing_inbox_does_not_end_the_loop_and_is_asked_again_only_on_the_next_tick()
    {
        var (service, plan, clock) = Build();
        plan.Fail = static call => string.Equals(call.Method, nameof(IWorkflowRequestStore.ExpireAsync), StringComparison.Ordinal)
            ? new IOException("the inbox is unreachable")
            : null;

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the loop to arm its timer");

        for (var tick = 1; tick <= 3; tick++)
        {
            clock.Advance(Interval);
            await WaitUntil.TrueAsync(
                () => plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)) >= tick,
                $"the loop to scan on tick {tick}");
        }

        await Task.Delay(150, TestContext.Current.CancellationToken); // delay: negative
        plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)).ShouldBe(3, "the loop scanned without a tick");

        plan.Fail = null;
        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)) >= 4,
            "the loop to scan the recovered inbox");

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_scan_that_never_answers_does_not_keep_the_host_from_stopping()
    {
        var (service, plan, clock) = Build();
        plan.Hang = static call => string.Equals(call.Method, nameof(IWorkflowRequestStore.ExpireAsync), StringComparison.Ordinal);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the loop to arm its timer");

        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)) >= 1,
            "the loop to reach the scan that hangs");

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static (WorkflowRequestExpirationService Service, FaultPlan Plan, ManualClock Clock) Build()
    {
        var clock = new ManualClock();
        var plan = new FaultPlan();

        var service = new WorkflowRequestExpirationService(
            FaultProxy<IWorkflowRequestStore>.Create(new InMemoryWorkflowRequestStore(), plan),
            new InMemoryRunStore(),
            new InMemorySingletonLeaseStore(),
            new StaticOptions<TraconWorkflowOptions>(new TraconWorkflowOptions { RequestExpirationScanInterval = Interval }),
            new StaticOptions<SingletonExecutionOptions>(new SingletonExecutionOptions()),
            new SchemaReadyGate([]),
            clock,
            NullLogger<WorkflowRequestExpirationService>.Instance,
            new DefaultRunErrorClassifier());

        return (service, plan, clock);
    }

    private sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
