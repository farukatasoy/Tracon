using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Root-run-only side effects of a completed run: quota accounting, the
/// <c>run.*</c> webhook, and the online evaluation sampling decision. None of
/// these can affect the run itself — the same "observability does not break
/// functionality" contract as the store and the sinks.
/// </summary>
public sealed partial class RunRecordingAgent
{
    /// <summary>
    /// The consumption of a root run's whole call tree: its own usage and cost
    /// plus those of every child agent run under it.
    /// </summary>
    /// <param name="Usage">The tree's tokens; <see langword="null"/> when no run reported any.</param>
    /// <param name="Cost">
    /// The tree's spend: the token cost and the amount reported beyond tokens, added by
    /// <see cref="RunSpend"/>. <see langword="null"/> when no run was priced.
    /// </param>
    /// <param name="Currency">The currency of <paramref name="Cost"/>.</param>
    /// <param name="NonTokenCost">
    /// The part of <paramref name="Cost"/> that was reported beyond tokens;
    /// <see langword="null"/> when no run reported an amount.
    /// </param>
    private readonly record struct TreeConsumption(RunUsage? Usage, decimal? Cost, string? Currency, decimal? NonTokenCost);

    /// <summary>
    /// Reads what a root run's child agent runs consumed and adds it to the
    /// root's own usage and cost.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A child run never accounts on its own (only a root does, so a
    /// tree is not counted once per depth), and the root used to write only its
    /// own usage. The children's tokens and cost reached no quota counter and no
    /// <c>run.*</c> webhook, so a tenant's quota was exceeded through sub-agents.
    /// A workflow root already counts its tree (<c>WorkflowRunner.RecordQuotaAsync</c>).
    /// </para>
    /// <para>
    /// The children's totals come from the run store, the same source the
    /// workflow root and the run detail read. It is read here, BEFORE the
    /// root's own row closes; whatever the row already holds is subtracted
    /// from the tree total (a queued run leased again after an earlier attempt
    /// closed the row keeps that attempt's usage on the row), and the root's
    /// in-memory usage is added once. The read happens only
    /// when the tree reserved a child run (<see cref="AgentRunBudget.StartedRuns"/>):
    /// every child goes through that reservation, and a run without children
    /// pays no read.
    /// </para>
    /// <para>
    /// A store that cannot answer leaves the root's own consumption, with a
    /// warning: observability does not break functionality. Costs in two
    /// different currencies are not added; the root's own cost is kept.
    /// </para>
    /// </remarks>
    private async ValueTask<TreeConsumption> ReadTreeConsumptionAsync(
        RunScope scope,
        RunUsage? usage,
        RunCost? cost,
        RunNonTokenCost? nonTokenCost,
        CancellationToken cancellationToken)
    {
        var pricedCost = cost is { Source: not PricingSource.Unknown } ? cost : null;

        var own = new TreeConsumption(
            usage,
            RunSpend.Total(pricedCost, nonTokenCost),
            pricedCost?.Currency ?? nonTokenCost?.Currency,
            nonTokenCost?.Amount);

        // Nothing reads the tree when neither consumer is registered, and a run
        // without a child reservation has no tree to read.
        if ((_quotaEnforcer is null && _webhookPublisher is null) || scope.Budget is not { StartedRuns: > 0 })
        {
            return own;
        }

        RunRecord? record;

        try
        {
            // Not the caller's token: a cancellation landing here would end the
            // completion before the quota entry and the terminal write.
            record = await _runStore.GetRunAsync(scope.RunId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            _logger.LogWarning(
                exception,
                "The child runs of run {RunId} could not be read; its quota entry and webhook count the root only.",
                scope.RunId);

            return own;
        }

        if (record is null)
        {
            return own;
        }

        var treeUsage = RunUsageSum.Add(usage, DescendantUsage(record));

        // The tree's spend is the token cost and the reported amount together
        // (RunSpend), and so is what the row already holds: subtracting like from
        // like leaves the descendants' share. The root's own in-memory spend, above,
        // is added once.
        var childCost = RunSpend.Total(record.TreeCost, record.TreeNonTokenCost) is { } treeTotal
            ? Math.Max(0m, treeTotal - (RunSpend.Total(record.Cost, record.NonTokenCost) ?? 0m))
            : (decimal?)null;

        if (childCost is null)
        {
            return own with { Usage = treeUsage };
        }

        var childNonToken = record.TreeNonTokenCost?.Amount is { } treeNonToken
            ? Math.Max(0m, treeNonToken - (record.NonTokenCost?.Amount ?? 0m))
            : (decimal?)null;

        var childCurrency = record.TreeCost?.Currency ?? record.TreeNonTokenCost?.Currency;

        if (own.Cost is null)
        {
            return new TreeConsumption(treeUsage, childCost, childCurrency ?? own.Currency, childNonToken);
        }

        if (childCurrency is not null && own.Currency is not null &&
            !string.Equals(childCurrency, own.Currency, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Run {RunId} and its child runs are priced in {Currency} and {ChildCurrency}; the tree cost is not added across currencies.",
                scope.RunId,
                own.Currency,
                childCurrency);

            return own with { Usage = treeUsage };
        }

        return new TreeConsumption(
            treeUsage,
            own.Cost + childCost,
            own.Currency ?? childCurrency,
            own.NonTokenCost is null && childNonToken is null ? null : (own.NonTokenCost ?? 0m) + (childNonToken ?? 0m));
    }

    /// <summary>The tree's tokens minus whatever the root's own row already holds.</summary>
    /// <param name="record">The root's row, read before it closes.</param>
    /// <returns>The descendants' tokens, or <see langword="null"/> when the tree reported none.</returns>
    private static RunUsage? DescendantUsage(RunRecord record)
    {
        if (record.TreeUsage is not { } tree)
        {
            return null;
        }

        if (record.Usage is not { } own)
        {
            return tree;
        }

        return new RunUsage
        {
            InputTokens = Math.Max(0, (tree.InputTokens ?? 0) - (own.InputTokens ?? 0)),
            OutputTokens = Math.Max(0, (tree.OutputTokens ?? 0) - (own.OutputTokens ?? 0)),
            TotalTokens = Math.Max(0, (tree.TotalTokens ?? 0) - (own.TotalTokens ?? 0)),
        };
    }

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
        TreeConsumption consumption,
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
                Tokens = consumption.Usage?.TotalTokens ?? 0,
                Cost = consumption.Cost,
                OccurredAt = _timeProvider.GetUtcNow(),
                RunId = scope.RunId.ToString(),
                UserId = scope.UserId,
            },
            cancellationToken).ConfigureAwait(false);

        // Empty unless TraconQuotaOptions.PublishThresholdToRunStream is
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
    /// Tracon's own code writes this event, so a client can trust its
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
            TraconCoreJsonContext.Default.QuotaThresholdNoticePayload);

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
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
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
        TreeConsumption consumption,
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
                    Kind = scope.Kind.ToString(),
                    ModelId = modelId,
                    Status = status.ToString(),
                    DurationMs = (long)elapsed.TotalMilliseconds,
                    InputTokens = consumption.Usage?.InputTokens,
                    OutputTokens = consumption.Usage?.OutputTokens,
                    Cost = consumption.Cost,
                    NonTokenCost = consumption.NonTokenCost,
                    Currency = consumption.Currency,
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
