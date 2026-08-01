namespace AgentPrism;

/// <summary>
/// Kodda kayitli bir tool'un arayuze gosterilen tanimi. Arayuz agent editorunde
/// bu listeden secim yaptirir; serbest metin girisi kabul etmez.
/// </summary>
public sealed record ToolDescriptor
{
    /// <summary>Tool adi. Agent tanimlarinda bu ad kullanilir.</summary>
    public required string Name { get; init; }

    /// <summary>Modelin tool'u ne zaman cagiracagini anlamasini saglayan aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Argumanlarin JSON semasi.</summary>
    public string? JsonSchema { get; init; }

    /// <summary>
    /// Cagri oncesi acik onay gerekip gerekmedigi. Onay akisi Faz 6'da devreye girer;
    /// bu bayrak o zamana kadar yalnizca bilgi amaclidir.
    /// </summary>
    public bool RequiresApproval { get; init; }
}
