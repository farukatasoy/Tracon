namespace AgentPrism;

/// <summary>Bir calistirmanin token kullanimi.</summary>
public sealed record RunUsage
{
    /// <summary>Girdi token sayisi.</summary>
    public long? InputTokens { get; init; }

    /// <summary>Cikti token sayisi.</summary>
    public long? OutputTokens { get; init; }

    /// <summary>Toplam token sayisi.</summary>
    public long? TotalTokens { get; init; }
}

/// <summary>Basarisiz bir calistirmanin hata bilgisi.</summary>
public sealed record RunError
{
    /// <summary>Istisna tipinin adi.</summary>
    public required string Type { get; init; }

    /// <summary>Hata mesaji.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Bir calistirmanin maliyeti. Calistirma bittiginde bir kez hesaplanir ve
/// yazilir (fiyat anlik goruntusu) — fiyat listesi sonradan degisirse gecmis
/// deger degismez (bkz. <c>docs/20-MALIYET-VE-GOSTERGE-PANELI.md</c> bolum 20.2).
/// </summary>
public sealed record RunCost
{
    /// <summary>Girdi token maliyeti. Fiyat tanimsizsa <see langword="null"/>.</summary>
    public decimal? InputCost { get; init; }

    /// <summary>Cikti token maliyeti. Fiyat tanimsizsa <see langword="null"/>.</summary>
    public decimal? OutputCost { get; init; }

    /// <summary>Para birimi. <c>AgentPrism:Pricing:Currency</c>'den gelir.</summary>
    public string? Currency { get; init; }

    /// <summary>Fiyatin nereden geldigi.</summary>
    public required PricingSource Source { get; init; }
}

/// <summary>
/// Bir calistirma agacinin (kok + tum alt calistirmalar) toplam maliyeti.
/// </summary>
/// <remarks>
/// <see cref="RunRecord.Cost"/> ile <strong>toplanmaz</strong>: bu deger zaten
/// kendi maliyetini icerir (bkz. <see cref="RunRecord.TreeUsage"/> ile ayni
/// gerekce). Arayuz ikisini ayri sutunda gosterir.
/// </remarks>
public sealed record RunTreeCost
{
    /// <summary>Agactaki toplam girdi maliyeti.</summary>
    public decimal? InputCost { get; init; }

    /// <summary>Agactaki toplam cikti maliyeti.</summary>
    public decimal? OutputCost { get; init; }

    /// <summary>Para birimi.</summary>
    public string? Currency { get; init; }

    /// <summary>Agacta fiyati tanimsiz kac calistirma oldugu.</summary>
    public long RunsWithUnknownPricing { get; init; }
}

/// <summary>Yeni bir calistirma baslatmak icin gereken bilgiler.</summary>
public sealed record RunStartInfo
{
    /// <summary>Calistirma kimligi. Cagiran taraf uretir, boylece kimligi hemen bilir.</summary>
    public required Guid RunId { get; init; }

    /// <summary>
    /// Calistirilan agent'in adi. Workflow calistirmalarinda workflow'un adi
    /// yazilir; gerekce <see cref="RunRecord.AgentName"/> aciklamasindadir.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>Bu satirin bir agent'i mi yoksa bir workflow'u mu kaydettigi.</summary>
    public RunKind Kind { get; init; }

    /// <summary>Calistirilan workflow'un adi. Agent calistirmalarinda <see langword="null"/>.</summary>
    public string? WorkflowName { get; init; }

    /// <summary>Baslangic zamani (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public string? TenantId { get; init; }

    /// <summary>Oturum kimligi.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Kullanilacak modelin adi. Agent tanimindan cozulur; bilinmiyorsa
    /// <see langword="null"/>.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>Akisli calistirma mi.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>Bu calistirmayi baslatan calistirmanin kimligi. Kokte <see langword="null"/>.</summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>Agacin kokundeki calistirmanin kimligi. Kokte <see langword="null"/>.</summary>
    public Guid? RootRunId { get; init; }

    /// <summary>Agactaki derinlik. Kok calistirma 0'dir.</summary>
    public int Depth { get; init; }

    /// <summary>Bu calistirmanin olctugu tanim surumu. Bilinmiyorsa <see langword="null"/>.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Bu calistirmanin bagli oldugu deneyin kimligi. Deney disi calistirmada <see langword="null"/>.</summary>
    public Guid? ExperimentId { get; init; }

    /// <summary>Bu calistirmanin atandigi deney kolunun adi. Deney disi calistirmada <see langword="null"/>.</summary>
    public string? Variant { get; init; }
}

/// <summary>Bir calistirmayi sonlandirmak icin gereken bilgiler.</summary>
public sealed record RunCompletion
{
    /// <summary>Calistirma kimligi.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Son durum.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Bitis zamani (UTC).</summary>
    public required DateTimeOffset CompletedAt { get; init; }

    /// <summary>Yazilmis toplam olay sayisi.</summary>
    public long EventCount { get; init; }

    /// <summary>Token kullanimi.</summary>
    public RunUsage? Usage { get; init; }

    /// <summary>Hata bilgisi. Yalnizca <see cref="RunStatus.Failed"/> durumunda dolu.</summary>
    public RunError? Error { get; init; }

    /// <summary>
    /// Hesaplanan maliyet. Model bilinmiyorsa (kod agent'i vb.) <see langword="null"/>;
    /// model biliniyorsa fiyat tanimsiz olsa bile dolu gelir (bkz. <see cref="RunCost"/>).
    /// </summary>
    public RunCost? Cost { get; init; }
}

/// <summary>Calistirma listesini filtrelemek icin sorgu.</summary>
public sealed record RunQuery
{
    /// <summary>Yalnizca bu agent'in calistirmalarini getirir.</summary>
    public string? AgentName { get; init; }

    /// <summary>Yalnizca bu durumdaki calistirmalari getirir.</summary>
    public RunStatus? Status { get; init; }

    /// <summary>Yalnizca bu kiracinin calistirmalarini getirir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Yalnizca bu oturuma ait calistirmalari getirir.</summary>
    public string? SessionId { get; init; }

    /// <summary>Bu andan sonra baslayan calistirmalari getirir (UTC).</summary>
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>
    /// Yalnizca kok calistirmalari getirir. Varsayilan <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Varsayilan bilerek <see langword="true"/>'dur: bir agent baska agent'lari
    /// cagirdiginda her alt cagri ayri bir <c>runs</c> satiri uretir ve liste
    /// kullanicinin baslatmadigi calistirmalarla dolar. Alt calistirmalar kendi
    /// koklerinin detayinda agac olarak gorunur; tam liste gerekiyorsa deger
    /// <see langword="false"/> yapilir.
    /// </remarks>
    public bool OnlyRootRuns { get; init; } = true;

    /// <summary>
    /// Yalnizca bu calistirmanin dogrudan alt calistirmalarini getirir.
    /// </summary>
    /// <remarks>
    /// Deger verildiginde <see cref="OnlyRootRuns"/> yok sayilir: alt calistirma
    /// istegi ile kok filtresi mantiksal olarak celisir ve sessizce bos liste
    /// donmek hata ayiklanmasi zor bir davranistir.
    /// </remarks>
    public Guid? ParentRunId { get; init; }

    /// <summary>Yalnizca bu agacin calistirmalarini getirir (kok dahil).</summary>
    public Guid? RootRunId { get; init; }

    /// <summary>Atlanacak kayit sayisi.</summary>
    public int Skip { get; init; }

    /// <summary>Getirilecek ust kayit sayisi.</summary>
    public int Take { get; init; } = 50;
}

/// <summary>Zaman serisi sorgusunun filtresi.</summary>
public sealed record RunTimeSeriesQuery
{
    /// <summary>Araligin baslangici (UTC, dahil).</summary>
    public required DateTimeOffset From { get; init; }

    /// <summary>Araligin bitisi (UTC, haric).</summary>
    public required DateTimeOffset To { get; init; }

    /// <summary>Kova genisligi. Varsayilan saatlik.</summary>
    public TimeSeriesBucket Bucket { get; init; } = TimeSeriesBucket.Hour;

    /// <summary>Yalnizca bu agent'in calistirmalarini sayar.</summary>
    public string? AgentName { get; init; }

    /// <summary>Yalnizca bu modelin calistirmalarini sayar.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Yalnizca bu turdeki calistirmalari sayar. <see langword="null"/> ise
    /// tum turler dahildir — <see cref="IRunStore.GetStatisticsAsync"/>'in
    /// aksine bu sorgu Eval/Workflow calistirmalarini varsayilan olarak
    /// haric tutmaz (bkz. <c>docs/KARARLAR.md</c> K-152).
    /// </summary>
    public RunKind? Kind { get; init; }

    /// <summary>Kiraci filtresi. Bos birakilirsa gecerli kiraci kullanilir.</summary>
    public string? TenantId { get; init; }
}

/// <summary>Bir maliyet yeniden hesaplama isleminin sonucu.</summary>
public sealed record RunCostRecalculationResult
{
    /// <summary>Islenen (modeli ve kullanimi olan) calistirma sayisi.</summary>
    public required long RunsConsidered { get; init; }

    /// <summary>Fiyati basariyla cozulup guncellenen calistirma sayisi.</summary>
    public required long RunsUpdated { get; init; }

    /// <summary>Islendikten sonra hala fiyati tanimsiz kalan calistirma sayisi.</summary>
    public required long RunsStillUnknown { get; init; }
}
