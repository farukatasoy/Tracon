using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Ayarli ise model saglayicilarinin saglik durumunu duzenli araliklarla tazeler.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentPrismHealthOptions.BackgroundInterval"/> <see langword="null"/>
/// ise (varsayilan) bu servis hicbir zamanlayici kurmadan hemen doner: bosta duran
/// bir kurulum saglayiciya duzenli istek atmaz. Gerekce:
/// <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, acik soru 3.
/// </para>
/// <para>
/// Bir denetim hatasi bu servisi <strong>durdurmaz</strong> — gozlemlenebilirlik
/// islevselligi bozmaz kurali (bkz. <c>CLAUDE.md</c>). Hata loglanir, bir sonraki
/// tur normal sekilde calisir.
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

        // Tek yurutucu secimi (Faz 42): kapaliysa (varsayilan) guard.IsHeld
        // daima true'dur ve RunAsync depoya hicbir sorgu atmadan hemen doner.
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
            // Normal kapanma.
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
                logger.LogWarning(exception, "Arka plan model saglayici saglik denetimi basarisiz oldu.");
            }
        }
    }
}
