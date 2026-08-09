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

/// <summary>
/// <c>GET /api/experiments/{name}/canary</c> yaniti — kural VE guncel degerlendirme
/// bir arada.
/// </summary>
/// <remarks>
/// <see cref="Evaluation"/> KALICI DEGILDIR: her cagrida <c>CanaryEvaluator</c> ile
/// CANLI hesaplanir (bkz. <see cref="CanaryEvaluation"/> sinif belgesi).
/// </remarks>
public sealed record ExperimentCanaryResponse
{
    /// <summary>Tanimli kanarya kurali. Hic tanimlanmamissa <see langword="null"/>.</summary>
    public CanaryPolicy? Policy { get; init; }

    /// <summary>Kuralin guncel degerlendirmesi. <see cref="Policy"/> <see langword="null"/> ise <see langword="null"/>.</summary>
    public CanaryEvaluation? Evaluation { get; init; }
}
