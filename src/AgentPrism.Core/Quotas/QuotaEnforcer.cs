using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Kota kurallarini denetler ve tamamlanan calistirmalarin tuketimini yazar.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Kota yaklasiktir</strong> (K-159). Denetim calistirma
/// <em>baslamadan once</em> yapilir, tuketim <em>bittikten sonra</em> yazilir.
/// Ayni anda baslayan calistirmalar kotayi bir miktar asabilir. Siki garanti,
/// her calistirma oncesinde kilit almayi gerektirir ve her istege gecikme
/// ekler. "Yaklasik kota" durust bir ifadedir; "kesin kota" olmayan bir seyi
/// vaat etmek olurdu.
/// </para>
/// <para>
/// Devam eden bir calistirma kota asilinca <strong>kesilmez</strong> (K-162):
/// yarim bir yanit ve harcanmis token, tutarli bir sonuctan kotudur. Yalnizca
/// yeni calistirma <c>429</c> alir.
/// </para>
/// </remarks>
public sealed class QuotaEnforcer(
    IQuotaStore store,
    IOptionsMonitor<AgentPrismQuotaOptions> optionsMonitor,
    IWebhookPublisher? webhookPublisher = null,
    TimeProvider? timeProvider = null,
    ILogger<QuotaEnforcer>? logger = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    // Bir esik her donemde yalnizca BIR KEZ yayilir. Sayac her calistirmada
    // arttigi icin aksi halde esigin ustundeki her calistirma yeni bir olay
    // uretirdi. Anahtar donem baslangicini icerir; donem donunce kendiliginden
    // yeni bir anahtar olusur ve eski girdiler temizlenir.
    private readonly ConcurrentDictionary<ThresholdKey, byte> _firedThresholds = new();

    /// <summary>
    /// Yeni bir calistirmaya izin verilip verilmedigini denetler.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="agentName">Calistirilacak agent'in adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Karar. Izin veriliyorsa <see cref="QuotaDecision.IsAllowed"/> <see langword="true"/>.</returns>
    public async ValueTask<QuotaDecision> CheckAsync(
        string tenantId,
        string agentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return QuotaDecision.Allowed;
        }

        IReadOnlyList<QuotaDefinition> definitions;
        IReadOnlyList<QuotaUsageRecord> usage;

        try
        {
            definitions = await store.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);

            if (definitions.Count == 0)
            {
                // Varsayilan kota YOKTUR: kural tanimlanmadikca hicbir sey
                // reddedilmez ve ikinci bir sorgu yapilmaz.
                return QuotaDecision.Allowed;
            }

            usage = await store
                .GetUsageAsync(new QuotaUsageQuery { TenantId = tenantId }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Kota denetimi basarisiz oldu; AllowOnStoreFailure={Allow}.", options.AllowOnStoreFailure);
            }

            return options.AllowOnStoreFailure
                ? QuotaDecision.Allowed
                : new QuotaDecision
                {
                    IsAllowed = false,
                    Reason = "Kota dogrulanamadi.",
                };
        }

        var now = _clock.GetUtcNow();
        var timeZone = options.ResolveTimeZone();

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
    /// Tamamlanmis bir calistirmanin tuketimini sayaclara ekler ve gerekirse
    /// esik olayi yayar.
    /// </summary>
    /// <param name="consumption">Tuketim.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// Bu cagri <strong>hicbir zaman istisna firlatmaz</strong>: kota
    /// muhasebesi bir gozlemlenebilirlik islevidir ve tamamlanmis bir
    /// calistirmayi geriye donuk bozmamalidir.
    /// </remarks>
    public async ValueTask RecordAsync(
        QuotaConsumption consumption,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumption);

        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled)
        {
            return;
        }

        try
        {
            var timeZone = options.ResolveTimeZone();
            var periodStarts = QuotaPeriodCalculator.GetAllPeriodStarts(consumption.OccurredAt, timeZone);

            await store.AddUsageAsync(consumption, periodStarts, cancellationToken).ConfigureAwait(false);
            await PublishThresholdEventsAsync(consumption, options, timeZone, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Gozlemlenebilirlik islevselligi bozmaz: sayac yazilamazsa
            // calistirma yine de tamamlanmis sayilir.
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(exception, "Kota tuketimi yazilamadi: {TenantId}/{AgentName}.", consumption.TenantId, consumption.AgentName);
            }
        }
    }

    /// <summary>Bir kuralin belirli bir agent'a uygulanip uygulanmadigini bildirir.</summary>
    /// <param name="definition">Kural.</param>
    /// <param name="agentName">Agent adi.</param>
    /// <returns>Kural uygulaniyorsa <see langword="true"/>.</returns>
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
        // Kiraci geneli kural bos ad'li sayaci okur; agent'a bagli kural kendi
        // agent sayacini okur.
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
            ? "kiraci geneli"
            : $"'{definition.AgentName}' agent'i";

        var metricName = metric switch
        {
            QuotaMetric.Runs => "calistirma",
            QuotaMetric.Tokens => "token",
            _ => "maliyet",
        };

        var periodName = definition.Period == QuotaPeriod.Daily ? "gunluk" : "aylik";

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
                $"{scope} icin {periodName} {metricName} kotasi asildi ({used}/{limit}). Sayac {resetsAt:O} tarihinde sifirlanir."),
        };
    }

    private async ValueTask PublishThresholdEventsAsync(
        QuotaConsumption consumption,
        AgentPrismQuotaOptions options,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        if (webhookPublisher is null || options.ThresholdPercents.Count == 0)
        {
            return;
        }

        var definitions = await store.ListAsync(consumption.TenantId, cancellationToken).ConfigureAwait(false);

        if (definitions.Count == 0)
        {
            return;
        }

        var usage = await store
            .GetUsageAsync(new QuotaUsageQuery { TenantId = consumption.TenantId }, cancellationToken)
            .ConfigureAwait(false);

        var now = consumption.OccurredAt;

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

                    var key = new ThresholdKey(consumption.TenantId, scope, definition.Period, periodStart, metric, threshold);

                    if (!_firedThresholds.TryAdd(key, 0))
                    {
                        // Bu esik bu donemde zaten yayildi.
                        break;
                    }

                    await PublishThresholdAsync(definition, metric, threshold, limit, used, now, timeZone, cancellationToken)
                        .ConfigureAwait(false);

                    break;
                }
            }
        }
    }

    /// <summary>Bir kuralda tanimli sinirlari (olcut, sinir, tuketim) sirayla dondurur.</summary>
    /// <param name="definition">Kural.</param>
    /// <param name="usage">Kapsamin gecerli donemdeki sayaci.</param>
    /// <returns><see langword="null"/> olmayan her sinir icin bir eleman.</returns>
    /// <remarks><see cref="QuotaUsageObserver"/> ayni numaralandirmayi olcer icin kullanir.</remarks>
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
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        if (webhookPublisher is null)
        {
            return;
        }

        await webhookPublisher.PublishAsync(
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
                    ResetsAt = QuotaPeriodCalculator.GetPeriodEnd(now, definition.Period, timeZone),
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    private readonly record struct ThresholdKey(
        string TenantId,
        string AgentName,
        QuotaPeriod Period,
        DateOnly PeriodStart,
        QuotaMetric Metric,
        int Threshold);
}
