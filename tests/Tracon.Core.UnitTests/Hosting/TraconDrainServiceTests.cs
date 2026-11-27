using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Hosting;

/// <summary>
/// <see cref="TraconDrainService"/> contract: a stuck run
/// must not block shutdown forever, and a disabled setup must not wait at all.
/// </summary>
public sealed class TraconDrainServiceTests
{
    [Fact]
    public async Task Disabled_by_default_never_drains_even_with_an_active_run()
    {
        var registry = new RunCancellationRegistry();
        using var runCts = new CancellationTokenSource();
        using var registration = registry.Register(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, runCts);

        var service = new TraconDrainService(
            registry,
            Options(new TraconDrainOptions()),
            logger: NullLogger<TraconDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsDraining.ShouldBeFalse();
    }

    [Fact]
    public async Task Options_a_reload_made_unreadable_neither_start_nor_stop_throws_and_nothing_drains()
    {
        var registry = new RunCancellationRegistry();
        using var runCts = new CancellationTokenSource();
        using var registration = registry.Register(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, runCts);
        var monitor = new SwitchableOptionsMonitor<TraconDrainOptions>(new TraconDrainOptions { Enabled = true });
        var service = new TraconDrainService(registry, monitor, logger: NullLogger<TraconDrainService>.Instance);

        monitor.Invalid = true;

        await Should.NotThrowAsync(async () => await service.StartAsync(TestContext.Current.CancellationToken));
        await Should.NotThrowAsync(async () => await service.StopAsync(TestContext.Current.CancellationToken));

        service.IsDraining.ShouldBeFalse("a host that cannot read the setting does not know whether to wait for a run");
    }

    [Fact]
    public async Task Enabled_with_no_active_runs_returns_immediately_and_sets_IsDraining()
    {
        var registry = new RunCancellationRegistry();

        var service = new TraconDrainService(
            registry,
            Options(new TraconDrainOptions { Enabled = true }),
            logger: NullLogger<TraconDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsDraining.ShouldBeTrue();
    }

    [Fact]
    public async Task Enabled_waits_for_the_active_run_to_finish_then_returns()
    {
        var registry = new RunCancellationRegistry();
        var runCts = new CancellationTokenSource();
        var registration = registry.Register(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, runCts);

        // The run used to end on a 150 ms timer while the test raced
        // StopAsync against a 4-second one. The drain timeout is now far longer
        // than the test's own bound, so a drain that returns only on timeout
        // fails instead of passing late.
        var service = new TraconDrainService(
            registry,
            Options(new TraconDrainOptions { Enabled = true, Timeout = TimeSpan.FromMinutes(5) }),
            logger: NullLogger<TraconDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var stopped = service.StopAsync(TestContext.Current.CancellationToken);

        stopped.IsCompleted.ShouldBeFalse("the drain waits while a run is still active");

        registration.Dispose();
        runCts.Dispose();

        await stopped.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        registry.ActiveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Enabled_stops_when_the_drain_timeout_elapses_with_a_stuck_run()
    {
        var registry = new RunCancellationRegistry();
        using var runCts = new CancellationTokenSource();
        using var registration = registry.Register(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, runCts);
        var clock = new ManualClock();

        var service = new TraconDrainService(
            registry,
            Options(new TraconDrainOptions { Enabled = true, Timeout = TimeSpan.FromMinutes(5) }),
            timeProvider: clock,
            logger: NullLogger<TraconDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var stopped = service.StopAsync(TestContext.Current.CancellationToken);
        clock.FireAll();

        await stopped.WaitAsync(TestContext.Current.CancellationToken);
        registry.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ApplicationStopping_sets_IsDraining_before_StopAsync_is_ever_called()
    {
        var registry = new RunCancellationRegistry();
        using var lifetime = new FakeHostApplicationLifetime();

        var service = new TraconDrainService(
            registry,
            Options(new TraconDrainOptions { Enabled = true }),
            lifetime,
            logger: NullLogger<TraconDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        service.IsDraining.ShouldBeFalse();

        lifetime.StopApplication();

        service.IsDraining.ShouldBeTrue();
    }

    [Fact]
    public async Task Enabled_waits_for_a_running_job_that_no_run_registry_knows_about_and_then_returns()
    {
        // A consumer's handler does its own work (an HTTP call, a render): nothing of it is in the run registry, so the
        // drain asked only how many runs were active and stopped the host under the job.
        var registry = new RunCancellationRegistry();
        var worker = NewActivity(1);
        var clock = new ManualClock();

        var service = new TraconDrainService(
            registry,
            Options(new TraconDrainOptions { Enabled = true, Timeout = TimeSpan.FromMinutes(5) }),
            timeProvider: clock,
            logger: NullLogger<TraconDrainService>.Instance,
            workerActivity: worker);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var stopped = service.StopAsync(TestContext.Current.CancellationToken);

        await WaitUntil.TrueAsync(() => clock.Pending.Contains(TimeSpan.FromMilliseconds(200)), "the drain to arm its poll");
        clock.Advance(TimeSpan.FromMilliseconds(200));

        stopped.IsCompleted.ShouldBeFalse("the drain waits while a job is still running");

        worker.Set(0);

        await WaitUntil.TrueAsync(
            () =>
            {
                clock.Advance(TimeSpan.FromMilliseconds(200));

                return stopped.IsCompleted;
            },
            "the drain to see the job end");

        await stopped.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        service.IsDraining.ShouldBeTrue();
    }

    [Fact]
    public async Task Enabled_stops_when_the_drain_timeout_elapses_with_a_stuck_job()
    {
        var registry = new RunCancellationRegistry();
        var worker = NewActivity(1);
        var clock = new ManualClock();

        var logger = new CollectingDrainLogger();

        var service = new TraconDrainService(
            registry,
            Options(new TraconDrainOptions { Enabled = true, Timeout = TimeSpan.FromMinutes(5) }),
            timeProvider: clock,
            logger: logger,
            workerActivity: worker);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var stopped = service.StopAsync(TestContext.Current.CancellationToken);
        clock.FireAll();

        await stopped.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);

        // The drain gave the job up at the timeout and said so: the job counted in the line is the one it left behind.
        logger.Warnings.ShouldHaveSingleItem().ShouldContain("1 job(s) still in flight");
    }

    [Fact]
    public async Task Enabled_with_no_run_and_no_running_job_returns_at_once()
    {
        var service = new TraconDrainService(
            new RunCancellationRegistry(),
            Options(new TraconDrainOptions { Enabled = true }),
            logger: NullLogger<TraconDrainService>.Instance,
            workerActivity: new FakeWorkerActivity());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsDraining.ShouldBeTrue();
    }

    [Fact]
    public async Task Disabled_never_waits_for_a_running_job()
    {
        var service = new TraconDrainService(
            new RunCancellationRegistry(),
            Options(new TraconDrainOptions()),
            logger: NullLogger<TraconDrainService>.Instance,
            workerActivity: NewActivity(1));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsDraining.ShouldBeFalse();
    }

    private static FakeWorkerActivity NewActivity(int runningJobs)
    {
        var activity = new FakeWorkerActivity();
        activity.Set(runningJobs);

        return activity;
    }

    private sealed class CollectingDrainLogger : ILogger<TraconDrainService>
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return [.. _warnings];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                lock (_warnings)
                {
                    _warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    private sealed class FakeWorkerActivity : IJobWorkerActivity
    {
        private int _runningJobs;

        public int RunningJobs => Volatile.Read(ref _runningJobs);

        public void Set(int runningJobs) => Volatile.Write(ref _runningJobs, runningJobs);
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) where T : class => new(value);

    /// <summary>Fake <see cref="IOptionsMonitor{T}"/> that returns a fixed value and never watches for changes.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>
    /// Minimal <see cref="IHostApplicationLifetime"/> fake: only
    /// <see cref="ApplicationStopping"/> and <see cref="StopApplication"/> are
    /// exercised by <see cref="TraconDrainService"/>.
    /// </summary>
    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopping.Cancel();

        public void Dispose() => _stopping.Dispose();
    }
}
