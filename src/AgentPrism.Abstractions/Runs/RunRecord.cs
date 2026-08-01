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

    /// <summary>Akisli calistirma miydi.</summary>
    public bool IsStreaming { get; init; }

    /// <summary>Token kullanimi. Saglayici bildirmediyse <see langword="null"/>.</summary>
    public RunUsage? Usage { get; init; }

    /// <summary>Hata bilgisi. Yalnizca <see cref="RunStatus.Failed"/> durumunda dolu.</summary>
    public RunError? Error { get; init; }

    /// <summary>Bu calistirmaya yazilmis olay sayisi.</summary>
    public long EventCount { get; init; }
}
