using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <c>agentprism.quota.usage</c>/<c>agentprism.quota.limit</c> gozlemlenen
/// olcerlerinin onbellekli veri kaynagi (Faz 35).
/// </summary>
/// <remarks>
/// <para>
/// <c>ObservableGauge</c> geri cagirmasi es zamanlidir; veritabani okumasi
/// icinde dogrudan YAPILAMAZ. Bu sinif her geri cagirmada onbellegin yasini
/// <see cref="AgentPrismObservabilityOptions.QuotaUsageRefreshInterval"/> ile
/// karsilastirir: onbellek tazeyse dogrudan doner, bayatladiysa BIR kez
/// (blok olarak) tazeler. Ardisik yoklamalar bu araligin icindeyse ikinci bir
/// veritabani sorgusu olusmaz.
/// </para>
/// <para>
/// <see cref="AgentPrismObservabilityOptions.EnableQuotaUsageGauge"/>
/// <see langword="false"/> oldugu surece onbellek HIC dokunulmaz — enstruman
/// adi yine de mevcuttur (tuketicinin OTel yapilandirmasi degismeden
/// acilabilir), ama olcum yayilmaz ve veritabanina gidilmez.
/// </para>
/// <para>
/// 🚨 <strong>Bilinen sinirlama:</strong> kiraci kaydi (<see cref="ITenantStore"/>)
/// zorunlu degildir; bu olcer yalniz KAYITLI kiracilari tarar. Kaydi olmayan bir
/// kiracinin kota kurali <see cref="QuotaEnforcer"/> tarafindan yine dogru
/// uygulanir, yalniz bu gosterge panosunda GORUNMEZ. Bu, fazin "yeni tablo/uc
/// yok" hedefiyle kabul edilen bir sinirlamadir
/// (bkz. <c>docs/35-MALIYET-VE-KOTA-METRIKLERI.md</c>).
/// </para>
/// </remarks>
public sealed class QuotaUsageObserver : IHostedService, IDisposable
{
    private readonly IQuotaStore _quotaStore;
    private readonly ITenantStore _tenantStore;
    private readonly IOptionsMonitor<AgentPrismOptions> _options;
    private readonly IOptionsMonitor<AgentPrismQuotaOptions> _quotaOptions;
    private readonly TimeProvider _clock;
    private readonly ILogger<QuotaUsageObserver>? _logger;
    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    // SemaphoreSlim: net8.0 de hedeflendigi icin System.Threading.Lock
    // kullanilamaz (MA0158 ayri bir object alanini da yasaklar); es zamanli
    // (senkron) kod yolunda Wait()/Release() ile kullanilir.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<QuotaGaugeSample> _snapshot = [];
    private DateTimeOffset? _lastRefreshedAt;

    /// <summary>Yeni bir kota olcer olusturur.</summary>
    /// <param name="quotaStore">Kota kural ve sayac deposu.</param>
    /// <param name="tenantStore">Kayitli kiracilarin deposu.</param>
    /// <param name="options">
    /// Olcer acik/kapali ve onbellek araligi ayarlarini tasiyan <see cref="AgentPrismOptions.Observability"/>'in
    /// bagli oldugu kok tip. <see cref="AgentPrismObservabilityOptions"/> kendi basina (standalone)
    /// hicbir yerde <c>services.Configure&lt;AgentPrismObservabilityOptions&gt;</c> ile kayitli DEGILDIR —
    /// yalnizca <see cref="AgentPrismOptions.Observability"/> uzerinden baglanir (HATA-S4-020).
    /// </param>
    /// <param name="quotaOptions">Kota donem hesabinin saat dilimi ayari.</param>
    /// <param name="meterFactory">
    /// Olcum fabrikasi. <see langword="null"/> ise kendi <see cref="Meter"/> ornegi
    /// olusturulur ve sahipligi bu nesneye ait olur.
    /// </param>
    /// <param name="timeProvider">Zaman kaynagi. <see langword="null"/> ise <see cref="TimeProvider.System"/>.</param>
    /// <param name="logger">Onbellek tazeleme hatalarinin loglandigi gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bir bagimlilik <see langword="null"/> ise.</exception>
    public QuotaUsageObserver(
        IQuotaStore quotaStore,
        ITenantStore tenantStore,
        IOptionsMonitor<AgentPrismOptions> options,
        IOptionsMonitor<AgentPrismQuotaOptions> quotaOptions,
        IMeterFactory? meterFactory = null,
        TimeProvider? timeProvider = null,
        ILogger<QuotaUsageObserver>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(quotaStore);
        ArgumentNullException.ThrowIfNull(tenantStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(quotaOptions);

        _quotaStore = quotaStore;
        _tenantStore = tenantStore;
        _options = options;
        _quotaOptions = quotaOptions;
        _clock = timeProvider ?? TimeProvider.System;
        _logger = logger;

        if (meterFactory is null)
        {
            _meter = new Meter(AgentPrismDiagnostics.MeterName);
            _ownsMeter = true;
        }
        else
        {
            _meter = meterFactory.Create(AgentPrismDiagnostics.MeterName);
        }

        _meter.CreateObservableGauge(
            AgentPrismDiagnostics.QuotaUsageGaugeName,
            ObserveUsage,
            description: "Kota kapsaminin gecerli donemdeki tuketimi.");

        _meter.CreateObservableGauge(
            AgentPrismDiagnostics.QuotaLimitGaugeName,
            ObserveLimit,
            description: "Kota kapsaminin tanimli sinirlari.");
    }

    /// <summary>
    /// Ayrica bir is yapmaz — kurucu, olcum aletlerini KAYIT sirasinda zaten
    /// olusturur. Bu tipin <see cref="IHostedService"/> olarak eklenmesinin
    /// tek amaci, konteynerin bu nesneyi ERKEN (barindirici baslarken) cozup
    /// olusturmasidir; aksi halde hicbir tuketici cozmedigi surece olcum
    /// aletleri hic olusmaz.
    /// </summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanmis bir gorev.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc cref="IHostedService.StopAsync(CancellationToken)" />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }

        _gate.Dispose();
    }

    private IEnumerable<Measurement<double>> ObserveUsage()
        => Snapshot().Select(static sample => new Measurement<double>((double)sample.Used, BuildTags(sample)));

    private IEnumerable<Measurement<double>> ObserveLimit()
        => Snapshot().Select(static sample => new Measurement<double>((double)sample.Limit, BuildTags(sample)));

    private IReadOnlyList<QuotaGaugeSample> Snapshot()
    {
        if (!_options.CurrentValue.Observability.EnableQuotaUsageGauge)
        {
            return [];
        }

        _gate.Wait();

        try
        {
            var interval = _options.CurrentValue.Observability.QuotaUsageRefreshInterval;
            var now = _clock.GetUtcNow();

            if (_lastRefreshedAt is null || now - _lastRefreshedAt.Value >= interval)
            {
                // 🚨 Es zamanli geri cagirmadan veritabanina BLOK olarak gidilir.
                // Bu, siniflandirmanin kendi belgesinde acikca kabul edilen bir
                // bedeldir: cagri sikligi en kotu ihtimalle
                // QuotaUsageRefreshInterval kadardir ve gauge toplama tipik
                // olarak ayri, dusuk frekansli bir arka plan gorevinde calisir.
                _snapshot = RefreshAsync().GetAwaiter().GetResult();
                _lastRefreshedAt = now;
            }

            return _snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<QuotaGaugeSample>> RefreshAsync()
    {
        try
        {
            var tenants = await _tenantStore.ListAsync().ConfigureAwait(false);
            var timeZone = _quotaOptions.CurrentValue.ResolveTimeZone();
            var now = _clock.GetUtcNow();
            var samples = new List<QuotaGaugeSample>();

            foreach (var tenant in tenants)
            {
                var definitions = await _quotaStore.ListAsync(tenant.Slug).ConfigureAwait(false);

                if (definitions.Count == 0)
                {
                    continue;
                }

                var usage = await _quotaStore
                    .GetUsageAsync(new QuotaUsageQuery { TenantId = tenant.Slug })
                    .ConfigureAwait(false);

                foreach (var definition in definitions)
                {
                    if (!definition.Enabled)
                    {
                        continue;
                    }

                    // Kiraci geneli kural bos ad'li sayaci okur; agent'a bagli
                    // kural kendi agent sayacini okur (QuotaEnforcer.Evaluate ile
                    // ayni sozlesme).
                    var scope = definition.AgentName ?? string.Empty;
                    var periodStart = QuotaPeriodCalculator.GetPeriodStart(now, definition.Period, timeZone);

                    var current = usage.FirstOrDefault(record =>
                        string.Equals(record.AgentName, scope, StringComparison.Ordinal)
                        && record.Period == definition.Period
                        && record.PeriodStart == periodStart);

                    if (current is null)
                    {
                        continue;
                    }

                    foreach (var (metric, limit, used) in QuotaEnforcer.EnumerateLimits(definition, current))
                    {
                        samples.Add(new QuotaGaugeSample(tenant.Slug, scope, definition.Period, metric, used, limit));
                    }
                }
            }

            return samples;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (_logger is not null && _logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(exception, "Kota olcer onbellegi tazelenemedi; onceki deger korunuyor.");
            }

            // Onceki gecerli onbellegi koru: kismi bir hata gostergeyi sifira
            // dusurmemelidir.
            return _snapshot;
        }
    }

    private static KeyValuePair<string, object?>[] BuildTags(QuotaGaugeSample sample) =>
    [
        new(AgentPrismDiagnostics.Tags.TenantId, sample.TenantId),
        new(AgentPrismDiagnostics.Tags.QuotaScope, sample.Scope),
        new(AgentPrismDiagnostics.Tags.QuotaPeriod, sample.Period.ToString()),
        new(AgentPrismDiagnostics.Tags.QuotaMetric, sample.Metric.ToString()),
    ];

    private readonly record struct QuotaGaugeSample(
        string TenantId,
        string Scope,
        QuotaPeriod Period,
        QuotaMetric Metric,
        decimal Used,
        decimal Limit);
}
