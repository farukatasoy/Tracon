using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Suresi dolmus bekleyen onay isteklerini araliklarla <see cref="ApprovalStatus.Expired"/>
/// olarak kapatan ve karsilik gelen calistirmalari <see cref="RunStatus.Failed"/>'e
/// dusuren arka plan servisi (Faz 55).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RunReconciliationService"/> ile AYNI desen: <see cref="AgentPrismApprovalOptions.ExpirationEnabled"/>
/// acikken kume genelinde yalniz BIR ornek tarama yapar (<see cref="SingletonGuard"/>,
/// Faz 42'nin ayni deseni).
/// </para>
/// <para>
/// 🚨 Kapatilan her istek icin karsilik gelen <c>runs</c> satiri da
/// <see cref="RunStatus.Failed"/>'e kapatilir: aksi halde istemci
/// <c>GET /api/runs/{id}</c> ile sonsuza dek <see cref="RunStatus.AwaitingApproval"/>
/// gorurdu. <c>IRunStore.CompleteRunAsync</c>'in onceki durumu denetlemeyen
/// UPSERT olmayan davranisi buna izin verir (K-355 yalniz BEKLENEN kiraciyi
/// dogrular, onceki durumu degil).
/// </para>
/// </remarks>
internal sealed class ApprovalExpirationService(
    IPendingApprovalStore approvalStore,
    IRunStore runStore,
    ISingletonLeaseStore leaseStore,
    IOptionsMonitor<AgentPrismApprovalOptions> optionsMonitor,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<ApprovalExpirationService>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.ExpirationEnabled)
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

        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "approval-expiration", logger);
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
        var now = _clock.GetUtcNow();

        try
        {
            var expired = await approvalStore.ExpireAsync(now, options.MaxPerScan, stoppingToken).ConfigureAwait(false);

            foreach (var approval in expired)
            {
                await FailRunAsync(approval, now, stoppingToken).ConfigureAwait(false);
            }

            if (expired.Count > 0 && logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("{Count} onay istegi suresi dolarak kapatildi.", expired.Count);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Tarama asla oldurmemeli: bir hata sonraki turda yeniden denenir.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Onay sure sonu taramasi basarisiz oldu.");
            }
        }
    }

    private async ValueTask FailRunAsync(PendingApproval approval, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = approval.RunId,
                    Status = RunStatus.Failed,
                    CompletedAt = now,
                    Error = new RunError
                    {
                        Type = "ApprovalExpired",
                        Message = $"'{approval.ToolName}' tool'u icin onay istegi " +
                                  $"{approval.ExpiresAt:O} tarihinde suresi dolarak kapatildi.",
                    },
                    TenantId = approval.TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Suresi dolan onay istegine ait calistirma Failed'e kapatilamadi: {RunId}.",
                    approval.RunId);
            }
        }
    }
}
