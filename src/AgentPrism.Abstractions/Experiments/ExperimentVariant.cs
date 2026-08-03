namespace AgentPrism;

/// <summary>Bir deneyin tek bir kolunu tanimlar: hangi tanim surumu, hangi agirlikla.</summary>
public sealed record ExperimentVariant
{
    /// <summary>Kolun adi. Ornek: <c>"control"</c>, <c>"v3"</c>. Deney icinde benzersiz olmalidir.</summary>
    public required string Name { get; init; }

    /// <summary>Sunulacak <see cref="AgentDefinition.Version"/> numarasi.</summary>
    public required int Version { get; init; }

    /// <summary>
    /// Trafik agirligi, 0-100 arasi. Bir deneydeki tum varyantlarin agirlik
    /// toplami tam 100 olmalidir; aksi halde kayit reddedilir (bkz. <see cref="Experiment"/>).
    /// </summary>
    public required int Weight { get; init; }
}
