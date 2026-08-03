namespace AgentPrism;

/// <summary>
/// Calistirma istatistigi sorgusunun filtresi.
/// </summary>
/// <remarks>
/// Sayfalama yoktur: sonuc her zaman tek bir ozet satiridir. Hacmi sinirlamak
/// icin <see cref="StartedAfter"/> kullanilir.
/// </remarks>
public sealed record RunStatisticsQuery
{
    /// <summary>Yalnizca bu agent'in calistirmalari sayilsin.</summary>
    public string? AgentName { get; init; }

    /// <summary>Kiraci filtresi. Bos birakilirsa gecerli kiraci kullanilir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Yalnizca bu andan sonra baslayan calistirmalar sayilsin.</summary>
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>
    /// Agent kirilimida en cok kac satir dondurulecegi. Calistirma sayisi en
    /// yuksek olan agent'lar oncelikli doner.
    /// </summary>
    public int MaxAgents { get; init; } = 20;
}

/// <summary>
/// Bir zaman araligindaki calistirmalarin ozeti.
/// </summary>
/// <remarks>
/// <para>
/// Token toplamlari yalnizca kullanim bilgisi bildiren calistirmalari kapsar.
/// Bir saglayici kullanim dondurmezse o calistirma sayilara girer ancak
/// token toplamina katki vermez.
/// </para>
/// <para>
/// Maliyet (Faz 20) yalniz fiyat yapilandirildiginda doludur — bkz.
/// <see cref="TotalCost"/>. Fiyat AgentPrism'e gomulu degildir (karar K-032):
/// model kataloğu veya <c>AgentPrism:Pricing</c> yapilandirmasindan gelir.
/// </para>
/// </remarks>
public sealed record RunStatistics
{
    /// <summary>Filtreye uyan toplam calistirma sayisi.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>Basariyla tamamlanan calistirma sayisi.</summary>
    public required long CompletedRuns { get; init; }

    /// <summary>Hata ile biten calistirma sayisi.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>Iptal edilen calistirma sayisi.</summary>
    public required long CanceledRuns { get; init; }

    /// <summary>Hala calisan calistirma sayisi.</summary>
    public required long RunningRuns { get; init; }

    /// <summary>Insan girdisi bekleyen calistirma sayisi.</summary>
    /// <remarks>
    /// Yalnizca workflow calistirmalarinda olusur. Ayri sayilir cunku boyle bir
    /// calistirma ne calisiyor ne de sonuclanmistir; bir kovaya zorla yazilsaydi
    /// alt toplamlar <see cref="TotalRuns"/> ile tutmazdi.
    /// </remarks>
    public long AwaitingInputRuns { get; init; }

    /// <summary>Girdi token toplami.</summary>
    public long InputTokens { get; init; }

    /// <summary>Cikti token toplami.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Toplam token.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Agent bazinda kirilim.</summary>
    public IReadOnlyList<RunAgentStatistics> ByAgent { get; init; } = [];

    /// <summary>
    /// Model bazinda kirilim. Model adi bilinmeyen calistirmalar bu listede
    /// yer almaz; toplamlarda ise sayilirlar.
    /// </summary>
    public IReadOnlyList<RunModelStatistics> ByModel { get; init; } = [];

    /// <summary>Tanim surumu bazinda kirilim. Surumu bilinmeyen calistirmalar bu listede yer almaz.</summary>
    public IReadOnlyList<RunVersionStatistics> ByVersion { get; init; } = [];

    /// <summary>
    /// Toplam maliyet. Fiyati tanimsiz bir model varsa o calistirmalarin
    /// maliyeti bu toplama <strong>katilmaz</strong> (yalniz fiyati bilinenler
    /// toplanir); kac calistirmanin dislandigi <see cref="RunsWithUnknownPricing"/>'de
    /// gorunur. Hic fiyatlandirilmis calistirma yoksa <see langword="null"/>.
    /// </summary>
    public decimal? TotalCost { get; init; }

    /// <summary>Para birimi. <see cref="TotalCost"/> doluysa doludur.</summary>
    public string? Currency { get; init; }

    /// <summary>Modeli bilinen ama fiyati tanimsiz olan calistirma sayisi.</summary>
    public long RunsWithUnknownPricing { get; init; }

    /// <summary>
    /// Sonuclanmis calistirmalar icindeki hata orani (0–1). Hic sonuclanmis
    /// calistirma yoksa <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// Payda <see cref="TotalRuns"/> degil, <em>sonuclanmis</em> calistirmalardir.
    /// Devam eden bir calistirmanin basarili mi basarisiz mi olacagi henuz
    /// bilinmez; paydaya katmak orani yapay olarak dusururdu.
    /// </remarks>
    public double? ErrorRate
    {
        get
        {
            var settled = CompletedRuns + FailedRuns + CanceledRuns;
            return settled == 0 ? null : (double)FailedRuns / settled;
        }
    }
}

/// <summary>Bir modelin calistirma ozeti. Maliyet hesabinin girdisidir.</summary>
public sealed record RunModelStatistics
{
    /// <summary>Model adi.</summary>
    public required string ModelId { get; init; }

    /// <summary>Bu modelle yapilan toplam calistirma sayisi.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>Girdi token toplami.</summary>
    public long InputTokens { get; init; }

    /// <summary>Cikti token toplami.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Toplam token.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Bu modelle yapilan calistirmalarin toplam maliyeti. Fiyat tanimsizsa <see langword="null"/>.</summary>
    public decimal? TotalCost { get; init; }
}

/// <summary>
/// Bir zaman kovasindaki calistirma ozeti. <c>/api/stats/timeseries</c>'in
/// sonuc birimidir; bos kovalar da doner (sifir olay ile).
/// </summary>
public sealed record TimeSeriesPoint
{
    /// <summary>Kovanin baslangic zamani (UTC).</summary>
    public required DateTimeOffset Bucket { get; init; }

    /// <summary>Bu kovada baslayan calistirma sayisi.</summary>
    public long Runs { get; init; }

    /// <summary>Bu kovada hata ile biten calistirma sayisi.</summary>
    public long FailedRuns { get; init; }

    /// <summary>Girdi token toplami.</summary>
    public long InputTokens { get; init; }

    /// <summary>Cikti token toplami.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Toplam maliyet. Hic fiyatlandirilmis calistirma yoksa <see langword="null"/>.</summary>
    public decimal? Cost { get; init; }

    /// <summary>Sonuclanmis calistirmalarin ortalama suresi (milisaniye).</summary>
    public double? AverageDurationMs { get; init; }
}

/// <summary>Bir tanim surumunun calistirma ozeti.</summary>
public sealed record RunVersionStatistics
{
    /// <summary>Bu kirilimin ait oldugu agent adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Tanim surumu.</summary>
    public required int Version { get; init; }

    /// <summary>Bu surumle yapilan toplam calistirma sayisi.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>Bu surumun hata ile biten calistirma sayisi.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>Bu surumun toplam token kullanimi.</summary>
    public long TotalTokens { get; init; }
}

/// <summary>Bir agent'in calistirma ozeti.</summary>
public sealed record RunAgentStatistics
{
    /// <summary>Agent adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Bu agent'in toplam calistirma sayisi.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>Bu agent'in hata ile biten calistirma sayisi.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>Bu agent'in toplam token kullanimi.</summary>
    public long TotalTokens { get; init; }
}
