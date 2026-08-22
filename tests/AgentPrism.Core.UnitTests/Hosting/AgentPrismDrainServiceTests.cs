using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Hosting;

/// <summary>
/// <see cref="AgentPrismDrainService"/> contract (Phase 87.6): a stuck run
/// must not block shutdown forever, and a disabled setup must not wait at all.
/// </summary>
public sealed class AgentPrismDrainServiceTests
{
    [Fact]
    public async Task Disabled_by_default_never_drains_even_with_an_active_run()
    {
        var registry = new RunCancellationRegistry();
        using var runCts = new CancellationTokenSource();
        using var registration = registry.Register(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, runCts);

        var service = new AgentPrismDrainService(
            registry,
            Options(new AgentPrismDrainOptions()),
            logger: NullLogger<AgentPrismDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        service.IsDraining.ShouldBeFalse();
    }

    [Fact]
    public async Task Enabled_with_no_active_runs_returns_immediately_and_sets_IsDraining()
    {
        var registry = new RunCancellationRegistry();

        var service = new AgentPrismDrainService(
            registry,
            Options(new AgentPrismDrainOptions { Enabled = true }),
            logger: NullLogger<AgentPrismDrainService>.Instance);

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

        var service = new AgentPrismDrainService(
            registry,
            Options(new AgentPrismDrainOptions { Enabled = true, Timeout = TimeSpan.FromSeconds(5) }),
            logger: NullLogger<AgentPrismDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var finishesShortly = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
            registration.Dispose();
            runCts.Dispose();
        }, TestContext.Current.CancellationToken);

        var stopped = service.StopAsync(TestContext.Current.CancellationToken);
        var completedFirst = await Task.WhenAny(stopped, Task.Delay(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken));

        completedFirst.ShouldBe(stopped);
        registry.ActiveCount.ShouldBe(0);
        await finishesShortly;
    }

    [Fact]
    public async Task Enabled_times_out_with_a_stuck_run_and_returns_anyway()
    {
        var registry = new RunCancellationRegistry();
        using var runCts = new CancellationTokenSource();
        using var registration = registry.Register(Guid.NewGuid(), Guid.NewGuid(), tenantId: null, runCts);

        var service = new AgentPrismDrainService(
            registry,
            Options(new AgentPrismDrainOptions { Enabled = true, Timeout = TimeSpan.FromMilliseconds(150) }),
            logger: NullLogger<AgentPrismDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        var stopped = service.StopAsync(TestContext.Current.CancellationToken);
        var completedFirst = await Task.WhenAny(stopped, Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));

        completedFirst.ShouldBe(stopped);
        registry.ActiveCount.ShouldBe(1);
    }

    [Fact]
    public async Task ApplicationStopping_sets_IsDraining_before_StopAsync_is_ever_called()
    {
        var registry = new RunCancellationRegistry();
        using var lifetime = new FakeHostApplicationLifetime();

        var service = new AgentPrismDrainService(
            registry,
            Options(new AgentPrismDrainOptions { Enabled = true }),
            lifetime,
            logger: NullLogger<AgentPrismDrainService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        service.IsDraining.ShouldBeFalse();

        lifetime.StopApplication();

        service.IsDraining.ShouldBeTrue();
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
    /// exercised by <see cref="AgentPrismDrainService"/>.
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
