using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Cevrimici degerlendirme puanlarinin kayan pencere ozetini tutar ve esik
/// asildiginda <see cref="WebhookEvents.RunScoreLow"/> olayini yayar — Faz 49.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Puan penceresi <strong>bellek icidir</strong> (kiraci basina bir kuyruk).
/// Kaynak gercek (source of truth) her zaman <c>run_scores</c> tablosudur —
/// bir operator <c>SELECT source, avg(value) FROM run_scores GROUP BY source</c>
/// ile her an tam sonuca ulasir. Bu servis yalnizca canli bir gosterge ve alarm
/// icin ucuz bir yaklastirmadir; sureç yeniden baslatilinca sifirlanir. Ayni
/// tasarim tercihi <see cref="RunSampler"/>'in saatlik butcesinde de kullanildi
/// (K1: surekli bir sayac deposu gerekmez).
/// </para>
/// <para>
/// 🚨 Tek bir dusuk puan alarm uretmez: <see cref="OnlineEvaluationOptions.MinSampleSize"/>
/// asilmadan esik denetimi hic yapilmaz. Model gurultuludur; aksi halde
/// bildirimler hizla yok sayilmaya baslar.
/// </para>
/// </remarks>
public sealed class OnlineEvalSummaryService(
    IRunStore runStore,
    IOptionsMonitor<OnlineEvaluationOptions> optionsMonitor,
    IWebhookPublisher? webhookPublisher = null,
    TimeProvider? timeProvider = null)
{
    private readonly ConcurrentDictionary<string, TenantWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>
    /// Yeni yazilan bir yargic puanini pencereye ekler ve gerekiyorsa
    /// <see cref="WebhookEvents.RunScoreLow"/> olayini yayar.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="score">Yazilan puan, 0-100.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    public async ValueTask RecordScoreAsync(string tenantId, int score, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = optionsMonitor.CurrentValue;
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var window = _windows.GetOrAdd(tenantId, static _ => new TenantWindow());

        ScoreWindowSnapshot snapshot;

        lock (window)
        {
            window.Add(now, score, options.EvaluationWindow);
            snapshot = window.Summarize(now, options.EvaluationWindow);
        }

        var belowThreshold = snapshot.SampleCount >= options.MinSampleSize &&
            snapshot.AverageScore is { } average && average < options.LowScoreThreshold;

        if (!belowThreshold || webhookPublisher is null)
        {
            return;
        }

        await webhookPublisher.PublishAsync(
            tenantId,
            WebhookEvents.RunScoreLow,
            new WebhookEventPayload
            {
                OccurredAt = now,
                Score = new WebhookScoreSummary
                {
                    AverageScore = snapshot.AverageScore!.Value,
                    SampleCount = snapshot.SampleCount,
                    Threshold = options.LowScoreThreshold,
                    WindowStart = now - options.EvaluationWindow,
                    WindowEnd = now,
                },
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Bir kiracinin guncel pencere ozetini ve yargic maliyetini dondurur.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    public async ValueTask<OnlineEvaluationSummary> GetSummaryAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var options = optionsMonitor.CurrentValue;
        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var since = now - options.EvaluationWindow;

        ScoreWindowSnapshot snapshot = default;

        if (_windows.TryGetValue(tenantId, out var window))
        {
            lock (window)
            {
                snapshot = window.Summarize(now, options.EvaluationWindow);
            }
        }

        // 🚨 Yargic calistirmalari `AgentName = "judge:{ad}"` ile kaydedilir
        // (bkz. ModelRunJudge); ayni Kind.Eval degerini paylasan eval takim
        // vaka calistirmalarindan bu onekle ayirt edilir. Ikinci bir SQL sorgu
        // yuzeyi acmadan mevcut RunQuery.Kind suzgeciyle (Faz 49) yeterli.
        var evalRuns = await runStore.QueryRunsAsync(
            new RunQuery
            {
                TenantId = tenantId,
                Kind = RunKind.Eval,
                StartedAfter = since,
                OnlyRootRuns = false,
                Take = 500,
            },
            cancellationToken).ConfigureAwait(false);

        decimal? judgeCost = null;
        string? judgeCostCurrency = null;

        foreach (var run in evalRuns)
        {
            if (!run.AgentName.StartsWith("judge:", StringComparison.Ordinal) ||
                run.Cost is not { Source: not PricingSource.Unknown } cost)
            {
                continue;
            }

            judgeCost = (judgeCost ?? 0m) + (cost.InputCost ?? 0m) + (cost.OutputCost ?? 0m);
            judgeCostCurrency ??= cost.Currency;
        }

        return new OnlineEvaluationSummary
        {
            WindowStart = since,
            WindowEnd = now,
            SampleCount = snapshot.SampleCount,
            AverageScore = snapshot.AverageScore,
            LowScoreThreshold = options.LowScoreThreshold,
            MinSampleSize = options.MinSampleSize,
            BelowThreshold = snapshot.SampleCount >= options.MinSampleSize &&
                snapshot.AverageScore is { } avg && avg < options.LowScoreThreshold,
            JudgeCost = judgeCost,
            JudgeCostCurrency = judgeCostCurrency,
        };
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ScoreWindowSnapshot(long SampleCount, double? AverageScore);

    private sealed class TenantWindow
    {
        private readonly Queue<(DateTimeOffset At, int Score)> _samples = new();

        public void Add(DateTimeOffset at, int score, TimeSpan windowSize)
        {
            _samples.Enqueue((at, score));
            Prune(at, windowSize);
        }

        public ScoreWindowSnapshot Summarize(DateTimeOffset now, TimeSpan windowSize)
        {
            Prune(now, windowSize);

            return _samples.Count == 0
                ? default
                : new ScoreWindowSnapshot(_samples.Count, _samples.Average(static sample => sample.Score));
        }

        private void Prune(DateTimeOffset now, TimeSpan windowSize)
        {
            var cutoff = now - windowSize;

            while (_samples.Count > 0 && _samples.Peek().At < cutoff)
            {
                _samples.Dequeue();
            }
        }
    }
}

/// <summary>Cevrimici degerlendirme penceresinin ozeti (<c>GET /api/evaluation/online</c>).</summary>
public sealed record OnlineEvaluationSummary
{
    /// <summary>Pencerenin baslangici (UTC).</summary>
    public required DateTimeOffset WindowStart { get; init; }

    /// <summary>Pencerenin bitisi (UTC).</summary>
    public required DateTimeOffset WindowEnd { get; init; }

    /// <summary>Pencere icindeki ornek (puanlanmis calistirma) sayisi.</summary>
    public required long SampleCount { get; init; }

    /// <summary>Pencere icindeki ortalama puan, 0-100. Ornek yoksa <see langword="null"/>.</summary>
    public double? AverageScore { get; init; }

    /// <summary>Yapilandirilmis dusuk puan esigi.</summary>
    public required int LowScoreThreshold { get; init; }

    /// <summary>Alarm icin gereken asgari ornek sayisi.</summary>
    public required int MinSampleSize { get; init; }

    /// <summary>
    /// Ortalama esigin altinda VE ornek sayisi asgariyi astiysa
    /// <see langword="true"/>.
    /// </summary>
    public required bool BelowThreshold { get; init; }

    /// <summary>Pencere icindeki toplam yargic maliyeti. Bilinmiyorsa <see langword="null"/>.</summary>
    public decimal? JudgeCost { get; init; }

    /// <summary>Yargic maliyetinin para birimi.</summary>
    public string? JudgeCostCurrency { get; init; }
}
