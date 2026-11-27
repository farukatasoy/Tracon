using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Waits for in-flight runs and running jobs to finish before the process actually stops, and
/// reports <see cref="IsDraining"/> so new work is refused while waiting.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IsDraining"/> flips to <see langword="true"/> the moment
/// <see cref="IHostApplicationLifetime.ApplicationStopping"/> fires — this
/// happens synchronously, before ANY <see cref="IHostedService.StopAsync"/>
/// runs. Consumers that gate new work (the HTTP run endpoints,
/// <c>JobWorkerBackgroundService</c>) read the flag directly; they do not
/// depend on this service's own <see cref="StopAsync"/> having run yet.
/// <see cref="StopAsync"/> also sets the flag itself, as a fallback for a
/// host that never wires <see cref="IHostApplicationLifetime"/> (a bare
/// <c>ServiceCollection</c>, as tests commonly build).
/// </para>
/// <para>
/// The actual wait happens in <see cref="StopAsync"/>: it polls
/// <see cref="IRunCancellationRegistry.ActiveCount"/> and the number of jobs the worker is
/// running until both reach zero or <see cref="TraconDrainOptions.Timeout"/> elapses. A job of a
/// consumer's handler is in no run registry, so the worker's own count is what the drain waits for
/// there; a handler sees a live token while it does, and finishes its work normally. For this wait to
/// matter for job-worker-driven runs (queued agent runs, workflows), this
/// service's <c>StopAsync</c> must run BEFORE <c>JobWorkerBackgroundService</c>'s
/// own — the generic host stops <see cref="IHostedService"/> instances in
/// the REVERSE of their registration order, and this service is registered
/// LAST in <c>TraconServiceCollectionExtensions.AddTracon</c> for
/// exactly this reason.
/// </para>
/// <para>
/// While <see cref="TraconDrainOptions.Enabled"/> is <see langword="false"/>
/// (the default), <see cref="IsDraining"/> never becomes <see langword="true"/>
/// and <see cref="StopAsync"/> returns immediately — today's behavior is
/// unchanged.
/// </para>
/// </remarks>
internal sealed class TraconDrainService(
    IRunCancellationRegistry registry,
    IOptionsMonitor<TraconDrainOptions> optionsMonitor,
    IHostApplicationLifetime? lifetime = null,
    TimeProvider? timeProvider = null,
    ILogger<TraconDrainService>? logger = null,
    IJobWorkerActivity? workerActivity = null) : IHostedService, ITraconDrainState
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly TickOptions<TraconDrainOptions> _readOptions = new(optionsMonitor, logger, "drain on shutdown");
    private volatile bool _isDraining;

    /// <inheritdoc />
    public bool IsDraining => _isDraining;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // 🚨 Optional: a bare ServiceCollection (common in tests) never
        // registers IHostApplicationLifetime. StopAsync sets the flag too,
        // so a real Generic Host-hosted setup is unaffected either way.
        lifetime?.ApplicationStopping.Register(() =>
        {
            if (_readOptions.TryRead(out var options) && options.Enabled)
            {
                _isDraining = true;
            }
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Options a reload made unreadable: the host is stopping and does not know whether, or how long, to wait for
        // a run, so it does not drain. A stop that threw here would only be logged by the host.
        if (!_readOptions.TryRead(out var options) || !options.Enabled)
        {
            return;
        }

        _isDraining = true;

        if (registry.ActiveCount == 0 && RunningJobs == 0)
        {
            return;
        }

        var timeout = options.Timeout;

        if (logger is not null && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Draining: waiting for {Count} in-flight run(s) and {Jobs} running job(s) to finish (timeout {Timeout}).",
                registry.ActiveCount,
                RunningJobs,
                timeout);
        }

        using var timeoutSource = new CancellationTokenSource(timeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200), _clock);

            while (registry.ActiveCount > 0 || RunningJobs > 0)
            {
                await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout, or the host's own ShutdownTimeout cut the wait short.
            // A stuck run must not block the process from stopping forever.
        }

        if ((registry.ActiveCount > 0 || RunningJobs > 0) && logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Drain timed out with {Count} run(s) and {Jobs} job(s) still in flight; stopping anyway.",
                registry.ActiveCount,
                RunningJobs);
        }
    }

    /// <summary>Gets how many jobs the worker of this process is running; zero when it runs none or there is no worker.</summary>
    private int RunningJobs => workerActivity?.RunningJobs ?? 0;
}
