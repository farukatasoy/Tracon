using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Waits for in-flight runs to finish before the process actually stops, and
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
/// <see cref="IRunCancellationRegistry.ActiveCount"/> until it reaches zero
/// or <see cref="TraconDrainOptions.Timeout"/> elapses. For this wait to
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
    ILogger<TraconDrainService>? logger = null) : IHostedService, ITraconDrainState
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
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
            if (optionsMonitor.CurrentValue.Enabled)
            {
                _isDraining = true;
            }
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!optionsMonitor.CurrentValue.Enabled)
        {
            return;
        }

        _isDraining = true;

        if (registry.ActiveCount == 0)
        {
            return;
        }

        var timeout = optionsMonitor.CurrentValue.Timeout;

        if (logger is not null && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Draining: waiting for {Count} in-flight run(s) to finish (timeout {Timeout}).",
                registry.ActiveCount,
                timeout);
        }

        using var timeoutSource = new CancellationTokenSource(timeout, _clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200), _clock);

            while (registry.ActiveCount > 0)
            {
                await timer.WaitForNextTickAsync(linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout, or the host's own ShutdownTimeout cut the wait short.
            // A stuck run must not block the process from stopping forever.
        }

        if (registry.ActiveCount > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning(
                "Drain timed out with {Count} run(s) still in flight; stopping anyway.",
                registry.ActiveCount);
        }
    }
}
