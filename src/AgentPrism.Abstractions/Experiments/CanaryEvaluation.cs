namespace AgentPrism;

/// <summary>
/// Bir kanarya politikasinin, kanarya ve kontrol kolunun GUNCEL sonuclarina karsi
/// degerlendirilmesinin sonucu.
/// </summary>
/// <remarks>
/// Bu tip <strong>kalici degildir</strong> — <c>GET /api/experiments/{name}/canary</c>
/// her cagrida <c>CanaryEvaluator</c>'i guncel <see cref="ExperimentVariantResult"/>
/// verisiyle yeniden calistirip doner. Kanarya kolunun calistirma sonuclari zaten
/// deneyin baslangicindan beri birikir; ayrica bir "degerlendirme gecmisi" tablosu
/// ayni bilgiyi ikinci kez tutardi.
/// </remarks>
public sealed record CanaryEvaluation
{
    /// <summary>Degerlendirmenin sonucu.</summary>
    public required CanaryDecisionKind Decision { get; init; }

    /// <summary>Kararin insan tarafindan okunabilir gerekcesi.</summary>
    public required string Reason { get; init; }

    /// <summary>Kanarya kolunun guncel sonuclari. Hic calistirma yoksa <see langword="null"/>.</summary>
    public ExperimentVariantResult? Canary { get; init; }

    /// <summary>Kontrol kolunun guncel sonuclari. Hic calistirma yoksa <see langword="null"/>.</summary>
    public ExperimentVariantResult? Control { get; init; }

    /// <summary>Degerlendirmenin yapildigi an (UTC).</summary>
    public required DateTimeOffset EvaluatedAt { get; init; }
}
