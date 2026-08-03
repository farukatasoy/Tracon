namespace AgentPrism;

/// <summary>Bir OAuth yetkilendirme istegin sonuc durumu.</summary>
public enum McpOAuthOperationStatus
{
    /// <summary>Istek basarili.</summary>
    Ok = 0,

    /// <summary>Bu adda kayitli bir sunucu yok.</summary>
    ServerNotFound = 1,

    /// <summary>
    /// Sunucu OAuth icin ayarli degil, akis <see cref="McpOAuthAuthorizationMode.AuthorizationCode"/>
    /// degil, veya <c>AgentPrism:Mcp:OAuthCallbackBaseUri</c> yapilandirilmamis.
    /// </summary>
    NotConfigured = 2,

    /// <summary>Sunucuya baglanip yetkilendirme adresi alinamadi (zaman asimi dahil).</summary>
    ConnectionFailed = 3,

    /// <summary><c>state</c> bilinmiyor veya suresi dolmus (CSRF korumasi).</summary>
    InvalidState = 4,

    /// <summary>Saglayici kod degisimini reddetti veya kullanici yetkilendirmeyi iptal etti.</summary>
    AuthorizationFailed = 5,
}

/// <summary>OAuth baslatma isteminin sonucu.</summary>
public sealed record McpOAuthStartResult
{
    /// <summary>Sonuc durumu.</summary>
    public required McpOAuthOperationStatus Status { get; init; }

    /// <summary>Yoneticinin yonlendirilecegi yetkilendirme adresi. Yalniz <see cref="McpOAuthOperationStatus.Ok"/> iken dolu.</summary>
    public Uri? AuthorizationUri { get; init; }

    /// <summary>CSRF korumasi icin uretilen tek kullanimlik durum degeri.</summary>
    public string? State { get; init; }
}

/// <summary>OAuth geri donusunun (callback) sonucu.</summary>
public sealed record McpOAuthCompleteResult
{
    /// <summary>Sonuc durumu.</summary>
    public required McpOAuthOperationStatus Status { get; init; }

    /// <summary>Yetkilendirilen sunucunun adi. <see cref="McpOAuthOperationStatus.InvalidState"/> iken <see langword="null"/>.</summary>
    public string? ServerName { get; init; }

    /// <summary>Basarisizlik gerekcesi (kullaniciya gosterilebilir, sir icermez).</summary>
    public string? Error { get; init; }
}

/// <summary>
/// MCP sunuculari icin OAuth Mod 1 (yetkilendirme kodu) akisini yoneten koordinator.
/// </summary>
/// <remarks>
/// <para>
/// Soyutlama <c>AgentPrism.Abstractions</c> icindedir cunku <c>AgentPrism.AspNetCore</c>
/// (uçlar) <c>AgentPrism.Mcp</c> paketine bagli degildir — gerekce <see cref="IMcpToolRefresher"/>
/// ile aynidir.
/// </para>
/// <para>
/// <strong>Token'lar hicbir zaman veritabanina yazilmaz.</strong> Basarili bir akis
/// sonunda erisim ve yenileme token'lari yalniz bellekte, surec omruyle sinirli
/// tutulur (docs/22-MCP-DERINLESMESI.md, bolum 22.3).
/// </para>
/// </remarks>
public interface IMcpOAuthCoordinator
{
    /// <summary>
    /// Bir sunucu icin yetkilendirme akisini baslatir ve saglayicinin
    /// yetkilendirme adresini dondurur.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="serverName">Sunucu adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask<McpOAuthStartResult> StartAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saglayicinin geri donus istegini isler: kodu <c>state</c> ile eslesen
    /// bekleyen akisa iletir ve token degisiminin sonucunu bekler.
    /// </summary>
    /// <param name="state"><see cref="McpOAuthStartResult.State"/> ile uretilen deger.</param>
    /// <param name="code">Saglayicinin dondurdugu yetkilendirme kodu.</param>
    /// <param name="iss">RFC 9207 <c>iss</c> parametresi (varsa).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask<McpOAuthCompleteResult> CompleteAsync(
        string state,
        string? code,
        string? iss,
        CancellationToken cancellationToken = default);
}
