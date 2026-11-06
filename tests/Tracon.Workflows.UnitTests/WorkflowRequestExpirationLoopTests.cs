using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task A_reloaded_value_that_fails_validation_skips_the_scan_and_the_loop_lives_on()
    {
        // Tracon:Workflows is the one set of options that follows a configuration reload, so this is a path an operator
        // reaches by saving appsettings.json with a value the validation refuses. The monitor then throws on every read,
        // and the scan read its options at the top of the tick, outside any try.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Tracon:Workflows:RequestExpirationScanInterval"] = "00:01:00",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseWorkflows();

        await using var provider = services.BuildServiceProvider();

        var capture = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(capture));

        var (service, plan, clock) = Build(
            provider.GetRequiredService<IOptionsMonitor<TraconWorkflowOptions>>(),
            loggerFactory.CreateLogger<WorkflowRequestExpirationService>());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntil.TrueAsync(() => clock.Pending.Count > 0, "the loop to arm its timer");

        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)) >= 1,
            "the loop to scan while the value is valid");

        configuration["Tracon:Workflows:RequestExpirationScanInterval"] = "00:00:00";
        Should.Throw<AggregateException>(configuration.Reload);

        clock.Advance(Interval);
        await WaitUntil.TrueAsync(
            () => capture.Entries.Any(static entry => entry.Level == LogLevel.Warning && entry.Message.Contains("expiration", StringComparison.OrdinalIgnoreCase)),
            "the loop to say it skipped a scan",
            TimeSpan.FromSeconds(5));

        service.ExecuteTask!.IsCompleted.ShouldBeFalse("the loop ended when its options could not be read");
        plan.CountOf(nameof(IWorkflowRequestStore.ExpireAsync)).ShouldBe(1, "a scan ran on options that could not be read");

        configuration["Tracon:Workflows:RequestExpirationScanInterval"] = "00:01:00";
        configuration.Reload();

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
}
