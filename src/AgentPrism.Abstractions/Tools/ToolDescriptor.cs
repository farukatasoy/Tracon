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
    /// Cagri oncesi acik onay gerekip gerekmedigi.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> ise derleyici tool'u
    /// <c>ApprovalRequiredAIFunction</c> ile sarar; Microsoft Agent Framework
    /// tool'u calistirmak yerine <c>ToolApprovalRequestContent</c> uretir ve
    /// cagri kullanicinin onayini bekler.
    /// </remarks>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Tool'un kaynagi. Kodda tanimli tool'larda <see langword="null"/>;
    /// uzak bir MCP sunucusundan gelen tool'larda sunucu adi.
    /// </summary>
    /// <remarks>
    /// Arayuz bu alani <em>ayri bir rozet</em> olarak gosterir: tool tanimi
    /// kodda degil, uzak bir sunucuda yasar ve o sunucu tanimi degistirebilir.
    /// </remarks>
    public string? Source { get; init; }
}
