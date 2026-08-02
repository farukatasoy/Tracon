namespace AgentPrism;

/// <summary>Uzak MCP sunucularina baglanma ayarlari.</summary>
public sealed class AgentPrismMcpOptions
{
    /// <summary>Yapilandirma bolumunun varsayilan adi.</summary>
    public const string SectionName = "AgentPrism:Mcp";

    /// <summary>
    /// MCP tool kesfi acik mi. Kapatilirsa hicbir sunucuya baglanilmaz ve
    /// yalnizca kodda kayitli tool'lar gorunur.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Tool listesinin yenilenme araligi. Uzak sunucu tool tanimini
    /// degistirebilir; kesif bu araliklarla tekrarlanir.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Bir sunucuya baglanma ve tool listeleme icin ust sure.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Tek bir sunucudan kabul edilecek ust tool sayisi. Asan tool'lar atilir
    /// ve bir uyari loglanir.
    /// </summary>
    /// <remarks>
    /// Uzak sunucu guvenilmez sayilir. Sinirsiz bir tool listesi hem model
    /// baglaminda hem de arayuzde denetimsiz buyume uretirdi.
    /// </remarks>
    public int MaxToolsPerServer { get; set; } = 100;
}
