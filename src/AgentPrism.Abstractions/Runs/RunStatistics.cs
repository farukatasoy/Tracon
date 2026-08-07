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
    /// Hata sinifina gore kirilim. Yalnizca hata ile biten calistirmalar
    /// sayilir. Hata sinifi eklenmeden once yazilmis satirlar <c>Unknown</c>
    /// kovasinda gorunur (K-014 -- gecmis satirlar geriye donuk doldurulmaz).
    /// </summary>
    public IReadOnlyList<RunErrorStatistics> ByErrorClass { get; init; } = [];

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

    /// <summary>
    /// En az bir <see cref="RunScore"/> alan (calistirma veya mesaj duzeyinde)
    /// calistirma sayisi. Eval calistirmalari (<see cref="RunKind.Eval"/>)
    /// <see cref="TotalRuns"/> ile ayni gerekceyle haric tutulur (K-141).
    /// </summary>
    public long ScoredRuns { get; init; }

    /// <summary>
    /// <see cref="RunScoreKind.Binary"/> turundeki puanlarin olumlu orani (0–1).
    /// Yildiz puanlari bu orana katilmaz — iki turun ortalamasi anlamsiz olurdu.
    /// Hic ikili puan yoksa <see langword="null"/>.
    /// </summary>
    public double? PositiveRate { get; init; }
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

/// <summary>Bir hata sinifinin ozeti.</summary>
public sealed record RunErrorStatistics
{
    /// <summary>Hata sinifi.</summary>
    public required RunErrorClass Class { get; init; }

    /// <summary>Bu sinifa dusen toplam calistirma sayisi.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>Bu sinifin en sik uc kumesi, sayiya gore azalan sirada.</summary>
    public IReadOnlyList<RunErrorCluster> TopClusters { get; init; } = [];
}

/// <summary>Ayni parmak izini paylasan calistirmalarin ozeti.</summary>
public sealed record RunErrorCluster
{
    /// <summary>Normallestirilmis mesajin ozeti.</summary>
    public required string Fingerprint { get; init; }

    /// <summary>Bu kumedeki calistirma sayisi.</summary>
    public required long Count { get; init; }

    /// <summary>Kumedeki en son goruntuye ait ham hata mesaji.</summary>
    public required string SampleMessage { get; init; }

    /// <summary>Kumedeki en son goruntunun calistirma kimligi.</summary>
    public required Guid SampleRunId { get; init; }

    /// <summary>Bu kumenin en son goruldugu an (UTC).</summary>
    public required DateTimeOffset LastSeenAt { get; init; }
}
