namespace AgentPrism;

/// <summary>Anlamsal arama istegi.</summary>
public sealed record VectorSearchRequest
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Koleksiyon adi.</summary>
    public required string Collection { get; init; }

    /// <summary>Sorgu metninin gomusu.</summary>
    public required ReadOnlyMemory<float> QueryEmbedding { get; init; }

    /// <summary>Kac sonuc dondurulur.</summary>
    public int Top { get; init; } = 5;

    /// <summary>
    /// En buyuk kabul edilen kosinus mesafesi (0 = ayni, 2 = zit).
    /// <see langword="null"/> ise mesafe suzgeci uygulanmaz.
    /// </summary>
    public double? MaxDistance { get; init; }
}
