using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Bir model saglayicisinin isteğe bagli saglik denetimi sozlesmesi.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IModelProvider"/> arayuzune uye <strong>eklenmez</strong> — bu, tuketicinin
/// kendi <see cref="IModelProvider"/> uygulamasini kirar. Bu ayri arayuz isteğe
/// bagli bir genisleme noktasidir: bir saglayici bunu uygulamiyorsa durumu
/// <see cref="ModelProviderHealthStatus.Unknown"/>'dir ve bu bir hata degildir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K4 (var olan bir arayuze uye eklemek
/// tuketicinin uygulamasini kirar).
/// </para>
/// <para>
/// Denetim <strong>ucretli bir model cagrisi yapmamalidir</strong>. OpenAI ve uyumlu
/// sunucular <c>GET {endpoint}/models</c> ucunu sunar; bu uc model adlarini doner ve
/// ucret uretmez.
/// </para>
/// </remarks>
public interface IModelProviderHealthCheck
{
    /// <summary>Saglayicinin erisilebilirligini denetler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Denetim sonucu.</returns>
    ValueTask<ModelProviderHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>Bir model saglayicisinin son denetimdeki durumu.</summary>
public sealed record ModelProviderHealth
{
    /// <summary>Saglayici adi.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Denetim sonucu.</summary>
    public required ModelProviderHealthStatus Status { get; init; }

    /// <summary>
    /// Kisa hata nedeni. <strong>Sir tasimaz</strong>: yanit govdesi, basliklar veya
    /// API anahtari hicbir kosulda buraya yazilmaz; yalnizca HTTP durum kodu ve kisa
    /// bir aciklama.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>Denetim istegi ne kadar surdu.</summary>
    public TimeSpan? Latency { get; init; }

    /// <summary>Denetimin yapildigi zaman.</summary>
    public DateTimeOffset CheckedAt { get; init; }

    /// <summary>Sunucunun bildirdigi model adlari. Denetim basarisizsa bostur.</summary>
    public IReadOnlyList<string> Models { get; init; } = [];
}

/// <summary>Bir saglayicinin denetlenen erisilebilirlik durumu.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir (karar K-040): sayisal deger kablo sozlesmesi olarak
/// okunaksiz olurdu ve enum sirasi degisirse sessizce kirilirdi.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ModelProviderHealthStatus>))]
public enum ModelProviderHealthStatus
{
    /// <summary>Saglayici <see cref="IModelProviderHealthCheck"/> uygulamiyor.</summary>
    Unknown,

    /// <summary>Saglayici erisilebilir.</summary>
    Healthy,

    /// <summary>Saglayici erisilebilir ama beklenmedik bir yanit verdi.</summary>
    Degraded,

    /// <summary>Saglayici erisilemiyor.</summary>
    Unhealthy,
}
