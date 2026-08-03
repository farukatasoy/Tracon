namespace AgentPrism;

/// <summary>AgentPrism uclarina uygulanan hiz siniri ayarlari — Faz 21.</summary>
/// <remarks>
/// <para>
/// <c>AgentPrism:RateLimit</c> yapilandirma bolumunden okunur.
/// </para>
/// <para>
/// 🚨 <strong>Varsayilan kapalidir</strong> (K-165). Bir kutuphane tuketicisinin
/// trafigini bilmez; acik gelen bir varsayilan, yukseltme yapan bir kurulumun
/// canli trafigini sessizce <c>429</c> ile karsilardi. Onerilen degerler
/// README'de yazar.
/// </para>
/// <para>
/// Hiz siniri <strong>kota degildir</strong>: saniye/dakika olceginde ani yuku
/// duzlestirir ve bellekte yasar. Gun/ay olcegindeki toplam tuketim sinirlamasi
/// <see cref="AgentPrismQuotaOptions"/> ile yapilir ve veritabaninda sayilir
/// (K-158).
/// </para>
/// </remarks>
public sealed class AgentPrismRateLimitOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:RateLimit";

    /// <summary>Hiz siniri etkin mi. Varsayilan <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Bir pencerede izin verilen istek sayisi.</summary>
    public int PermitLimit { get; set; } = 60;

    /// <summary>Pencerenin uzunlugu.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Sinir asilinca kuyruga alinacak istek sayisi. <c>0</c> ise istek hemen
    /// <c>429</c> alir.
    /// </summary>
    public int QueueLimit { get; set; }

    /// <summary>
    /// Sinirin hangi anahtara gore bolundugu. Varsayilan kiraci bazlidir.
    /// </summary>
    public RateLimitPartitionKind Partition { get; set; } = RateLimitPartitionKind.Tenant;
}

/// <summary>Hiz sinirinin hangi anahtara gore bolundugu.</summary>
public enum RateLimitPartitionKind
{
    /// <summary>Her kiraci kendi kotasini alir.</summary>
    Tenant = 0,

    /// <summary>Sinir tum kurulum icin ortaktir.</summary>
    Global = 1,
}
