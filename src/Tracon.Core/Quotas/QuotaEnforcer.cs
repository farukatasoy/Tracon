using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Enforces quota rules and records the consumption of completed runs.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The quota is approximate</strong>. The check happens
/// <em>before</em> a run starts; consumption is written <em>after</em> it
/// finishes. Runs that start at the same time can exceed the quota by a small
/// margin. A strict guarantee would require taking a lock before every run and
/// adds latency to every request. "Approximate quota" is an honest statement;
/// "exact quota" would promise something that is not delivered.
/// </para>
/// <para>
/// An in-progress run is <strong>not cut off</strong> when the quota is
/// exceeded: a half-finished response with tokens already spent is
/// worse than a consistent result. Only a new run gets <c>429</c>.
/// </para>
/// </remarks>
internal sealed class QuotaEnforcer
{
    private readonly IQuotaStore _store;
    private readonly IOptionsMonitor<TraconQuotaOptions> _optionsMonitor;
    private readonly IWebhookPublisher? _webhookPublisher;
    private readonly ILogger<QuotaEnforcer>? _logger;
    private readonly TimeProvider _clock;

    internal QuotaEnforcer(
        IQuotaStore store,
        IOptionsMonitor<TraconQuotaOptions> optionsMonitor,
        IWebhookPublisher? webhookPublisher = null,
        TimeProvider? timeProvider = null,
        ILogger<QuotaEnforcer>? logger = null)
    {
        _store = store;
        _optionsMonitor = optionsMonitor;
        _webhookPublisher = webhookPublisher;
        _logger = logger;
        _clock = timeProvider ?? TimeProvider.System;
    }

    // A threshold is published only ONCE per period. Since the counter
    // increases on every run, every run above the threshold would otherwise
    // produce a new event. This in-process set is only the fast path: the
    // durable claim (IQuotaStore.TryClaimThresholdNotificationAsync, K-682) is
    // the single source of truth, so dropping an entry can never publish twice;
    // at worst it costs one more store round-trip.
    //
    // The keys are bucketed by (period, period start). A period that has
    // closed is never written again, so its bucket is dropped on the next
    // call (SweepClosedPeriods). Without it the singleton grew by one key set
    // per tenant scope per day for the life of the process.
    private readonly ConcurrentDictionary<PeriodBucket, ConcurrentDictionary<ThresholdKey, byte>> _firedThresholds = new();

    /// <summary>Gets how many thresholds the in-process fast path holds. Test seam.</summary>
    internal int FiredThresholdCount => _firedThresholds.Values.Sum(static bucket => bucket.Count);

    /// <summary>
    /// Checks whether a new run is allowed.
    /// </summary>
    /// <param name="tenantId">The tenant identity.</param>
    /// <param name="agentName">The name of the agent to run.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The decision. If allowed, <see cref="QuotaDecision.IsAllowed"/> is <see langword="true"/>.</returns>
    public async ValueTask<QuotaDecision> CheckAsync(
        string tenantId,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = _optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return QuotaDecision.Allowed;
        }

        IReadOnlyList<QuotaDefinition> definitions;
        IReadOnlyList<QuotaUsageRecord> usage;

        var now = _clock.GetUtcNow();
        var timeZone = options.ResolveTimeZone();

        try
        {
            definitions = await _store.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);

            if (definitions.Count == 0)
            {
                // There is NO default quota: nothing is rejected until a rule
                // is defined, and no second query is made.
                return QuotaDecision.Allowed;
            }

            // Only the current periods: without the filter every check read
            // the tenant's whole usage history, which only grows (F-275).
            usage = await _store
                .GetUsageAsync(
                    new QuotaUsageQuery
                    {
                        TenantId = tenantId,
                        PeriodStarts = QuotaPeriodCalculator.GetAllPeriodStarts(now, timeZone),
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Quota check failed; AllowOnStoreFailure={Allow}.", options.AllowOnStoreFailure);
            }

            return options.AllowOnStoreFailure
                ? QuotaDecision.Allowed
                : new QuotaDecision
                {
                    IsAllowed = false,
                    Reason = "Quota could not be verified.",
                };
        }

        foreach (var definition in definitions)
        {
            if (!definition.Enabled || !AppliesTo(definition, agentName))
            {
                continue;
            }

            var decision = Evaluate(definition, usage, agentName, now, timeZone);

            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        return QuotaDecision.Allowed;
    }

    /// <summary>
    /// Adds a completed run's consumption to the counters and, if needed, publishes a threshold event.
    /// </summary>
    /// <param name="consumption">The consumption.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The thresholds this call newly crossed, in publication order. Always
    /// empty unless <see cref="TraconQuotaOptions.PublishThresholdToRunStream"/>
    /// is on — the <c>quota.threshold</c> webhook itself is unconditional and
    /// does not depend on this return value; see the option's own remarks.
    /// </returns>
    /// <remarks>
    /// A failure <em>below</em> this call never surfaces: quota accounting is an
    /// observability function and must not retroactively break a completed run.
    /// A store error, and a cancellation that is not the caller's own, return
    /// an empty list — the same as "nothing crossed".
    /// <para>
    /// Two things do come out: <see cref="ArgumentNullException"/> when
    /// <paramref name="consumption"/> is <see langword="null"/>, which is a
    /// caller bug rather than a runtime failure, and
    /// <see cref="OperationCanceledException"/> when
    /// <paramref name="cancellationToken"/> is cancelled, which is the caller
    /// asking to stop.
    /// </para>
    /// </remarks>
    public async ValueTask<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(
        QuotaConsumption consumption,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumption);

        var options = _optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return [];
        }

        try
        {
            var timeZone = options.ResolveTimeZone();
            var periodStarts = QuotaPeriodCalculator.GetAllPeriodStarts(consumption.OccurredAt, timeZone);

            await _store.AddUsageAsync(consumption, periodStarts, cancellationToken).ConfigureAwait(false);

            return await ClaimThresholdCrossingsAsync(consumption, options, timeZone, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            // Observability does not break functionality: if the counter
            // cannot be written, the run is still considered complete.
            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Could not write quota consumption: {TenantId}/{AgentName}.", consumption.TenantId, consumption.AgentName);
            }

            return [];
        }
    }

    /// <summary>Reports whether a rule applies to a specific agent.</summary>
    /// <param name="definition">The rule.</param>
    /// <param name="agentName">The agent name.</param>
    /// <returns><see langword="true"/> if the rule applies.</returns>
    internal static bool AppliesTo(QuotaDefinition definition, string agentName)
        => definition.AgentName is null
           || string.Equals(definition.AgentName, agentName, StringComparison.Ordinal);

    private static QuotaDecision Evaluate(
        QuotaDefinition definition,
        IReadOnlyList<QuotaUsageRecord> usage,
        string agentName,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        // A tenant-wide rule reads the counter with an empty name; a rule
        // scoped to an agent reads its own agent's counter.
        var scope = definition.AgentName is null ? string.Empty : agentName;
        var periodStart = QuotaPeriodCalculator.GetPeriodStart(now, definition.Period, timeZone);
        var resetsAt = QuotaPeriodCalculator.GetPeriodEnd(now, definition.Period, timeZone);

        var current = usage.FirstOrDefault(record =>
            string.Equals(record.AgentName, scope, StringComparison.Ordinal)
            && record.Period == definition.Period
            && record.PeriodStart == periodStart);

        if (current is null)
        {
            return QuotaDecision.Allowed;
        }

        if (definition.MaxRuns is { } maxRuns && current.Runs >= maxRuns)
        {
            return Exceeded(definition, QuotaMetric.Runs, maxRuns, current.Runs, resetsAt);
        }

        if (definition.MaxTokens is { } maxTokens && current.Tokens >= maxTokens)
        {
            return Exceeded(definition, QuotaMetric.Tokens, maxTokens, current.Tokens, resetsAt);
        }

        if (definition.MaxCost is { } maxCost && current.Cost >= maxCost)
        {
            return Exceeded(definition, QuotaMetric.Cost, maxCost, current.Cost, resetsAt);
        }

        return QuotaDecision.Allowed;
    }

    private static QuotaDecision Exceeded(
        QuotaDefinition definition,
        QuotaMetric metric,
        decimal limit,
        decimal used,
        DateTimeOffset resetsAt)
    {
        var scope = definition.AgentName is null
            ? "the whole tenant"
            : $"agent '{definition.AgentName}'";

        var metricName = metric switch
        {
            QuotaMetric.Runs => "run",
            QuotaMetric.Tokens => "token",
            _ => "cost",
        };

        var periodName = definition.Period == QuotaPeriod.Daily ? "daily" : "monthly";

        return new QuotaDecision
        {
            IsAllowed = false,
            Metric = metric,
            AgentName = definition.AgentName,
            Period = definition.Period,
            Limit = limit,
            Used = used,
            ResetsAt = resetsAt,
            Reason = string.Create(
                CultureInfo.InvariantCulture,
                $"The {periodName} {metricName} quota for {scope} has been exceeded ({used}/{limit}). The counter resets at {resetsAt:O}."),
        };
    }

    /// <summary>
    /// Finds every threshold this consumption newly crosses, claims each one
    /// exactly once (in-process fast path first, durable store second),
    /// publishes the unconditional <c>quota.threshold</c> webhook for it, and
    /// — only when <see cref="TraconQuotaOptions.PublishThresholdToRunStream"/>
    /// is on — reports it back for the caller to write into the run's own
    /// event stream.
    /// </summary>
    private async ValueTask<IReadOnlyList<QuotaThresholdCrossing>> ClaimThresholdCrossingsAsync(
        QuotaConsumption consumption,
        TraconQuotaOptions options,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        SweepClosedPeriods(consumption.OccurredAt, timeZone);

        if (options.ThresholdPercents.Count == 0)
        {
            return [];
        }

        var definitions = await _store.ListAsync(consumption.TenantId, cancellationToken).ConfigureAwait(false);

        if (definitions.Count == 0)
        {
            return [];
        }

        var now = consumption.OccurredAt;

        var usage = await _store
            .GetUsageAsync(
                new QuotaUsageQuery
                {
                    TenantId = consumption.TenantId,
                    PeriodStarts = QuotaPeriodCalculator.GetAllPeriodStarts(now, timeZone),
                },
                cancellationToken)
            .ConfigureAwait(false);

        List<QuotaThresholdCrossing>? crossings = null;

        foreach (var definition in definitions)
        {
            if (!definition.Enabled || !AppliesTo(definition, consumption.AgentName))
            {
                continue;
            }

            var scope = definition.AgentName is null ? string.Empty : consumption.AgentName;
            var periodStart = QuotaPeriodCalculator.GetPeriodStart(now, definition.Period, timeZone);

            var current = usage.FirstOrDefault(record =>
                string.Equals(record.AgentName, scope, StringComparison.Ordinal)
                && record.Period == definition.Period
                && record.PeriodStart == periodStart);

            if (current is null)
            {
                continue;
            }

            foreach (var (metric, limit, used) in EnumerateLimits(definition, current))
            {
                if (limit <= 0m)
                {
                    continue;
                }

                var percent = used / limit * 100m;

                foreach (var threshold in options.ThresholdPercents.OrderByDescending(value => value))
                {
                    if (percent < threshold)
                    {
                        continue;
                    }

                    var key = new ThresholdKey(consumption.TenantId, scope, metric, threshold);
                    var bucket = _firedThresholds.GetOrAdd(
                        new PeriodBucket(definition.Period, periodStart),
                        static _ => new ConcurrentDictionary<ThresholdKey, byte>());

                    // Fast path: already handled by THIS process in this period —
                    // no store round-trip needed, whichever way the claim resolved.
                    if (!bucket.TryAdd(key, 0))
                    {
                        break;
                    }

                    // Durability layer (146.4): claims the SAME threshold across a
                    // process restart or a concurrent worker. Only the caller that
                    // wins this atomic claim publishes; a loss is not an error, it
                    // means another run already reported this crossing.
                    bool claimed;

                    try
                    {
                        claimed = await _store.TryClaimThresholdNotificationAsync(
                            consumption.TenantId, scope, definition.Period, periodStart, metric, threshold, cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                        // The claim did not resolve, so the fast path must not
                        // remember it. A kept key would silence this threshold
                        // in this process until the period ends, although the
                        // store never recorded it. Cancellation counts too.
                        bucket.TryRemove(key, out _);
                        throw;
                    }

                    if (!claimed)
                    {
                        break;
                    }

                    var resetsAt = QuotaPeriodCalculator.GetPeriodEnd(now, definition.Period, timeZone);

                    if (_webhookPublisher is not null)
                    {
                        await PublishThresholdAsync(definition, metric, threshold, limit, used, resetsAt, consumption, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (options.PublishThresholdToRunStream)
                    {
                        (crossings ??= []).Add(new QuotaThresholdCrossing
                        {
                            NoticeId = TraconId.NewId().ToString(),
                            Metric = metric,
                            Period = definition.Period,
                            ThresholdPercent = threshold,
                            Limit = limit,
                            Used = used,
                            AgentName = definition.AgentName,
                            ResetsAt = resetsAt,
                        });
                    }

                    break;
                }
            }
        }

        return (IReadOnlyList<QuotaThresholdCrossing>?)crossings ?? [];
    }

    /// <summary>
    /// Drops the fast-path buckets of every period that has closed. A bucket is
    /// dropped only when its start is STRICTLY before the period start that
    /// <paramref name="now"/> falls into for its period type, so a consumption
    /// stamped slightly in the past never removes the open period's keys.
    /// </summary>
    /// <param name="now">
    /// The consumption's own instant — the same clock the keys are derived from,
    /// so the sweep and the keys can never disagree about which period is open.
    /// </param>
    /// <param name="timeZone">The time zone the period boundary is calculated in.</param>
    private void SweepClosedPeriods(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        // A handful of buckets at most: one per period type for the open
        // period, plus the ones that closed since the last call.
        if (_firedThresholds.IsEmpty)
        {
            return;
        }

        // Enumerating the dictionary itself takes no lock and tolerates the
        // removal; Keys would lock every segment to build a snapshot.
        foreach (var (bucket, _) in _firedThresholds)
        {
            if (bucket.PeriodStart < QuotaPeriodCalculator.GetPeriodStart(now, bucket.Period, timeZone))
            {
                _firedThresholds.TryRemove(bucket, out _);
            }
        }
    }

    /// <summary>Returns, in order, the limits (metric, limit, consumption) defined on a rule.</summary>
    /// <param name="definition">The rule.</param>
    /// <param name="usage">The scope's counter for the current period.</param>
    /// <returns>One element for every limit that is not <see langword="null"/>.</returns>
    /// <remarks><see cref="QuotaUsageObserver"/> uses the same enumeration for the gauge.</remarks>
    internal static IEnumerable<(QuotaMetric Metric, decimal Limit, decimal Used)> EnumerateLimits(
        QuotaDefinition definition,
        QuotaUsageRecord usage)
    {
        if (definition.MaxRuns is { } maxRuns)
        {
            yield return (QuotaMetric.Runs, maxRuns, usage.Runs);
        }

        if (definition.MaxTokens is { } maxTokens)
        {
            yield return (QuotaMetric.Tokens, maxTokens, usage.Tokens);
        }

        if (definition.MaxCost is { } maxCost)
        {
            yield return (QuotaMetric.Cost, maxCost, usage.Cost);
        }
    }

    private async ValueTask PublishThresholdAsync(
        QuotaDefinition definition,
        QuotaMetric metric,
        int threshold,
        decimal limit,
        decimal used,
        DateTimeOffset resetsAt,
        QuotaConsumption consumption,
        CancellationToken cancellationToken)
    {
        if (_webhookPublisher is null)
        {
            return;
        }

        await _webhookPublisher.PublishAsync(
            definition.TenantId,
            WebhookEvents.QuotaThreshold,
            new WebhookEventPayload
            {
                Quota = new WebhookQuotaSummary
                {
                    Metric = metric,
                    AgentName = definition.AgentName,
                    Period = definition.Period,
                    ThresholdPercent = threshold,
                    Limit = limit,
                    Used = used,
                    ResetsAt = resetsAt,
                    RunId = consumption.RunId,
                    UserId = consumption.UserId,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PeriodBucket(QuotaPeriod Period, DateOnly PeriodStart);

    private readonly record struct ThresholdKey(
        string TenantId,
        string AgentName,
        QuotaMetric Metric,
        int Threshold);
}
