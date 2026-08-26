using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Root-run-only side effects of a completed run: quota accounting, the
/// <c>run.*</c> webhook, and the online evaluation sampling decision. None of
/// these can affect the run itself — the same "observability does not break
/// functionality" contract as the store and the sinks.
/// </summary>
public sealed partial class RunRecordingAgent
{
    /// <summary>Writes the consumption of a completed root run into the quota counters.</summary>
    /// <remarks>
    /// When the price is undefined, <see cref="QuotaConsumption.Cost"/> stays
    /// <see langword="null"/> — <strong>not</strong> zero. Such a run
    /// does not count toward the monetary quota, but it does count toward the token quota:
    /// when the quota cannot be applied in money, it falls back to tokens.
    /// </remarks>
    private async ValueTask RecordQuotaAsync(
        RunScope scope,
        RunUsage? usage,
        RunCost? cost,
        CancellationToken cancellationToken)
    {
        if (_quotaEnforcer is null)
        {
            return;
        }

        await _quotaEnforcer.RecordAsync(
            new QuotaConsumption
            {
                TenantId = scope.TenantId,
                AgentName = scope.AgentName,
                Runs = 1,
                Tokens = usage?.TotalTokens ?? 0,
                Cost = cost is { Source: not PricingSource.Unknown }
                    ? cost.Total()
                    : null,
                OccurredAt = _timeProvider.GetUtcNow(),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Makes the sampling decision for online evaluation when a root run ends.
    /// </summary>
    /// <remarks>
    /// <see cref="RunSampler.SampleAsync"/> already swallows its own error (see the class
    /// documentation); the <c>try/catch</c> here is a second layer of defense — sampling
    /// must NEVER AFFECT the run (the rule that observability does not break functionality).
    /// </remarks>
    private async ValueTask SampleForOnlineEvalAsync(RunScope scope, RunStatus status, CancellationToken cancellationToken)
    {
        if (_runSampler is null)
        {
            return;
        }

        try
        {
            await _runSampler.SampleAsync(
                new RunSampleRequest
                {
                    RunId = scope.RunId,
                    TenantId = scope.TenantId,
                    AgentName = scope.AgentName,
                    Kind = scope.Kind,
                    Status = status,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "The online evaluation sampling decision failed: run={RunId}.", scope.RunId);
            }
        }
    }

    /// <summary>Emits <c>run.completed</c>/<c>run.failed</c> when a root run ends.</summary>
    /// <remarks>
    /// The payload carries only a <strong>summary</strong>: the message content and
    /// the model response do not go in here. A receiver that wants the content calls
    /// <c>/api/runs/{id}</c>.
    /// </remarks>
    private async ValueTask PublishRunEventAsync(
        RunScope scope,
        RunStatus status,
        RunUsage? usage,
        RunCost? cost,
        RunError? error,
        TimeSpan elapsed,
        string? modelId,
        CancellationToken cancellationToken)
    {
        if (_webhookPublisher is null)
        {
            return;
        }

        // A canceled run is neither a success nor a failure; for a subscriber it is noise.
        var eventType = status switch
        {
            RunStatus.Completed => WebhookEvents.RunCompleted,
            RunStatus.Failed => WebhookEvents.RunFailed,
            _ => null,
        };

        if (eventType is null)
        {
            return;
        }

        await _webhookPublisher.PublishAsync(
            scope.TenantId,
            eventType,
            new WebhookEventPayload
            {
                OccurredAt = _timeProvider.GetUtcNow(),
                Run = new WebhookRunSummary
                {
                    RunId = scope.RunId.ToString(),
                    RootRunId = scope.RootRunId.ToString(),
                    SessionId = scope.SessionId,
                    AgentName = scope.AgentName,
                    ModelId = modelId,
                    Status = status.ToString(),
                    DurationMs = (long)elapsed.TotalMilliseconds,
                    InputTokens = usage?.InputTokens,
                    OutputTokens = usage?.OutputTokens,
                    Cost = cost is { Source: not PricingSource.Unknown }
                        ? cost.Total()
                        : null,
                    Currency = cost?.Currency,
                    Error = error?.Message,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }
}
