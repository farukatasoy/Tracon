using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>MCP OAuth yetkilendirme akisi.</summary>
/// <remarks>
/// <strong>Tek deger:</strong> <c>ModelContextProtocol.Core</c> 2.0.0 yalnizca
/// Authorization Code (+PKCE) akisini destekler; <c>ClientOAuthOptions.RedirectUri</c>
/// zorunlu bir alandir ve kutuphane etkilesimsiz bir istemci-kimlik-bilgileri
/// akisi sunmaz. Deger yine de bir enum olarak tutulur — SDK ileride baska bir
/// akis eklerse (ornegin client_credentials) genisleme noktasi hazir olur.
/// Gerekce: docs/22-MCP-DERINLESMESI.md, bolum 22.3.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<McpOAuthAuthorizationMode>))]
public enum McpOAuthAuthorizationMode
{
    /// <summary>
    /// Yetkilendirme kodu akisi (Authorization Code + PKCE). Yonetici arayuzden
    /// <c>/oauth/start</c> ile baslatilir, saglayiciya yonlendirilir ve
    /// <c>/oauth/callback</c>'e doner.
    /// </summary>
    AuthorizationCode = 0,
}

/// <summary>Bir MCP sunucusuna baglanma bicimi.</summary>
/// <remarks>
/// <strong>Stdio bilerek yoktur.</strong> Stdio aktarimi sunucuda bir surec
/// baslatir; bu, arayuze erisen birinin sunucuda program calistirmasi demektir
/// ve tasarim kurali K2'yi temelden bozar. AgentPrism yalnizca <em>uzak</em>
/// MCP sunucularina baglanir. Gerekce: <c>docs/KARARLAR.md</c>, karar K-058.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<McpTransportMode>))]
public enum McpTransportMode
{
    /// <summary>Streamable HTTP. MCP'nin guncel uzak aktarimi.</summary>
    StreamableHttp = 0,

    /// <summary>Sunucu tarafli olaylar (SSE). Eski sunucular icin.</summary>
    Sse = 1,
}

/// <summary>
/// Kayitli bir uzak MCP sunucusu. Tool'lari baglanti aninda kesfedilir ve
/// kodda kayitli tool'larin yaninda listelenir.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Sir tasimaz.</strong> Kimlik dogrulama basliginin <em>degeri</em>
/// bu kayitta saklanmaz; yalnizca degerin okunacagi yapilandirma anahtarinin
/// adi (<see cref="AuthorizationConfigurationKey"/>) saklanir. Deger calisma
/// aninda <c>IConfiguration</c> uzerinden cozulur ve boylece
/// <c>dotnet user-secrets</c> veya ortam degiskeninde kalir. Veritabani yedegine,
/// denetim izine veya arayuz yanitina hicbir zaman girmez.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-059.
/// </para>
/// </remarks>
public sealed record McpServerDefinition
{
    /// <summary>Sunucu kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Sunucunun ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>
    /// Sunucu adi. Kesfedilen tool'lar <c>{ad}.{tool}</c> bicimiyle adlandirilir;
    /// boylece iki sunucudaki ayni adli tool cakismaz.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Aciklama.</summary>
    public string? Description { get; init; }

    /// <summary>Sunucu adresi. Yalnizca <c>http</c> ve <c>https</c> kabul edilir.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Aktarim bicimi.</summary>
    public McpTransportMode Transport { get; init; }

    /// <summary>
    /// <c>Authorization</c> basliginin degerinin okunacagi yapilandirma anahtari.
    /// Ornek: <c>AgentPrism:Mcp:GithubToken</c>. Bos birakilirsa baslik gonderilmez.
    /// </summary>
    public string? AuthorizationConfigurationKey { get; init; }

    /// <summary>
    /// Ek istek basliklari. <strong>Sir tasimamalidir</strong> — bu degerler
    /// oldugu gibi saklanir ve arayuzde gorunur.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Sunucu etkin mi. Kapaliyken tool'lari kesfedilmez.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// OAuth ile kimlik dogrulama acik mi. Acikken <see cref="AuthorizationConfigurationKey"/>
    /// ile ayni anda kullanilamaz — ikisi de <c>Authorization</c> basligini
    /// yonetmeye calisirdi.
    /// </summary>
    /// <remarks>
    /// 🚨 <c>[JsonPropertyName]</c> BILEREK verilir: System.Text.Json'in camelCase
    /// politikasi yalniz ILK harfi kucultur, "OAuth" iki buyuk harfle basladigi
    /// icin varsayilan cikti <c>oAuthEnabled</c> olurdu (beklenen <c>oauthEnabled</c>
    /// degil). Ayni kisit asagidaki dort OAuth alaninin hepsinde gecerlidir.
    /// </remarks>
    [JsonPropertyName("oauthEnabled")]
    public bool OAuthEnabled { get; init; }

    /// <summary>OAuth istemci kimligi. Sir degildir, oldugu gibi saklanir.</summary>
    [JsonPropertyName("oauthClientId")]
    public string? OAuthClientId { get; init; }

    /// <summary>
    /// OAuth istemci gizli anahtarinin degerinin okunacagi yapilandirma anahtari.
    /// Deger, <see cref="AuthorizationConfigurationKey"/> ile ayni kuralla
    /// (K-059) veritabanina hicbir zaman yazilmaz.
    /// </summary>
    [JsonPropertyName("oauthClientSecretConfigurationKey")]
    public string? OAuthClientSecretConfigurationKey { get; init; }

    /// <summary>Bosluk ile ayrilmis OAuth scope listesi. Ornek: <c>"repo read:user"</c>.</summary>
    [JsonPropertyName("oauthScopes")]
    public string? OAuthScopes { get; init; }

    /// <summary>OAuth yetkilendirme akisi.</summary>
    [JsonPropertyName("oauthAuthorizationMode")]
    public McpOAuthAuthorizationMode OAuthAuthorizationMode { get; init; } = McpOAuthAuthorizationMode.AuthorizationCode;

    /// <summary>
    /// Bu sunucunun tool'lari cagri oncesi acik onay ister mi.
    /// <strong>Varsayilani <see langword="true"/></strong>: tool tanimi disaridan
    /// gelir ve guvenilmez sayilir.
    /// </summary>
    public bool RequiresApproval { get; init; } = true;

    /// <summary>Olusturulma zamani (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncelleme zamani (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Kayitli MCP sunucularinin deposu.</summary>
public interface IMcpServerStore
{
    /// <summary>Bir kiracinin sunucularini ada gore sirali listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sunucular.</returns>
    ValueTask<IReadOnlyList<McpServerDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir sunucuyu ada gore getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Sunucu adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sunucu; yoksa <see langword="null"/>.</returns>
    ValueTask<McpServerDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Bir sunucuyu ekler veya gunceller. Anahtar <c>(kiraci, ad)</c> ciftidir.</summary>
    /// <param name="server">Yazilacak sunucu.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kalici kayit.</returns>
    ValueTask<McpServerDefinition> SaveAsync(
        McpServerDefinition server,
        CancellationToken cancellationToken = default);

    /// <summary>Bir sunucuyu siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Sunucu adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit silindiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);
}
