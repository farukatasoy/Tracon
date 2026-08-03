namespace AgentPrism;

/// <summary>Bir deneyin sonuc sorgusu.</summary>
public sealed record ExperimentResultsQuery
{
    /// <summary>Sonuclari istenen deneyin kimligi.</summary>
    public required Guid ExperimentId { get; init; }

    /// <summary>Kiraci filtresi. Bos birakilirsa gecerli kiraci kullanilir.</summary>
    public string? TenantId { get; init; }
}

/// <summary>Bir deney kolunun calistirma sonuclarinin ozeti (K-041: depoda hesaplanir).</summary>
public sealed record ExperimentVariantResult
{
    /// <summary>Kolun adi.</summary>
    public required string Variant { get; init; }

    /// <summary>Kolun sundugu tanim surumu.</summary>
    public required int Version { get; init; }

    /// <summary>Bu kola atanmis toplam calistirma sayisi.</summary>
    public required long TotalRuns { get; init; }

    /// <summary>Basariyla tamamlanan calistirma sayisi.</summary>
    public required long CompletedRuns { get; init; }

    /// <summary>Hata ile biten calistirma sayisi.</summary>
    public required long FailedRuns { get; init; }

    /// <summary>Iptal edilen calistirma sayisi.</summary>
    public required long CanceledRuns { get; init; }

    /// <summary>Girdi token toplami.</summary>
    public long InputTokens { get; init; }

    /// <summary>Cikti token toplami.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Toplam token.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Bu kolun toplam maliyeti. Fiyat tanimsizsa <see langword="null"/>.</summary>
    public decimal? TotalCost { get; init; }

    /// <summary>Para birimi. <see cref="TotalCost"/> doluysa doludur.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Sonuclanmis calistirmalarin ortalama suresi (milisaniye). Hic sonuclanmamissa
    /// <see langword="null"/>.
    /// </summary>
    public double? AverageDurationMs { get; init; }

    /// <summary>
    /// Sonuclanmis calistirmalar icindeki hata orani (0-1). <see cref="RunStatistics.ErrorRate"/>
    /// ile ayni gerekce.
    /// </summary>
    public double? ErrorRate
    {
        get
        {
            var settled = CompletedRuns + FailedRuns + CanceledRuns;
            return settled == 0 ? null : (double)FailedRuns / settled;
        }
    }
}
