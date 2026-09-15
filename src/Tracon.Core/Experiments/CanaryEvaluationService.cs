using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Background service that, for experiments with a canary policy defined,
/// periodically evaluates the running experiment and applies an automatic
/// rollback or a gradual traffic ramp-up.
/// </summary>
/// <remarks>
/// <para>
/// The SAME pattern as <see cref="ApprovalExpirationService"/>: only ONE
/// instance cluster-wide performs the scan (<see cref="SingletonGuard"/>),
/// waiting for <see cref="SchemaReadyGate"/> before the first SQL
/// attempt.
/// </para>
/// <para>
/// <see cref="CanaryOptions.AutoRollbackEnabled"/> defaults to DISABLED.
/// While disabled, <see cref="ExecuteAsync"/> returns immediately; no
/// experiment is scanned.
/// </para>
/// <para>
/// The automatic rollback decision takes the fail-closed audit path
/// (<see cref="AuditRecorder.WriteOrThrowAsync"/>, NOT the best-effort
/// <see cref="AuditRecorder.WriteAsync"/>) BEFORE the mutation; if the write
/// fails, the rollback is never applied - the same pattern as
/// <c>ApprovalEndpoints.DecideAsync</c>.
/// </para>
/// </remarks>
internal sealed class CanaryEvaluationService(
    IExperimentStore experiments,
    IRunStore runs,
    IAuditLog auditLog,
    ISingletonLeaseStore leaseStore,
    TraconMetrics? metrics,
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

        // 🚨 Wait for the schema to be ready BEFORE the first SQL attempt (K-354).
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
            // Normal shutdown.
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
                logger.LogWarning(exception, "Could not list running experiments with a canary policy defined.");
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
                // A scan must never die: an error in one experiment does not
                // block the others, it is retried on the next tick.
                if (logger is not null && logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LogWarning(exception, "Canary evaluation failed for experiment '{Name}'.", experiment.Name);
                }
            }
        }
    }

    private async Task EvaluateAsync(Experiment experiment, CancellationToken cancellationToken)
    {
        var policy = experiment.Canary;

        if (policy is null)
        {
            // Race: the policy may have been removed after the scan started.
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

        // 🚨 K-089: the audit entry is written BEFORE the mutation, not after.
        // If the write fails, the rollback is never applied ("a rollback that
        // cannot be written to the audit log is not applied").
        //
        // The refusal reaches nobody here — this is a background scan, not an
        // HTTP call — so the throw is caught one frame up in TickAsync, which
        // logs it and moves on to the next experiment. That is the point: the
        // rollback below is not reached.
        try
        {
            await AuditRecorder.WriteOrThrowAsync(
                auditLog,
                // No ambient actor: the scan runs on a background timer, with no
                // request and no IAuditActorResolver to read. The system actor is
                // named here instead.
                "system:canary-evaluator",
                logger ?? NullLogger<CanaryEvaluationService>.Instance,
                metrics,
                experiment.TenantId,
                action: "experiment.auto_rollback",
                entity: $"experiment:{experiment.Name}",
                before: null,
                after: JsonSerializer.Serialize(
                    new CanaryRollbackAuditPayload
                    {
                        Reason = evaluation.Reason,
                        CanaryErrorRate = evaluation.Canary?.ErrorRate,
                        ControlErrorRate = evaluation.Control?.ErrorRate,
                        CanaryAverageScore = evaluation.Canary?.AverageScore,
                    },
                    TraconCoreJsonContext.Default.CanaryRollbackAuditPayload),
                refusal: $"The automatic rollback of experiment '{experiment.Name}' was not applied",
                _clock,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException)
        {
            // Already logged, already counted. The rollback is not applied.
            return;
        }

        await experiments
            .RollbackCanaryAsync(experiment.TenantId, experiment.Name, revertedVariants, evaluation.Reason, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Advances the canary weight to the next <see cref="CanaryPolicy.RampSteps"/>
    /// value. UNLIKE a rollback, this is NOT written to the audit log - it is
    /// only a reversible operation that increases the traffic share (see the
    /// 56.2 flow diagram).
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
            // Already at the last step (or the step list does not exceed the current weight).
            return;
        }

        // 🚨 Before advancing to the next step, this step must have reached
        // MinSampleSize AND RampInterval time must have passed. A second
        // threshold field is NOT opened: MinSampleSize is taken AS-IS from the
        // policy itself.
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
