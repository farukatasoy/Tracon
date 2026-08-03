using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="McpServerDefinition"/>'dan aktarim ayarlari kurar.
/// </summary>
/// <remarks>
/// <see cref="McpConnection"/> (uzun omurlu, onbellekli baglanti) ve kisa
/// omurlu prompt/kaynak istemcileri ayni kurulum mantigini paylasir; bu sinif
/// tekrari onlemek icin tek yerde toplar.
/// </remarks>
internal static class McpTransportFactory
{
    /// <summary>Yalnizca uzak http/https adresleri kabul edilir; stdio yoktur (K-058).</summary>
    [SuppressMessage(
        "Design",
        "MA0089:Optimize string method usage",
        Justification = "Sema karsilastirmasi buyuk/kucuk harfe duyarsiz olmalidir.")]
    public static bool IsRemoteHttp(Uri endpoint)
        => endpoint.IsAbsoluteUri
            && (string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase));

    /// <summary>Bir sunucunun OAuth geri donus (callback) adresini kurar.</summary>
    /// <remarks>
    /// Saglayicida onceden kayitli olmalidir; bu yuzden istekten degil, sabit
    /// <see cref="AgentPrismMcpOptions.OAuthCallbackBaseUri"/> ayarindan turetilir.
    /// </remarks>
    public static Uri BuildCallbackUri(Uri baseUri, string serverName)
        => new(baseUri, $"api/mcp-servers/{Uri.EscapeDataString(serverName)}/oauth/callback");

    /// <summary>
    /// Sunucu tanimindan, arka planda tekrar kullanilan (etkilesimsiz) baglanti
    /// icin aktarim ayarlarini kurar.
    /// </summary>
    /// <remarks>
    /// OAuth acikken <see cref="ClientOAuthOptions.AuthorizationCallbackHandler"/>
    /// kasitli olarak <strong>hemen basarisiz olur</strong>: bu, arka plan
    /// tazeleme dongusudur ve etkilesimli bir yetkilendirmeyi tamamlayacak bir
    /// yonetici yoktur. Gecerli token'lar <paramref name="tokenCache"/> uzerinden
    /// yeniden kullanilir; yoksa baglanti "erisilemedi" olarak loglanir ve o
    /// sunucunun tool'lari listeden duser — <c>/oauth/start</c> ile yeniden
    /// yetkilendirme beklenir.
    /// </remarks>
    public static HttpClientTransportOptions BuildTransportOptions(
        McpServerDefinition server,
        IConfiguration configuration,
        AgentPrismMcpOptions mcpOptions,
        ITokenCache tokenCache,
        ILogger logger)
        => new()
        {
            Name = server.Name,
            Endpoint = server.Endpoint,
            TransportMode = server.Transport == McpTransportMode.Sse
                ? HttpTransportMode.Sse
                : HttpTransportMode.StreamableHttp,
            AdditionalHeaders = BuildHeaders(server, configuration, logger),
            OAuth = BuildNonInteractiveOAuthOptions(server, configuration, mcpOptions, tokenCache, logger),
        };

    /// <summary>
    /// OAuth acikken ama <see cref="AgentPrismMcpOptions.OAuthCallbackBaseUri"/>
    /// ayarlanmamisken sunucu atlanmalidir; bu, saglayicida kayitli olmayan bir
    /// geri donus adresiyle baglanma girisimini onler.
    /// </summary>
    public static bool RequiresUnconfiguredCallback(McpServerDefinition server, AgentPrismMcpOptions mcpOptions)
        => server.OAuthEnabled && mcpOptions.OAuthCallbackBaseUri is null;

    /// <summary>
    /// Kimlik dogrulama basligini yapilandirmadan cozer ve ek basliklarla birlestirir.
    /// </summary>
    /// <remarks>
    /// Sunucu tanimi sirri <strong>tasimaz</strong>; yalnizca degerin okunacagi
    /// yapilandirma anahtarinin adini tasir. Deger burada, calisma aninda cozulur
    /// ve <c>dotnet user-secrets</c> veya ortam degiskeninde kalir.
    /// </remarks>
    private static Dictionary<string, string> BuildHeaders(
        McpServerDefinition server,
        IConfiguration configuration,
        ILogger logger)
    {
        var headers = new Dictionary<string, string>(server.Headers, StringComparer.OrdinalIgnoreCase);

        if (server.OAuthEnabled)
        {
            // OAuth acikken Authorization basligini ClientOAuthOptions yonetir;
            // ikisi ayni basligi yazmaya calisirsa hangisinin kazandigi sunucu
            // SDK'sinin ic detayina kalirdi. GovernanceEndpoints.Validate bu
            // kombinasyonu zaten 400 ile reddeder; burasi son bir savunmadir.
            return headers;
        }

        if (server.AuthorizationConfigurationKey is not { Length: > 0 } key)
        {
            return headers;
        }

        if (configuration[key] is { Length: > 0 } value)
        {
            headers["Authorization"] = value;
        }
        else
        {
            logger.LogWarning(
                "MCP sunucusu '{ServerName}' icin '{ConfigurationKey}' yapilandirma anahtari bos. " +
                "Kimlik dogrulama basligi gonderilmeyecek.",
                server.Name,
                key);
        }

        return headers;
    }

    private static ClientOAuthOptions? BuildNonInteractiveOAuthOptions(
        McpServerDefinition server,
        IConfiguration configuration,
        AgentPrismMcpOptions mcpOptions,
        ITokenCache tokenCache,
        ILogger logger)
    {
        if (!server.OAuthEnabled || mcpOptions.OAuthCallbackBaseUri is not { } baseUri)
        {
            return null;
        }

        return new ClientOAuthOptions
        {
            ClientId = server.OAuthClientId,
            ClientSecret = ResolveClientSecret(server, configuration, logger),
            Scopes = ParseScopes(server.OAuthScopes),
            RedirectUri = BuildCallbackUri(baseUri, server.Name),
            TokenCache = tokenCache,
            // Etkilesimsiz yol: gecerli bir token yoksa hemen basarisiz olunur,
            // McpConnection.ConnectAsync'in genel "sunucuya baglanilamadi"
            // yakalayicisina duser ve o sunucunun tool'lari bu tazelemede
            // listelenmez. Gercek yetkilendirme yalniz McpOAuthAuthorizationCoordinator
            // uzerinden (yonetici arayuzu, /oauth/start) yapilir.
            AuthorizationCallbackHandler = (_, _) => Task.FromException<AuthorizationResult?>(
                new InvalidOperationException(
                    $"'{server.Name}' MCP sunucusu OAuth yetkilendirmesi gerektiriyor. " +
                    "Yonetici arayuzden 'Yetkilendir' ile /oauth/start akisini baslatin.")),
        };
    }

    /// <summary>OAuth istemci gizli anahtarini yapilandirmadan cozer (K-059).</summary>
    public static string? ResolveClientSecret(McpServerDefinition server, IConfiguration configuration, ILogger logger)
    {
        if (server.OAuthClientSecretConfigurationKey is not { Length: > 0 } key)
        {
            return null;
        }

        if (configuration[key] is { Length: > 0 } value)
        {
            return value;
        }

        logger.LogWarning(
            "MCP sunucusu '{ServerName}' icin OAuth '{ConfigurationKey}' yapilandirma anahtari bos.",
            server.Name,
            key);

        return null;
    }

    /// <summary>Bosluk ile ayrilmis scope metnini listeye cevirir.</summary>
    public static IEnumerable<string>? ParseScopes(string? scopes)
        => string.IsNullOrWhiteSpace(scopes)
            ? null
            : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
