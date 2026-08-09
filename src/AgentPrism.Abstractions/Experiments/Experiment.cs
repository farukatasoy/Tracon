namespace AgentPrism;

/// <summary>
/// Ayni agent'in iki veya daha fazla tanim surumu arasinda trafigi bolen bir A/B deneyi.
/// </summary>
/// <remarks>
/// <para>
/// Deney yalnizca <strong>ayni agent'in surumleri</strong> arasinda olabilir; farkli
/// agent'lar arasi deney bu fazin kapsami disindadir (ad cozumlemesini karmasiklastirir).
/// </para>
/// <para>
/// Kod kaynakli agent'larda (<see cref="AgentDefinitionOrigin.Code"/>) deney kurulamaz:
/// surum gecmisi yoktur (karar K-003). Uc bunu 400 ile acikca soyler.
/// </para>
/// </remarks>
public sealed record Experiment
{
    /// <summary>Deney kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Deneyin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Deney adi. Kiraci icinde benzersizdir ve API yollarinda anahtar olarak kullanilir.</summary>
    public required string Name { get; init; }

    /// <summary>Trafigi bolunen agent'in adi.</summary>
    public required string AgentName { get; init; }

    /// <summary>Deneyin kollari. Agirlik toplami 100 olmalidir.</summary>
    public required IReadOnlyList<ExperimentVariant> Variants { get; init; }

    /// <summary>Deneyin guncel durumu.</summary>
    public ExperimentStatus Status { get; init; } = ExperimentStatus.Draft;

    /// <summary>
    /// Rezerve alan. Bu fazda calisma zamani atamasi tarafindan <strong>okunmaz</strong>;
    /// atama anahtari her zaman oturum kimligidir (yoksa calistirma kimligi). Gelecekte
    /// oturum disi atama stratejileri icin ayrilmistir.
    /// </summary>
    public string? AssignmentKey { get; init; }

    /// <summary>Deneyin <see cref="ExperimentStatus.Running"/>'e gectigi an. Draft'ta <see langword="null"/>.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>Deneyin durduruldugu an. Calisiyorsa veya hic baslamadiysa <see langword="null"/>.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Son degistirilme zamani (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Kanarya kurallari. <see langword="null"/> ise otomatik karar YOKTUR (K1) —
    /// hicbir arka plan servisi bu deneyi degerlendirmez.
    /// </summary>
    public CanaryPolicy? Canary { get; init; }

    /// <summary>
    /// Otomatik geri almanin nedeni. Deney elle durdurulmus veya hic durdurulmamissa
    /// <see langword="null"/>.
    /// </summary>
    public string? RollbackReason { get; init; }
}
