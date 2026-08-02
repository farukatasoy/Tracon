namespace AgentPrism;

/// <summary>Bir calistirmanin ozeti. Olay akisinin basligi gibidir.</summary>
public sealed record RunRecord
{
    /// <summary>Calistirma kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Calistirilan agent'in adi.</summary>
    public required string AgentName { get; init; }

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
}
