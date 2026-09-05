using System.Text.Json;
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
    /// <summary>
    /// Writes the consumption of a completed root run into the quota
    /// counters and, for every threshold newly crossed, appends its notice
    /// into this SAME run's own event stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the price is undefined, <see cref="QuotaConsumption.Cost"/> stays
    /// <see langword="null"/> — <strong>not</strong> zero. Such a run
    /// does not count toward the monetary quota, but it does count toward the token quota:
    /// when the quota cannot be applied in money, it falls back to tokens.
    /// </para>
    /// <para>
    /// Called BEFORE <c>scope.Writer.CompleteAsync</c>: a notice appended
    /// after the terminal event would never reach this run's own append-only
    /// stream — the reader that watches for the terminal event has already
    /// stopped by then.
    /// </para>
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

        var crossings = await _quotaEnforcer.RecordAsync(
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
                RunId = scope.RunId.ToString(),
                UserId = scope.UserId,
            },
            cancellationToken).ConfigureAwait(false);

        // Empty unless AgentPrismQuotaOptions.PublishThresholdToRunStream is
        // on (K1, 146.5) — QuotaEnforcer itself decides that, this loop does
        // not re-check the option.
        foreach (var crossing in crossings)
        {
            await WriteQuotaThresholdNoticeAsync(scope, crossing, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Appends one quota threshold crossing as a <see cref="RunEventType.Custom"/>
    /// event under <see cref="RunEventCustomTypes.QuotaThreshold"/>.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="RunEventWriter.AppendReservedAsync"/>, not the public
    /// <see cref="RunEventWriter.AppendAsync"/>: this <c>CustomType</c> falls
    /// under <see cref="RunEventCustomTypes.ReservedPrefix"/>, which
    /// <c>AppendAsync</c> exists to reject for every OTHER caller — only
    /// AgentPrism's own code writes this event, so a client can trust its
    /// origin without inspecting who is calling.
    /// </remarks>
    private static async ValueTask WriteQuotaThresholdNoticeAsync(
        RunScope scope,
        QuotaThresholdCrossing crossing,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new QuotaThresholdNoticePayload
            {
                NoticeId = crossing.NoticeId,
                TenantId = scope.TenantId,
                UserId = scope.UserId,
                RunId = scope.RunId,
                SessionId = scope.SessionId,
                Metric = crossing.Metric,
                Period = crossing.Period,
                ThresholdPercent = crossing.ThresholdPercent,
                Limit = crossing.Limit,
                Used = crossing.Used,
                ResetsAt = crossing.ResetsAt,
            },
            AgentPrismCoreJsonContext.Default.QuotaThresholdNoticePayload);

        await scope.Writer.AppendReservedAsync(
            new RunEventDraft(RunEventType.Custom)
            {
                CustomType = RunEventCustomTypes.QuotaThreshold,
                Payload = payload,
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

/// <summary>
/// The <see cref="RunEventCustomTypes.QuotaThreshold"/> notice's payload.
/// </summary>
internal sealed record QuotaThresholdNoticePayload
{
    /// <summary>Gets the dedup/delivery identifier. See <see cref="QuotaThresholdCrossing.NoticeId"/>.</summary>
    public required string NoticeId { get; init; }

    /// <summary>Gets the tenant identifier.</summary>
    public string? TenantId { get; init; }

    /// <summary>Gets the user the run belongs to. <see langword="null"/> when unknown.</summary>
    public string? UserId { get; init; }

    /// <summary>Gets the identity of the run whose completion crossed this threshold.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the session the run belongs to. <see langword="null"/> for a sessionless run.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the metric whose threshold was crossed.</summary>
    public required QuotaMetric Metric { get; init; }

    /// <summary>Gets the counter's interval.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>Gets the crossed threshold percentage.</summary>
    public required int ThresholdPercent { get; init; }

    /// <summary>Gets the rule's defined limit.</summary>
    public required decimal Limit { get; init; }

    /// <summary>Gets the consumption at the moment the threshold was claimed.</summary>
    public required decimal Used { get; init; }

    /// <summary>Gets the time the counter resets (UTC).</summary>
    public DateTimeOffset? ResetsAt { get; init; }
}
