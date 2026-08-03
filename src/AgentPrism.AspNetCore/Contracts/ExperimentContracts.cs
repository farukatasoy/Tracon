namespace AgentPrism;

/// <summary>Bir deney olusturma/guncelleme istegi. Ad yoldan gelir.</summary>
public sealed record ExperimentSaveRequest
{
    /// <summary>Trafigi bolunecek agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Deneyin kollari. Agirlik toplami 100 olmalidir.</summary>
    public required IReadOnlyList<ExperimentVariant> Variants { get; init; }
}

/// <summary>Bir deneyin varyant bazinda sonuc gorunumu.</summary>
public sealed record ExperimentResultsResponse
{
    /// <summary>Deneyin kendisi.</summary>
    public required Experiment Experiment { get; init; }

    /// <summary>Kol bazinda sonuclar.</summary>
    public required IReadOnlyList<ExperimentVariantResult> Results { get; init; }
}
