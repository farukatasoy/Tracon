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
/// <strong>Maliyet burada yoktur.</strong> Faz 6'da <c>runs</c> tablosuna model
/// adi eklendi ve <see cref="ByModel"/> kirilimi geldi; para birimi cinsinden
/// maliyet ise model basina fiyat listesi ister. OpenAI <c>/v1/models</c> yalnizca
/// <c>id</c> dondurur (karar K-032), bu yuzden fiyat AgentPrism'e gomulemez.
/// Token kirilimi verilir; fiyatlandirmayi tuketici kendi listesiyle yapar.
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
