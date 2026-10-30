using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Side effects of a completed run: quota accounting (every run in a tree), the
/// <c>run.*</c> webhook and the online evaluation sampling decision (root runs
/// only). None of these can affect the run itself — the same "observability does
/// not break functionality" contract as the store and the sinks.
/// </summary>
public sealed partial class RunRecordingAgent
{
    /// <summary>
    /// What a run consumed: its own usage and cost, or — in the webhook of a root
    /// run — those of the root's whole call tree as it stands when the root closes.
    /// </summary>
    /// <param name="Usage">The tokens; <see langword="null"/> when no run reported any.</param>
    /// <param name="Cost">
    /// The spend: the token cost and the amount reported beyond tokens, added by
    /// <see cref="RunSpend"/>. <see langword="null"/> when no run was priced.
    /// </param>
    /// <param name="Currency">The currency of <paramref name="Cost"/>.</param>
    /// <param name="NonTokenCost">
    /// The part of <paramref name="Cost"/> that was reported beyond tokens;
    /// <see langword="null"/> when no run reported an amount.
    /// </param>
    private readonly record struct TreeConsumption(RunUsage? Usage, decimal? Cost, string? Currency, decimal? NonTokenCost);

    /// <summary>What this run alone consumed, from the figures it holds in memory as it closes.</summary>
    private static TreeConsumption OwnConsumption(RunUsage? usage, RunCost? cost, RunNonTokenCost? nonTokenCost)
    {
        var pricedCost = cost is { Source: not PricingSource.Unknown } ? cost : null;

        return new TreeConsumption(
            usage,
            RunSpend.Total(pricedCost, nonTokenCost),
            pricedCost?.Currency ?? nonTokenCost?.Currency,
            nonTokenCost?.Amount);
    }

    /// <summary>
    /// Reads what a root run's child agent runs consumed and adds it to the
    /// root's own usage and cost: the figures its <c>run.*</c> webhook carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The webhook is a picture of the moment the root closes. A child that ends later
    /// is not in it, nor is a tool's spend reported after the run closed; the exact total
    /// is <c>GET /api/runs/{id}</c> and the quota counter, which every run writes to when
    /// it ends. The webhook used to be the quota's twin; it is now the only reader of
    /// this tree read.
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
        TreeConsumption own,
        CancellationToken cancellationToken)
    {
        var usage = own.Usage;

        // Nothing reads the tree when no webhook is registered, and a run without a
        // child reservation has no tree to read.
        if (_webhookPublisher is null || scope.Budget is not { StartedRuns: > 0 })
        {
            return own;
        }

        RunRecord? record;

        try
        {
            // Not the caller's token: a cancellation landing here would end the
            // completion before the terminal write.
            record = await _runStore.GetRunAsync(scope.RunId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, CancellationToken.None))
        {
            _logger.LogWarning(
                exception,
                "The child runs of run {RunId} could not be read; its webhook counts the root only.",
                scope.RunId);

            return own;
        }

        if (record is null)
        {
            return own;
        }

        var treeUsage = RunUsageSum.Add(usage, DescendantUsage(record));

        // A tree whose runs are priced in more than one currency has no total;
        // subtracting the row's own share from a partial one would count a wrong amount.
        if (record.TreeCost is { CurrencyCount: > 1 } || record.TreeNonTokenCost is { CurrencyCount: > 1 })
        {
            _logger.LogWarning(
                "The run tree of run {RunId} is priced in more than one currency; its webhook counts the root's own cost only.",
                scope.RunId);

            return own with { Usage = treeUsage };
        }

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
    /// Writes the consumption of a completed run into the quota counters — the
    /// root's tenant and name, whatever the run's depth — and, for every threshold
    /// newly crossed, appends its notice into this SAME run's own event stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A root writes <c>Runs = 1</c>; a child writes <c>Runs = 0</c>, so a tree is one
    /// run however deep it is and a child that ends after its root still adds its
    /// tokens and cost. The identity comes from the tree's budget
    /// (<see cref="AgentRunBudget.Account"/>), never from the ambient run scope: a
    /// background child ends in a task that does not carry the root's tenant or user.
    /// </para>
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
    /// stopped by then. A threshold a child crosses is therefore written to the
    /// child's stream, which is still open, and not to a root that has long closed.
    /// </para>
    /// </remarks>
    private async ValueTask RecordQuotaAsync(
        RunScope scope,
        TreeConsumption own,
        RunCost? cost,
        RunNonTokenCost? nonTokenCost,
        CancellationToken cancellationToken)
    {
        // A budget no root of ours opened names no counter, and neither does a host
        // with no quota registered (the account carries no recorder then).
        if (scope.Budget?.Account is not { Quota: not null } account)
        {
            return;
        }

        var crossings = await TreeQuota.RecordAsync(
            account,
            scope.RunId,
            runs: scope.Depth == 0 ? 1 : 0,
            tokens: own.Usage?.TotalTokens ?? 0,
            amount: QuotaAmount(scope, own.Cost, cost, nonTokenCost),
            _timeProvider.GetUtcNow(),
            cancellationToken).ConfigureAwait(false);

        // Empty unless TraconQuotaOptions.PublishThresholdToRunStream is
        // on — QuotaEnforcer itself decides that, this loop does
        // not re-check the option.
        foreach (var crossing in crossings)
        {
            await WriteQuotaThresholdNoticeAsync(scope, account, crossing, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The amount a run adds to the quota counter.</summary>
    /// <remarks>
    /// A root adds its own spend as it priced it, as it always did. A descendant adds money
    /// only in the currency Tracon prices in (<c>Tracon:Pricing:Currency</c>, an unlabeled
    /// cost being in it): the counter holds a number with no currency, and the root's
    /// currency is not known while a child ends before it. A child priced in another
    /// currency counts its tokens and the amounts it reported beyond tokens, both of which
    /// are in the pricing currency by construction.
    /// </remarks>
    private decimal? QuotaAmount(RunScope scope, decimal? ownSpend, RunCost? cost, RunNonTokenCost? nonTokenCost)
    {
        // One rule for money across currencies; the counter holds no currency.
        var amount = QuotaAmount(scope.Depth == 0, ownSpend, cost, nonTokenCost, _pricingCurrency, out var tokenCostLeftOut);

        if (tokenCostLeftOut)
        {
            _logger.LogWarning(
                "Child run {RunId} is priced in {Currency}, not in the pricing currency; its quota entry counts its tokens and the amounts it reported beyond tokens, not its token cost.",
                scope.RunId,
                cost!.Currency);
        }

        return amount;
    }

    /// <summary>The pure rule behind <see cref="QuotaAmount(RunScope, decimal?, RunCost?, RunNonTokenCost?)"/>.</summary>
    internal static decimal? QuotaAmount(
        bool isRoot,
        decimal? ownSpend,
        RunCost? cost,
        RunNonTokenCost? nonTokenCost,
        string? pricingCurrency,
        out bool tokenCostLeftOut)
    {
        tokenCostLeftOut = !isRoot &&
                           cost is { Source: not PricingSource.Unknown, Currency: { } currency } &&
                           !string.IsNullOrWhiteSpace(currency) &&
                           !string.Equals(currency, pricingCurrency, StringComparison.OrdinalIgnoreCase);

        return tokenCostLeftOut ? nonTokenCost?.Amount : ownSpend;
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
        TreeAccount account,
        QuotaThresholdCrossing crossing,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new QuotaThresholdNoticePayload
            {
                NoticeId = crossing.NoticeId,

                // The tree's tenant and user, as the quota entry and the webhook name them: a
                // background child's own ambient values are not the root's.
                TenantId = account.TenantId,
                UserId = account.UserId,
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
