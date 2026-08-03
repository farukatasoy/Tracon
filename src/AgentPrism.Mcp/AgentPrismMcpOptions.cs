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

    /// <summary>
    /// Mod A'da (<see cref="AgentDefinition.McpResourceUris"/>) tek bir kaynaktan
    /// baglama eklenecek ust bayt siniri. Asan icerik kirpilir.
    /// </summary>
    public int MaxResourceBytesPerResource { get; set; } = 64 * 1024;

    /// <summary>
    /// Mod A'da bir agent tanimindaki tum kaynaklarin toplam bayt siniri.
    /// Asan kaynaklar kirpilir; siniri asindan sonrakiler tamamen atlanir.
    /// </summary>
    public int MaxResourceBytesTotal { get; set; } = 256 * 1024;

    /// <summary>
    /// OAuth Mod 1 (yetkilendirme kodu) geri donus adreslerinin taban URI'si.
    /// Ornek: <c>https://myapp.example.com/</c>. Saglayicida onceden kayitli
    /// olmalidir. <see langword="null"/> ise OAuth acik bir sunucuya baglanilmaz
    /// ve <c>/oauth/start</c> <see cref="McpOAuthOperationStatus.NotConfigured"/> doner.
    /// </summary>
    public Uri? OAuthCallbackBaseUri { get; set; }
}
