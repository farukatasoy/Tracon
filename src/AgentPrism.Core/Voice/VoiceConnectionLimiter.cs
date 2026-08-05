using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Kiraci basina es zamanli konusma baglantisi sayisini sinirlar.
/// </summary>
/// <remarks>
/// <para>
/// Sayac <strong>bellek icidir</strong> ve sureç basinadir. Cok ornekli bir
/// dagitimda her ornek kendi sinirini uygular; toplam sinir ornek sayisiyla
/// carpilir. Bu bilinclidir: konusma baglantisi zaten tek bir ornege baglidir
/// (yapiskan oturum) ve dagitik bir sayac, korumadan cok daha pahali bir
/// koordinasyon gerektirirdi.
/// </para>
/// <para>
/// Ayni gerekce hiz siniri icin de gecerlidir (K-158).
/// </para>
/// </remarks>
public sealed class VoiceConnectionLimiter
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.Ordinal);

    /// <summary>Yeni bir sinirlayici kurar.</summary>
    /// <param name="limit">Kiraci basina en fazla es zamanli baglanti.</param>
    public VoiceConnectionLimiter(int limit) => Limit = Math.Max(1, limit);

    /// <summary>Kiraci basina izin verilen en fazla baglanti.</summary>
    public int Limit { get; }

    /// <summary>Bir baglanti yeri ayirmayi dener.</summary>
    /// <param name="tenantId">Kiraci.</param>
    /// <returns>
    /// Ayrilan yer; sinir doluysa <see langword="null"/>. Donen nesne
    /// bertaraf edildiginde yer serbest kalir.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="tenantId"/> bos ise.</exception>
    public VoiceConnectionLease? TryAcquire(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        while (true)
        {
            var current = _counts.GetOrAdd(tenantId, 0);

            if (current >= Limit)
            {
                return null;
            }

            // 🚨 Karsilastir-ve-degistir sart: iki istek ayni anda okursa
            // ikisi de siniri asmayan bir deger gorur ve sinir bir fazla acilir.
            if (_counts.TryUpdate(tenantId, current + 1, current))
            {
                return new VoiceConnectionLease(this, tenantId);
            }
        }
    }

    /// <summary>Bir kiracinin acik baglanti sayisini dondurur.</summary>
    /// <param name="tenantId">Kiraci.</param>
    /// <returns>Acik baglanti sayisi.</returns>
    public int CountFor(string tenantId)
        => tenantId is not null && _counts.TryGetValue(tenantId, out var count) ? count : 0;

    internal void Release(string tenantId)
    {
        while (true)
        {
            if (!_counts.TryGetValue(tenantId, out var current) || current <= 0)
            {
                return;
            }

            if (_counts.TryUpdate(tenantId, current - 1, current))
            {
                return;
            }
        }
    }
}

/// <summary>Ayrilmis bir konusma baglantisi yeri.</summary>
/// <remarks>
/// Bertaraf etmek yeri serbest birakir. Bertaraf edilmeyen bir yer kalici olarak
/// tukenir; bu yuzden baglanti kodu <c>using</c> kullanir.
/// </remarks>
public sealed class VoiceConnectionLease : IDisposable
{
    private readonly VoiceConnectionLimiter _limiter;
    private readonly string _tenantId;
    private bool _released;

    internal VoiceConnectionLease(VoiceConnectionLimiter limiter, string tenantId)
    {
        _limiter = limiter;
        _tenantId = tenantId;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        _limiter.Release(_tenantId);
    }
}
