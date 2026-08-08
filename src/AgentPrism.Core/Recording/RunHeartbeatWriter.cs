using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Bu surecte suren calistirmalarin "hala buradayim" isaretini araliklarla
/// topluca yazan arka plan servisi (Faz 54).
/// </summary>
/// <remarks>
/// <para>
/// Calistirma basina degil, TUR basina bir sorgu atar:
/// <see cref="IRunCancellationRegistry.ActiveRunIds"/>'in o anki goruntusunu
/// alir ve <see cref="IRunStore.TouchHeartbeatAsync"/>'i BIR kez cagirir. Bu,
/// N suren calistirma icin N ayri yazma yerine sicak yola hicbir sey
/// eklemeyen tek bir toplu isaretlemedir (bkz.
/// <c>docs/54-OKSUZ-CALISTIRMA-UZLASTIRMASI.md</c>, Acik Soru 1).
/// </para>
/// <para>
/// <see cref="RunReconciliationOptions.Enabled"/> <see langword="false"/>
/// (varsayilan) iken hicbir isaret yazilmaz -- uzlastirici zaten calismiyorsa
/// isaret tutmanin bir anlami yoktur (K1).
/// </para>
/// </remarks>
internal sealed class RunHeartbeatWriter(
    IRunStore runStore,
    IRunCancellationRegistry cancellationRegistry,
    IOptionsMonitor<RunReconciliationOptions> optionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<RunHeartbeatWriter>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
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

        using var timer = new PeriodicTimer(optionsMonitor.CurrentValue.HeartbeatInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal kapanma.
        }
    }

    private async Task TickAsync(CancellationToken stoppingToken)
    {
        var activeRunIds = cancellationRegistry.ActiveRunIds;

        if (activeRunIds.Count == 0)
        {
            return;
        }

        try
        {
            await runStore.TouchHeartbeatAsync(activeRunIds, _clock.GetUtcNow(), stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Gozlemlenebilirlik islevselligi bozmaz: bir turun basarisiz
            // olmasi suren calistirmalari etkilemez, bir sonraki turda
            // yeniden denenir.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Calistirma heartbeat turu basarisiz oldu.");
            }
        }
    }
}
