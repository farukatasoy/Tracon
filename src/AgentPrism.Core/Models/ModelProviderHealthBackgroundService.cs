using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Refreshes model provider health status at regular intervals when configured.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentPrismHealthOptions.BackgroundInterval"/> <see langword="null"/>
/// is <see langword="null"/> by default. The service immediately returns without
/// setting a timer, so an idle deployment does not send regular requests to a provider.
/// Rationale: <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, open question 3.
/// </para>
/// <para>
/// A check error does <strong>not</strong> stop this service. This follows the rule
/// that observability does not break functionality. See <c>CLAUDE.md</c>. The error
/// is logged and the next cycle runs normally.
/// </para>
/// </remarks>
internal sealed class ModelProviderHealthBackgroundService(
    ModelProviderHealthCache cache,
    IOptionsMonitor<AgentPrismOptions> optionsMonitor,
    ISingletonLeaseStore leaseStore,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    ILogger<ModelProviderHealthBackgroundService>? logger = null) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (optionsMonitor.CurrentValue.Health.BackgroundInterval is not { } interval || interval <= TimeSpan.Zero)
        {
            return;
        }

        // Singleton execution selection (Phase 42): when disabled by default,
        // guard.IsHeld is always true and RunAsync returns without querying the store.
        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "model-provider-health", logger);
        var guardTask = guard.RunAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (guard.IsHeld)
                {
                    await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            await guardTask.ConfigureAwait(false);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cache.GetAllAsync(refresh: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "The background model provider health check failed.");
            }
        }
    }
}
