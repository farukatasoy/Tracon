using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Uzun sure heartbeat vermemis <c>Running</c> calistirmalari araliklarla
/// <c>Failed</c> olarak kapatan arka plan servisi (Faz 54, F-36).
/// </summary>
/// <remarks>
/// <para>
/// Bir surec <c>agent.RunAsync</c> ortasinda cokerse (varsayilan
/// <c>MaxAttempts = 1</c> yolunda) satir <c>Running</c>'de sonsuza dek kalir
/// ve <c>RunStatistics.ErrorRate</c>'in paydasini (K-014,
/// <c>settled = CompletedRuns + FailedRuns + CanceledRuns</c>) sessizce
/// seyreltir. Bu servis o satiri bulup kapatir ve nedenini yazar.
/// </para>
/// <para>
/// <see cref="RunReconciliationOptions.Enabled"/> <see langword="false"/>
/// (varsayilan) iken hicbir SQL sorgusu atilmaz (K1). Acikken bile kume
/// genelinde yalniz BIR ornek tarama yapar -- <see cref="SingletonGuard"/>
/// <c>McpDiscoveryService</c>'in kullandigi AYNI desendir (Faz 42).
/// </para>
/// </remarks>
internal sealed class RunReconciliationService(
    IRunStore runStore,
    ISingletonLeaseStore leaseStore,
    IOptionsMonitor<RunReconciliationOptions> optionsMonitor,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<RunReconciliationService>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.Enabled)
        {
            return;
        }

        // 🚨 Ilk SQL denemesinden ONCE semanin hazir olmasini bekle (K-354).
        try
        {
            await schemaReadyGate.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Tek yurutucu secimi (Faz 42): kapaliysa (varsayilan) guard.IsHeld
        // daima true'dur ve RunAsync depoya hicbir sorgu atmadan hemen doner.
        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "run-reconciliation", logger);
        var guardTask = guard.RunAsync(stoppingToken);

        using var timer = new PeriodicTimer(optionsMonitor.CurrentValue.ScanInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                if (guard.IsHeld)
                {
                    await TickAsync(stoppingToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal kapanma.
        }
        finally
        {
            await guardTask.ConfigureAwait(false);
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;
        var staleBefore = _clock.GetUtcNow() - options.OrphanThreshold;

        try
        {
            var claimed = await runStore
                .ClaimOrphanedRunsAsync(staleBefore, options.MaxRunsPerScan, stoppingToken)
                .ConfigureAwait(false);

            if (claimed.Count > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    "{Count} oksuz calistirma kapatildi (esik: {StaleBefore:O}).",
                    claimed.Count,
                    staleBefore);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Uzlastirma turu asla oldurmemeli: bir hata sonraki turda
            // yeniden denenir.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Oksuz calistirma uzlastirma turu basarisiz oldu.");
            }
        }
    }
}
