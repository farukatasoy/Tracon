using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A tick loop sits behind a <c>GatedLoop</c>: the host builds the gate, and the gate builds the loop only when the loop's
/// switch is on. A loop that is off costs its dependencies nothing; a loop that is on starts, fails and stops as the host
/// would have run it directly.
/// </summary>
/// <remarks>
/// Each case starts a real host. Whether a loop is built, and whether its failure reaches the host's
/// background-service exception behaviour, depend on the host's own start and stop sequence.
/// </remarks>
public sealed class GatedLoopHostTests
{
    [Fact]
    public async Task The_loops_that_are_off_by_default_are_never_built()
    {
        using var host = await StartAsync();

        var gates = host.Services.GetServices<IHostedService>().Where(IsGate).ToList();

        // Off by default: health, the two gauges, heartbeat, reconciliation and canary rollback.
        foreach (var off in new[]
                 {
                     typeof(GatedLoop<ModelProviderHealthBackgroundService>),
                     typeof(GatedLoop<QuotaUsageObserver>),
                     typeof(GatedLoop<JobQueueDepthObserver>),
                     typeof(GatedLoop<RunHeartbeatWriter>),
                     typeof(GatedLoop<RunReconciliationService>),
                     typeof(GatedLoop<CanaryEvaluationService>),
                 })
        {
            var gate = (BackgroundService)gates.Single(service => service.GetType() == off);

            await gate.ExecuteTask!.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
            LoopOf(gate).ShouldBeNull($"{off.GenericTypeArguments[0].Name} is off and must not be built");
        }
    }

    [Fact]
    public async Task A_loop_that_is_on_is_built_started_and_stopped_with_the_host()
    {
        using var host = await StartAsync();

        // The job worker and the approval expiration scan are on by default.
        var worker = host.Services.GetServices<IHostedService>().OfType<GatedLoop<JobWorkerBackgroundService>>().Single();
        var loop = worker.Loop.ShouldNotBeNull();

        loop.ShouldBeSameAs(host.Services.GetRequiredService<JobWorkerBackgroundService>());

        // A background service runs its work on the thread pool (the host's start does not wait for it), so the loop is
        // built when the host has started and running a moment later.
        await WaitUntil.TrueAsync(() => loop.ExecuteTask is not null, "the gate to start the loop");

        loop.ExecuteTask!.IsCompleted.ShouldBeFalse();

        await host.StopAsync(TestContext.Current.CancellationToken);

        loop.ExecuteTask.IsCompleted.ShouldBeTrue("the stop must reach the loop behind the gate");
        worker.ExecuteTask!.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public async Task An_api_only_host_does_not_build_the_job_worker()
    {
        using var host = await StartAsync(static services => services.PostConfigure<TraconSchedulingOptions>(static options => options.RunWorker = false));

        var worker = host.Services.GetServices<IHostedService>().OfType<GatedLoop<JobWorkerBackgroundService>>().Single();

        await worker.ExecuteTask!.WaitAsync(WaitUntil.DefaultTimeout, TestContext.Current.CancellationToken);
        worker.Loop.ShouldBeNull();
    }

    [Fact]
    public async Task A_loop_that_fails_stops_the_host_as_a_background_service_does()
    {
        var logs = new RecordingLoggerProvider();

        using var host = await StartAsync(static services => services.TryAddGatedLoop<FailingLoop>(), logs);

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        await WaitUntil.TrueAsync(() => lifetime.ApplicationStopping.IsCancellationRequested, "the host to stop after the loop failed");

        var gate = host.Services.GetServices<IHostedService>().OfType<GatedLoop<FailingLoop>>().Single();
        gate.ExecuteTask.ShouldNotBeNull().IsFaulted.ShouldBeTrue();
        logs.AllText.ShouldContain(FailingLoop.Message);
    }

    [Fact]
    public async Task A_switched_on_loop_whose_dependency_cannot_be_built_stops_the_start()
    {
        // The loop is built in the gate's start, not in the background: a loop that cannot be built fails the start, as a
        // hosted service that cannot be built always did, instead of dying unseen after the host has said it is ready.
        var failure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await StartAsync(static services => services.TryAddGatedLoop<UnbuildableLoop>()));

        failure.Message.ShouldContain(nameof(UnregisteredDependency));
    }

    [Fact]
    public async Task A_loop_that_ignores_its_stop_token_does_not_hold_the_host_beyond_the_shutdown_timeout()
    {
        var shutdownTimeout = TimeSpan.FromMilliseconds(100);

        using var host = await StartAsync(
            services =>
            {
                services.Configure<HostOptions>(options => options.ShutdownTimeout = shutdownTimeout);
                services.TryAddGatedLoop<StubbornLoop>();
            });

        // The loop has to be running for its stop to be the thing that is bounded.
        var gate = host.Services.GetServices<IHostedService>().OfType<GatedLoop<StubbornLoop>>().Single();
        await WaitUntil.TrueAsync(() => gate.Loop?.ExecuteTask is not null, "the gate to start the stubborn loop");

        // The shutdown timeout ends the wait for the loop, both in the loop's own stop and in the gate's: the stop is
        // bounded by the timeout and not by the loop, which never ends. A stop that waited for the loop does not return.
        await host.StopAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    private static bool IsGate(IHostedService service)
        => service.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(GatedLoop<>);

    private static object? LoopOf(BackgroundService gate)
        => gate.GetType().GetProperty(nameof(GatedLoop<FailingLoop>.Loop), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(gate);

    private static HostApplicationBuilder Builder(RecordingLoggerProvider logs, Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.AddTracon();
        configure?.Invoke(builder.Services);

        return builder;
    }

    private static async Task<IHost> StartAsync(Action<IServiceCollection>? configure = null, RecordingLoggerProvider? logs = null)
    {
        var host = Builder(logs ?? new RecordingLoggerProvider(), configure).Build();

        try
        {
            await host.StartAsync(TestContext.Current.CancellationToken);
        }
        catch
        {
            host.Dispose();

            throw;
        }

        return host;
    }

    private sealed class UnregisteredDependency;

    private sealed class UnbuildableLoop(UnregisteredDependency dependency) : BackgroundService, ISwitchedLoop
    {
        public static bool IsEnabled(IServiceProvider services) => true;

        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.FromResult(dependency);
    }

    private sealed class StubbornLoop : BackgroundService, ISwitchedLoop
    {
        public static bool IsEnabled(IServiceProvider services) => true;

        // Never completes and never looks at the token: the worst a loop can do to a stop.
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => new TaskCompletionSource().Task;
    }

    private sealed class FailingLoop : BackgroundService, ISwitchedLoop
    {
        public const string Message = "the loop failed";

        public static bool IsEnabled(IServiceProvider services) => true;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            throw new InvalidOperationException(Message);
        }
    }
}
