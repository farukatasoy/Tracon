using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Workflows.UnitTests.Fakes;

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
        var failed = 0;
        var recovered = false;
        var recoveredAt = 0;

        // The inbox fails every scan until the test lets it answer. The first scan that gets an answer records its position
        // among the scans, and the position is the claim: a loop that scanned without a tick (a hot spin after a failure)
        // pushes the scan of the recovery tick further down the list.
        plan.Fail = call =>
        {
            if (!string.Equals(call.Method, nameof(IWorkflowRequestStore.ExpireAsync), StringComparison.Ordinal))
            {
                return null;
            }

            if (Volatile.Read(ref recovered))
            {
                Interlocked.CompareExchange(ref recoveredAt, call.MethodIndex, 0);

                return null;
            }

            Interlocked.Increment(ref failed);

            return new IOException("the inbox is unreachable");
        };

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the loop to arm its timer");

        for (var tick = 1; tick <= 3; tick++)
        {
            clock.Advance(Interval);
            await WaitUntil.TrueAsync(
                () => Volatile.Read(ref failed) >= tick,
                $"the loop to scan on tick {tick}");
        }

        Volatile.Write(ref recovered, true);
        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => Volatile.Read(ref recoveredAt) != 0,
            "the loop to scan the recovered inbox");

        // The loop is sequential, so a scan it made without a tick came before the scan of the recovery tick and moved it
        // down the list: the first scan that was answered is the one after the three ticks.
        recoveredAt.ShouldBe(4, "the loop scanned without a tick");

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

    [Fact]
    public async Task Options_that_cannot_be_read_skip_the_scan_and_the_loop_lives_on()
    {
        // The scan reads its options through a monitor, and a monitor can throw on a read: a host that registers a
        // change token source of its own for these options (Tracon registers none, and supports no reload) and then
        // reloads a value the validation refuses reaches this path. The scan read its options at the top of the tick,
        // outside any try, so it has to skip the tick and keep the loop alive.
        var options = new SwitchableOptions<TraconWorkflowOptions>(new TraconWorkflowOptions { RequestExpirationScanInterval = Interval });
        var capture = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(capture));

        var (service, plan, clock) = Build(options, loggerFactory.CreateLogger<WorkflowRequestExpirationService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the loop to arm its timer");

        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)) >= 1,
            "the loop to scan while the value is valid");

        options.Failure = new OptionsValidationException(
            Options.DefaultName,
            typeof(TraconWorkflowOptions),
            ["TraconWorkflowOptions.RequestExpirationScanInterval must be at least one millisecond."]);

        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => capture.Entries.Any(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("expiration", StringComparison.OrdinalIgnoreCase)),
            "the loop to say it skipped a scan",
            TimeSpan.FromSeconds(5));

        service.ExecuteTask!.IsCompleted.ShouldBeFalse("the loop ended when its options could not be read");
        plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)).ShouldBe(1, "a scan ran on options that could not be read");

        options.Failure = null;

        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)) >= 2,
            "the loop to scan again once the value is valid");

        await service.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    private static (WorkflowRequestExpirationService Service, FaultPlan Plan, ManualClock Clock) Build(
        IOptionsMonitor<TraconWorkflowOptions>? options = null,
        ILogger<WorkflowRequestExpirationService>? logger = null)
    {
        var clock = new ManualClock();
        var plan = new FaultPlan();

        var service = new WorkflowRequestExpirationService(
            FaultProxy<IWorkflowRequestStore>.Create(new InMemoryWorkflowRequestStore(), plan),
            new InMemoryRunStore(),
            new InMemorySingletonLeaseStore(),
            options ?? new StaticOptions<TraconWorkflowOptions>(new TraconWorkflowOptions { RequestExpirationScanInterval = Interval }),
            new StaticOptions<SingletonExecutionOptions>(new SingletonExecutionOptions()),
            new SchemaReadyGate([]),
            clock,
            logger ?? NullLogger<WorkflowRequestExpirationService>.Instance,
            new DefaultRunErrorClassifier());

        return (service, plan, clock);
    }

    private sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>A monitor whose reads throw while <see cref="Failure"/> is set, as a monitor does for a value that fails validation.</summary>
    private sealed class SwitchableOptions<T>(T value) : IOptionsMonitor<T>
    {
        public Exception? Failure { get; set; }

        public T CurrentValue => Failure is null ? value : throw Failure;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
