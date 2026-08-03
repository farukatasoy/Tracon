namespace AgentPrism;

/// <summary>Bir calistirmanin ozeti. Olay akisinin basligi gibidir.</summary>
public sealed record RunRecord
{
    /// <summary>Calistirma kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Calistirilan agent'in adi. Workflow calistirmalarinda workflow'un adidir:
    /// mevcut listeler, istatistikler ve arayuz bu sutunu okur ve bos birakmak
    /// workflow satirlarini adsiz gosterirdi.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>Bu satirin bir agent'i mi yoksa bir workflow'u mu kaydettigi.</summary>
    public RunKind Kind { get; init; }

    /// <summary>
    /// Calistirilan workflow'un adi. Yalnizca <see cref="RunKind.Workflow"/>
    /// satirlarinda dolu.
    /// </summary>
    public string? WorkflowName { get; init; }

    /// <summary>Calistirmanin guncel durumu.</summary>
    public required RunStatus Status { get; init; }

    /// <summary>Baslangic zamani (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Bitis zamani (UTC). Calistirma surerken <see langword="null"/>.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Calistirmanin ait oldugu kiraci.</summary>
    public string? TenantId { get; init; }

    /// <summary>Kullanilan oturumun kimligi.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Calistirmada kullanilan modelin adi. Maliyet ve model kirilimi raporlari
    /// bunu gerektirir. Agent tanimi model tasimiyorsa <see langword="null"/>.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>Akisli calistirma miydi.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>Token kullanimi. Saglayici bildirmediyse <see langword="null"/>.</summary>
    public RunUsage? Usage { get; init; }

    /// <summary>Hata bilgisi. Yalnizca <see cref="RunStatus.Failed"/> durumunda dolu.</summary>
    public RunError? Error { get; init; }

    /// <summary>Bu calistirmaya yazilmis olay sayisi.</summary>
    public long EventCount { get; init; }

    /// <summary>
    /// Bu calistirmayi baslatan calistirmanin kimligi. Kok calistirmada
    /// <see langword="null"/>.
    /// </summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>
    /// Agacin kokundeki calistirmanin kimligi. Kok calistirmada
    /// <see langword="null"/>; alt calistirmalarda her zaman dolu.
    /// </summary>
    /// <remarks>
    /// Denormalize edilmistir: bir agacin tamami bu alan uzerinden tek indeksli
    /// bir sorguyla cekilir, <see cref="ParentRunId"/> uzerinden ozyinelemeli
    /// CTE gerekmez.
    /// </remarks>
    public Guid? RootRunId { get; init; }

    /// <summary>Agactaki derinlik. Kok calistirma 0'dir.</summary>
    public int Depth { get; init; }

    /// <summary>Bu calistirmanin olctugu tanim surumu. Bilinmiyorsa <see langword="null"/>.</summary>
    public int? AgentVersion { get; init; }

    /// <summary>Bu calistirmanin bagli oldugu deneyin kimligi. Deney disi calistirmada <see langword="null"/>.</summary>
    public Guid? ExperimentId { get; init; }

    /// <summary>Bu calistirmanin atandigi deney kolunun adi. Deney disi calistirmada <see langword="null"/>.</summary>
    public string? Variant { get; init; }

    /// <summary>Bu calistirmanin <em>dogrudan</em> alt calistirma sayisi.</summary>
    public int ChildRunCount { get; init; }

    /// <summary>
    /// Bu calistirmanin ve altindaki tum calistirmalarin toplam token kullanimi.
    /// </summary>
    /// <remarks>
    /// <see cref="Usage"/> ile <strong>toplanmaz</strong>: bu deger zaten
    /// <see cref="Usage"/> degerini icerir. Alt calistirmasi olmayan bir
    /// calistirmada ikisi esittir. Arayuz ikisini ayri sutunda gosterir, cunku
    /// "bu calistirma ne harcadi" ile "bu istek toplamda ne harcadi" farkli
    /// sorulardir.
    /// </remarks>
    public RunUsage? TreeUsage { get; init; }

    /// <summary>
    /// Bu calistirmanin kendi maliyeti. Model bilinmiyorsa <see langword="null"/>;
    /// model biliniyorsa fiyat tanimsiz olsa bile dolu gelir.
    /// </summary>
    public RunCost? Cost { get; init; }

    /// <summary>
    /// Bu calistirmanin ve altindaki tum calistirmalarin toplam maliyeti.
    /// </summary>
    /// <remarks>
    /// <see cref="Cost"/> ile <strong>toplanmaz</strong>: bu deger zaten kendi
    /// maliyetini icerir (bkz. <see cref="TreeUsage"/> ile ayni gerekce). Alt
    /// calistirmasi olmayan bir calistirmada ikisi esdegerdir.
    /// </remarks>
    public RunTreeCost? TreeCost { get; init; }
}
