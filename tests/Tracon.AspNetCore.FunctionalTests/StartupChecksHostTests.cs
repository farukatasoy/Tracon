using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The start-up checks run inside one hosted service, at the moment their stage names, and a failure does what the
/// check's kind promises: a gate stops the start before any of Tracon's loops has started, an advisory is logged and
/// the start goes on.
/// </summary>
/// <remarks>
/// Each case starts a real host: the order of <c>StartingAsync</c> and <c>StartAsync</c> across hosted services, and
/// what a failure does to the start, belong to the host and are not visible to a check built by hand.
/// </remarks>
public sealed class StartupChecksHostTests
{
    [Fact]
    public async Task A_failing_advisory_is_logged_and_the_host_starts()
    {
        var logs = new RecordingLoggerProvider();

        using var host = await StartAsync(
            logs,
            static services => services.AddSingleton<ITraconStartupCheck>(new FailingCheck(StartupCheckKind.Advisory)));

        logs.AllText.ShouldContain($"The start-up advisory {nameof(FailingCheck)} failed; the host starts anyway.");
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failing_gate_stops_the_start_before_any_loop_has_started()
    {
        var builder = Builder(new RecordingLoggerProvider());
        builder.Services.AddSingleton<ITraconStartupCheck>(new FailingCheck(StartupCheckKind.Gate));
        using var host = builder.Build();

        var failure = await Should.ThrowAsync<InvalidOperationException>(
            async () => await host.StartAsync(TestContext.Current.CancellationToken));

        failure.Message.ShouldBe(FailingCheck.Message);

        // The job worker is on by default; the gate in front of it never ran, so the worker was never built.
        var worker = host.Services.GetServices<IHostedService>().OfType<GatedLoop<JobWorkerBackgroundService>>().Single();
        worker.ExecuteTask.ShouldBeNull();
        worker.Loop.ShouldBeNull();
    }

    [Fact]
    public async Task The_starting_stage_runs_before_every_start_and_the_rest_runs_after_the_services_registered_ahead()
    {
        var order = new ConcurrentQueue<string>();

        using var host = await StartAsync(
            new RecordingLoggerProvider(),
            services =>
            {
                services.AddSingleton<ITraconStartupCheck>(new RecordingCheck(order, "start check", StartupCheckStage.Start));
                services.AddSingleton<ITraconStartupCheck>(new RecordingCheck(order, "starting check", StartupCheckStage.Starting));
            },
            ahead: services => services.AddSingleton<IHostedService>(new RecordingHostedService(order, "service registered ahead")));

        order.ShouldBe(["starting check", "service registered ahead", "start check"]);
    }

    [Fact]
    public async Task Checks_of_one_stage_run_in_the_order_they_were_registered()
    {
        var order = new ConcurrentQueue<string>();

        using var host = await StartAsync(
            new RecordingLoggerProvider(),
            services =>
            {
                services.AddSingleton<ITraconStartupCheck>(new RecordingCheck(order, "first", StartupCheckStage.Start));
                services.AddSingleton<ITraconStartupCheck>(new RecordingCheck(order, "second", StartupCheckStage.Start));
                services.AddSingleton<ITraconStartupCheck>(new RecordingCheck(order, "third", StartupCheckStage.Start));
            });

        order.ShouldBe(["first", "second", "third"]);
    }

    [Fact]
    public async Task A_service_with_no_check_starts_and_stops()
    {
        var checks = new TraconStartupChecks([], NullLogger<TraconStartupChecks>.Instance);
        var token = TestContext.Current.CancellationToken;

        var calls = new[]
        {
            checks.StartingAsync(token),
            checks.StartAsync(token),
            checks.StartedAsync(token),
            checks.StoppingAsync(token),
            checks.StopAsync(token),
            checks.StoppedAsync(token),
        };

        await Task.WhenAll(calls);

        calls.ShouldAllBe(static call => call.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task A_cancelled_start_is_not_logged_as_a_failed_advisory()
    {
        var logs = new RecordingLoggerProvider();
        var checks = new TraconStartupChecks(
            [new CancelledCheck()],
            new LoggerFactory([logs]).CreateLogger<TraconStartupChecks>());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await checks.StartAsync(cancelled.Token));

        logs.AllText.ShouldNotContain("failed; the host starts anyway");
    }

    private static HostApplicationBuilder Builder(RecordingLoggerProvider logs, Action<IServiceCollection>? ahead = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        ahead?.Invoke(builder.Services);
        builder.AddTracon();

        return builder;
    }

    private static async Task<IHost> StartAsync(
        RecordingLoggerProvider logs,
        Action<IServiceCollection> configure,
        Action<IServiceCollection>? ahead = null)
    {
        var builder = Builder(logs, ahead);
        configure(builder.Services);

        var host = builder.Build();

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

    private sealed class FailingCheck(StartupCheckKind kind) : ITraconStartupCheck
    {
        public const string Message = "the check failed";

        public StartupCheckKind Kind => kind;

        public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(Message);
    }

    private sealed class CancelledCheck : ITraconStartupCheck
    {
        public StartupCheckKind Kind => StartupCheckKind.Advisory;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCheck(ConcurrentQueue<string> order, string name, StartupCheckStage stage) : ITraconStartupCheck
    {
        public StartupCheckKind Kind => StartupCheckKind.Gate;

        public StartupCheckStage Stage => stage;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            order.Enqueue(name);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHostedService(ConcurrentQueue<string> order, string name) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            order.Enqueue(name);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
