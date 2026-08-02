using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Kayitli model saglayicilarinin saglik denetimi sonuclarini onbellekler.
/// </summary>
/// <remarks>
/// <para>
/// <c>/api/models/health</c> ucu her acilista saglayiciya gitmez; sonuc
/// <see cref="AgentPrismHealthOptions.CacheTtl"/> suresince onbellekten dondurulur.
/// <c>refresh: true</c> onbellegi atlar ve saglayiciya yeniden gider.
/// </para>
/// <para>
/// Bu sinif <see cref="IModelProviderRegistry"/>'ye bagli DEGILDIR — dogrudan
/// <see cref="IModelProvider"/> singleton'larini <c>IEnumerable&lt;IModelProvider&gt;</c>
/// uzerinden okur. Boylece <see cref="IModelProvider"/> arayuzune uye eklemeden
/// (K4) her saglayicinin <see cref="IModelProviderHealthCheck"/> uygulayip
/// uygulamadigini denetleyebilir; uygulamayan saglayicilarin durumu
/// <see cref="ModelProviderHealthStatus.Unknown"/>'dir.
/// </para>
/// <para>
/// Devre kesici acik olan bir saglayici, onbellekteki ham denetim sonucu ne olursa
/// olsun <see cref="ModelProviderHealthStatus.Unhealthy"/> olarak raporlanir — gercek
/// sohbet cagrilari basarisiz oluyorsa bu, ham baglanti yoklamasindan daha guvenilir
/// bir sinyaldir. Bu katman her cagrida TAZE okunur, onbellege yazilmaz.
/// </para>
/// </remarks>
public sealed class ModelProviderHealthCache
{
    private readonly IEnumerable<IModelProvider> _providers;
    private readonly IOptionsMonitor<AgentPrismOptions> _optionsMonitor;
    private readonly ModelProviderCircuitBreaker? _circuitBreaker;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Yeni bir saglik onbellegi olusturur.</summary>
    /// <param name="providers">Kayitli model saglayicilari.</param>
    /// <param name="optionsMonitor">Onbellek TTL'sini okumak icin ayarlar.</param>
    /// <param name="circuitBreaker">Devre durumunu sonuca yansitmak icin devre kesici.</param>
    /// <param name="timeProvider">Zaman kaynagi. <see langword="null"/> ise sistem saati kullanilir.</param>
    /// <exception cref="ArgumentNullException"><paramref name="providers"/> veya <paramref name="optionsMonitor"/> <see langword="null"/> ise.</exception>
    public ModelProviderHealthCache(
        IEnumerable<IModelProvider> providers,
        IOptionsMonitor<AgentPrismOptions> optionsMonitor,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _providers = providers;
        _optionsMonitor = optionsMonitor;
        _circuitBreaker = circuitBreaker;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Tum kayitli saglayicilarin saglik durumunu dondurur (ada gore sirali).</summary>
    /// <param name="refresh">Onbellegi atlayip saglayiciya yeniden gidilsin mi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Her saglayici icin bir kayit.</returns>
    public async ValueTask<IReadOnlyList<ModelProviderHealth>> GetAllAsync(
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ModelProviderHealth>();

        foreach (var provider in _providers)
        {
            results.Add(await GetOneAsync(provider, refresh, cancellationToken).ConfigureAwait(false));
        }

        results.Sort(static (left, right) => string.CompareOrdinal(left.ProviderName, right.ProviderName));
        return results;
    }

    /// <summary>Tek bir saglayicinin saglik durumunu dondurur.</summary>
    /// <param name="providerName">Saglayici adi.</param>
    /// <param name="refresh">Onbellegi atlayip saglayiciya yeniden gidilsin mi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Saglayici kayitliysa durumu; degilse <see langword="null"/>.</returns>
    public async ValueTask<ModelProviderHealth?> GetAsync(
        string providerName,
        bool refresh,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        var provider = _providers.FirstOrDefault(
            candidate => string.Equals(candidate.Name, providerName, StringComparison.OrdinalIgnoreCase));

        return provider is null
            ? null
            : await GetOneAsync(provider, refresh, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Onbellekteki son bilinen durumu, ag cagrisi yapmadan dondurur. <c>/api/models</c>
    /// gibi hizli uclarin durum alanini doldurmasi icindir.
    /// </summary>
    /// <param name="providerName">Saglayici adi.</param>
    /// <param name="health">Bulunursa saglik kaydi.</param>
    /// <returns>Onbellekte bir kayit varsa <see langword="true"/>.</returns>
    public bool TryPeek(string providerName, out ModelProviderHealth health)
    {
        if (_cache.TryGetValue(providerName, out var entry))
        {
            health = ApplyCircuitBreakerOverlay(providerName, entry.Health);
            return true;
        }

        health = null!;
        return false;
    }

    private async ValueTask<ModelProviderHealth> GetOneAsync(
        IModelProvider provider,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var baseHealth = await GetBaseHealthAsync(provider, refresh, cancellationToken).ConfigureAwait(false);
        return ApplyCircuitBreakerOverlay(provider.Name, baseHealth);
    }

    private async ValueTask<ModelProviderHealth> GetBaseHealthAsync(
        IModelProvider provider,
        bool refresh,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        if (!refresh && _cache.TryGetValue(provider.Name, out var cached) && cached.ExpiresAt > now)
        {
            return cached.Health;
        }

        var health = provider is IModelProviderHealthCheck check
            ? await check.CheckHealthAsync(cancellationToken).ConfigureAwait(false)
            : new ModelProviderHealth
            {
                ProviderName = provider.Name,
                Status = ModelProviderHealthStatus.Unknown,
                CheckedAt = now,
            };

        var ttl = _optionsMonitor.CurrentValue.Health.CacheTtl;
        _cache[provider.Name] = new CacheEntry(health, now + ttl);

        return health;
    }

    private ModelProviderHealth ApplyCircuitBreakerOverlay(string providerName, ModelProviderHealth health)
    {
        if (_circuitBreaker is null || !_circuitBreaker.IsOpen(providerName, out var retryAfter))
        {
            return health;
        }

        var circuitDetail = $"Devre kesici acik. {retryAfter?.TotalSeconds ?? 0:F0} sn sonra yeniden denenecek.";

        return health with
        {
            Status = ModelProviderHealthStatus.Unhealthy,
            Detail = string.IsNullOrEmpty(health.Detail) ? circuitDetail : $"{circuitDetail} ({health.Detail})",
        };
    }

    private sealed record CacheEntry(ModelProviderHealth Health, DateTimeOffset ExpiresAt);
}
