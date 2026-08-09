using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Kanarya kurali tanimli, calisan deneyleri araliklarla degerlendirip otomatik
/// geri alma veya kademeli trafik artirma uygulayan arka plan servisi (Faz 56).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ApprovalExpirationService"/> ile AYNI desen: kume genelinde yalniz BIR
/// ornek tarama yapar (<see cref="SingletonGuard"/>, Faz 42), ilk SQL denemesinden
/// once <see cref="SchemaReadyGate"/>'i bekler (K-354).
/// </para>
/// <para>
/// 🚨 <see cref="CanaryOptions.AutoRollbackEnabled"/> varsayilan KAPALIDIR (K1).
/// Kapaliyken <see cref="ExecuteAsync"/> hemen doner; hicbir deney taranmaz.
/// </para>
/// <para>
/// 🚨 K-089 emsali: otomatik geri alma karari <see cref="IAuditLog.WriteAsync"/> ile
/// dogrudan (dekoratorun en iyi cabayla yazan <see cref="AuditRecorder"/>'i DEGIL)
/// mutasyondan ONCE yazilir; yazma basarisiz olursa geri alma hic uygulanmaz —
/// <c>ApprovalEndpoints.DecideAsync</c> ile ayni desen.
/// </para>
/// </remarks>
internal sealed class CanaryEvaluationService(
    IExperimentStore experiments,
    IRunStore runs,
    IAuditLog auditLog,
    ISingletonLeaseStore leaseStore,
    IOptionsMonitor<CanaryOptions> optionsMonitor,
    IOptionsMonitor<SingletonExecutionOptions> singletonOptionsMonitor,
    SchemaReadyGate schemaReadyGate,
    TimeProvider? timeProvider = null,
    ILogger<CanaryEvaluationService>? logger = null) : BackgroundService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!optionsMonitor.CurrentValue.AutoRollbackEnabled)
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

        var guard = new SingletonGuard(leaseStore, singletonOptionsMonitor, "canary-evaluation", logger);
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
        IReadOnlyList<Experiment> running;

        try
        {
            running = await experiments.ListRunningWithCanaryAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Kanarya kurali tanimli calisan deneyler listelenemedi.");
            }

            return;
        }

        foreach (var experiment in running)
        {
            try
            {
                await EvaluateAsync(experiment, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Tarama asla oldurmemeli: bir deneydeki hata digerlerini engellemez,
                // sonraki turda yeniden denenir.
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "'{Name}' deneyinin kanarya degerlendirmesi basarisiz oldu.", experiment.Name);
                }
            }
        }
    }

    private async Task EvaluateAsync(Experiment experiment, CancellationToken cancellationToken)
    {
        var policy = experiment.Canary;

        if (policy is null)
        {
            // Yaris: tarama basladiktan sonra kural kaldirilmis olabilir.
            return;
        }

        var results = await runs
            .GetExperimentResultsAsync(
                new ExperimentResultsQuery { ExperimentId = experiment.Id, TenantId = experiment.TenantId },
                cancellationToken)
            .ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var evaluation = CanaryEvaluator.Evaluate(policy, results, now);

        if (evaluation.Decision == CanaryDecisionKind.RollBack)
        {
            await RollbackAsync(experiment, policy, evaluation, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (evaluation.Decision == CanaryDecisionKind.Healthy && policy.RampSteps.Count > 0)
        {
            await TryAdvanceRampAsync(experiment, policy, evaluation, now, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RollbackAsync(
        Experiment experiment,
        CanaryPolicy policy,
        CanaryEvaluation evaluation,
        CancellationToken cancellationToken)
    {
        var canaryVariant = experiment.Variants.FirstOrDefault(
            variant => string.Equals(variant.Name, policy.CanaryVariant, StringComparison.Ordinal));
        var controlVariant = experiment.Variants.FirstOrDefault(
            variant => !string.Equals(variant.Name, policy.CanaryVariant, StringComparison.Ordinal));

        if (canaryVariant is null || controlVariant is null)
        {
            return;
        }

        IReadOnlyList<ExperimentVariant> revertedVariants =
        [
            canaryVariant with { Weight = 0 },
            controlVariant with { Weight = 100 },
        ];

        var now = _clock.GetUtcNow();

        // 🚨 K-089: audit ONCE yazilir, mutasyondan SONRA degil. Yazma basarisiz
        // olursa geri alma hic uygulanmaz ("denetim izine yazilamayan bir geri
        // alma uygulanmaz").
        try
        {
            await auditLog.WriteAsync(
                new AuditEntry
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = experiment.TenantId,
                    Actor = "system:canary-evaluator",
                    Action = "experiment.auto_rollback",
                    Entity = $"experiment:{experiment.Name}",
                    Before = null,
                    After = AuditSecretFilter.Redact(
                        JsonSerializer.Serialize(
                            new CanaryRollbackAuditPayload
                            {
                                Reason = evaluation.Reason,
                                CanaryErrorRate = evaluation.Canary?.ErrorRate,
                                ControlErrorRate = evaluation.Control?.ErrorRate,
                                CanaryAverageScore = evaluation.Canary?.AverageScore,
                            },
                            AgentPrismCoreJsonContext.Default.CanaryRollbackAuditPayload)),
                    CreatedAt = now,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "'{Name}' deneyinin otomatik geri alma denetim izi yazilamadi; geri alma UYGULANMADI.",
                    experiment.Name);
            }

            return;
        }

        await experiments
            .RollbackCanaryAsync(experiment.TenantId, experiment.Name, revertedVariants, evaluation.Reason, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Kanarya agirligini bir sonraki <see cref="CanaryPolicy.RampSteps"/> degerine
    /// tasir. Bu, geri almanin AKSINE denetim izine YAZILMAZ — yalniz trafik
    /// paylasimini artiran, geri alinabilir bir islemdir (bkz. 56.2 akis semasi).
    /// </summary>
    private async Task TryAdvanceRampAsync(
        Experiment experiment,
        CanaryPolicy policy,
        CanaryEvaluation evaluation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var canaryVariant = experiment.Variants.FirstOrDefault(
            variant => string.Equals(variant.Name, policy.CanaryVariant, StringComparison.Ordinal));
        var controlVariant = experiment.Variants.FirstOrDefault(
            variant => !string.Equals(variant.Name, policy.CanaryVariant, StringComparison.Ordinal));

        if (canaryVariant is null || controlVariant is null)
        {
            return;
        }

        var nextStep = policy.RampSteps.Where(step => step > canaryVariant.Weight).OrderBy(step => step).FirstOrDefault();

        if (nextStep == 0)
        {
            // Zaten son adimda (veya adim listesi mevcut agirligi asmiyor).
            return;
        }

        // 🚨 Bir sonraki adima gecmeden once bu adimda MinSampleSize'a ulasilmis
        // olmali VE RampInterval kadar zaman gecmis olmali. Ikinci bir esik alani
        // ACILMAZ: MinSampleSize policy'nin kendisinden AYNEN alinir.
        var canarySettled = evaluation.Canary is null
            ? 0
            : evaluation.Canary.CompletedRuns + evaluation.Canary.FailedRuns + evaluation.Canary.CanceledRuns;

        if (canarySettled < policy.MinSampleSize)
        {
            return;
        }

        var since = experiment.UpdatedAt ?? experiment.StartedAt ?? now;

        if (now - since < policy.RampInterval)
        {
            return;
        }

        IReadOnlyList<ExperimentVariant> advancedVariants =
        [
            canaryVariant with { Weight = nextStep },
            controlVariant with { Weight = 100 - nextStep },
        ];

        await experiments
            .AdvanceCanaryRampAsync(experiment.TenantId, experiment.Name, advancedVariants, cancellationToken)
            .ConfigureAwait(false);
    }

}
