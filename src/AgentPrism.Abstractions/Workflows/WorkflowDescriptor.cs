namespace AgentPrism;

/// <summary>
/// Katalogda listelenen bir workflow'un ozet gorunumu.
/// </summary>
/// <remarks>
/// Kodda tanimli workflow'lar bildirimsel bir <see cref="WorkflowDefinition"/>
/// tasimaz: bir fabrikadan gelirler ve grafiklerini yalnizca derlendikten sonra
/// bilinir. Boyle bir kayit icin <see cref="Kind"/> ve <see cref="AgentNames"/>
/// bos kalir; katalog yine de adi ve aciklamayi gosterir.
/// </remarks>
public sealed record WorkflowDescriptor
{
    /// <summary>Workflow'un benzersiz adi.</summary>
    public required string Name { get; init; }

    /// <summary>Arayuzde gosterilecek ad.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Kisa aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Tanimin kaynagi.</summary>
    public required AgentDefinitionOrigin Origin { get; init; }

    /// <summary>
    /// Hazir desen. Kodda fabrika ile tanimlanmis workflow'larda
    /// <see langword="null"/>: serbest graf bir desene karsilik gelmez.
    /// </summary>
    public WorkflowKind? Kind { get; init; }

    /// <summary>Grafa giren agent adlari. Kod workflow'larinda bos olabilir.</summary>
    public IReadOnlyList<string> AgentNames { get; init; } = [];

    /// <summary>Tanim surumu. Kod workflow'larinda her zaman 1'dir.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Son degistirilme zamani (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
