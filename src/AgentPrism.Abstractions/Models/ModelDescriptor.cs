namespace AgentPrism;

/// <summary>Bir modelin yetenekleri ve sinirlari.</summary>
public sealed record ModelDescriptor
{
    /// <summary>Model adi. <see cref="ModelBinding.Model"/> bu degerle eslesir.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Baglam penceresinin token kapasitesi.</summary>
    public int? ContextWindowTokens { get; init; }

    /// <summary>Tek yanitta uretilebilecek ust token sayisi.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Akisli yaniti destekliyor mu.</summary>
    public bool SupportsStreaming { get; init; } = true;

    /// <summary>Tool cagrisini destekliyor mu.</summary>
    public bool SupportsTools { get; init; } = true;

    /// <summary>Akil yurutme cabasi ayarini destekliyor mu.</summary>
    public bool SupportsReasoning { get; init; }

    /// <summary>Milyon girdi token'i basina maliyet. Yalnizca raporlama icindir.</summary>
    public decimal? InputCostPerMillionTokens { get; init; }

    /// <summary>Milyon cikti token'i basina maliyet. Yalnizca raporlama icindir.</summary>
    public decimal? OutputCostPerMillionTokens { get; init; }
}

/// <summary>Bir saglayicinin arayuze gosterilen tanimi.</summary>
public sealed record ModelProviderDescriptor
{
    /// <summary>Saglayici adi.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Bu saglayicinin sundugu modeller.</summary>
    public IReadOnlyList<ModelDescriptor> Models { get; init; } = [];
}
