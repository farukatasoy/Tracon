using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Tamamlanan bir calistirmayi cevrimici degerlendirme icin orneklemeye karar
/// verir ve orneklenirse <see cref="JobKind.OnlineEval"/> isini kuyruga yazar —
/// Faz 49.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Sadece kuyruga <strong>yazma</strong> burada olur; gercek yargic cagrisi
/// (para harcayan islem) <see cref="OnlineEvalJobHandler"/>'da, arka planda
/// yapilir. Bu tasarim <see cref="WebhookPublisher"/> ile aynidir: ana
/// calistirma yolu yalniz hizli bir kuyruk yazimi kadar yavaslar, yavas veya
/// erisilemeyen bir yargicin etkisi hic hissedilmez.
/// </para>
/// <para>
/// <see cref="RunRecordingAgent"/> bu cagriyi <c>try/catch</c> ile sarar:
/// orneklemenin hatasi calistirmayi ETKILEMEZ (gozlemlenebilirlik islevselligi
/// bozmaz kurali).
/// </para>
/// </remarks>
public sealed class RunSampler(
    IJobStore jobStore,
    IOptionsMonitor<OnlineEvaluationOptions> optionsMonitor,
    TimeProvider? timeProvider = null,
    ILogger<RunSampler>? logger = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, HourlyWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>Bir calistirmayi orneklemeyi degerlendirir ve gerekiyorsa kuyruga yazar.</summary>
    /// <param name="request">Orneklenecek calistirmanin ozeti.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Is kuyruga yazildiysa <see langword="true"/>.</returns>
    public async ValueTask<bool> SampleAsync(RunSampleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = optionsMonitor.CurrentValue;

        if (!options.Enabled || options.SampleRate <= 0.0)
        {
            return false;
        }

        // Eval calistirmalari (yargicin KENDI cagrilari dahil) sentetik trafiktir
        // ve sonsuz donguyu burada keser. Basarisiz calistirmalar hata
        // siniflandirmanin isidir; yargic bir hatayi puanlayamaz. Alt calistirma
        // zaten cagirandan (RunRecordingAgent, Depth == 0 kontrolu) buraya hic
        // ulasmaz.
        if (request.Kind == RunKind.Eval || request.Status != RunStatus.Completed)
        {
            return false;
        }

        if (options.AgentNames.Count > 0 &&
            !options.AgentNames.Contains(request.AgentName, StringComparer.Ordinal))
        {
            return false;
        }

        if (!IsSampled(request.RunId, options.SampleRate))
        {
            return false;
        }

        if (!TryConsumeHourlyBudget(request.TenantId, options.MaxScoresPerHour))
        {
            return false;
        }

        try
        {
            var now = _clock.GetUtcNow();
            var runIdText = request.RunId.ToString();

            // 🚨 Payload ATANMALIDIR (K-166): atanmazsa JsonElement `default`
            // kalir ve /api/jobs listesinin tamami 500 ile doner. Webhook
            // teslim isinin ayni sekli (string[]) paylasilir; ikinci bir
            // JsonSerializerContext acmaya gerek yoktur.
            var payload = JsonSerializer.SerializeToElement(
                new[] { runIdText },
                WebhookJobPayloadJsonContext.Default.StringArray);

            await jobStore.EnqueueAsync(
                new JobRecord
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = request.TenantId,
                    Kind = JobKind.OnlineEval,
                    TargetName = request.AgentName,
                    Status = JobStatus.Pending,
                    Payload = payload,
                    ScheduledFor = now,
                    CreatedAt = now,
                },
                [runIdText],
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    exception,
                    "Cevrimici degerlendirme isi kuyruga yazilamadi: calistirma={RunId} kiraci={TenantId}.",
                    request.RunId,
                    request.TenantId);
            }

            return false;
        }
    }

    /// <summary>
    /// Bir calistirmanin orneklenip orneklenmeyecegine calistirma kimliginden
    /// deterministik olarak karar verir.
    /// </summary>
    /// <remarks>
    /// <see cref="HashCode"/> KASITLI OLARAK kullanilmaz: her surec baslatiminda
    /// farkli bir tuzla sonuc uretir ve "ayni calistirma hep ayni karari alir"
    /// garantisini bozar. FNV-1a surec/tuz BAGIMSIZDIR.
    /// </remarks>
    private static bool IsSampled(Guid runId, double sampleRate)
    {
        if (sampleRate >= 1.0)
        {
            return true;
        }

        Span<byte> bytes = stackalloc byte[16];
        runId.TryWriteBytes(bytes);

        var hash = 14695981039346656037UL;

        foreach (var b in bytes)
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        // Ust 53 bit, bir double'in kayipsiz tasiyabildigi tamsayi araligidir;
        // [0, 1) araliginda esit dagilimli bir kesir uretir.
        var fraction = (hash >> 11) * (1.0 / (1UL << 53));

        return fraction < sampleRate;
    }

    /// <summary>
    /// Bir kiracinin bu saatteki orneklem butcesinden bir birim duser.
    /// </summary>
    /// <remarks>
    /// Bellek ici, sabit (kaydirmali degil) saatlik penceredir — orneklemenin
    /// oranin hesap hatasina karsi IKINCI savunmasidir, kesin bir hiz sinirlayici
    /// degildir. Surec yeniden baslatilinca butce sifirlanir; bu kabul edilen
    /// bir davranistir (K1: sadelik, sürekli bir sayaç deposu gerekmez).
    /// </remarks>
    private bool TryConsumeHourlyBudget(string tenantId, int maxPerHour)
    {
        if (maxPerHour <= 0)
        {
            return false;
        }

        var now = _clock.GetUtcNow();
        var windowStart = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Offset);
        var window = _windows.GetOrAdd(tenantId, static _ => new HourlyWindow());

        lock (window)
        {
            if (window.WindowStart != windowStart)
            {
                window.WindowStart = windowStart;
                window.Count = 0;
            }

            if (window.Count >= maxPerHour)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    private sealed class HourlyWindow
    {
        public DateTimeOffset WindowStart;
        public int Count;
    }
}

/// <summary>Ornekleme karari icin gereken calistirma ozeti.</summary>
public sealed record RunSampleRequest
{
    /// <summary>Calistirma kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Calistirilan agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Calistirma turu. <see cref="RunKind.Eval"/> hic orneklenmez.</summary>
    public required RunKind Kind { get; init; }

    /// <summary>Son durum. Yalnizca <see cref="RunStatus.Completed"/> orneklenir.</summary>
    public required RunStatus Status { get; init; }
}
