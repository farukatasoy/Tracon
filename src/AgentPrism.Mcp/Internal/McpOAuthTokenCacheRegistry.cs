using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Kiraci+sunucu basina paylasilan <see cref="InMemoryMcpTokenCache"/> ornekleri.
/// </summary>
/// <remarks>
/// Aradaki kontrat basittir: ayni <c>(tenantId, serverName)</c> ciftini isteyen
/// her cagiran ayni onbellek nesnesini alir. Bu, <see cref="McpOAuthAuthorizationCoordinator"/>'un
/// etkilesimli akista aldigi token'lari, <see cref="McpToolCatalog"/>'un arka
/// plan yeniden baglanmalarinin da gorebilmesini saglar.
/// </remarks>
internal sealed class McpOAuthTokenCacheRegistry
{
    private readonly ConcurrentDictionary<string, InMemoryMcpTokenCache> _caches = new(StringComparer.Ordinal);

    /// <summary>Verilen kiraci+sunucu icin onbellegi getirir; yoksa olusturur.</summary>
    public InMemoryMcpTokenCache GetOrCreate(string tenantId, string serverName)
        => _caches.GetOrAdd(Key(tenantId, serverName), static _ => new InMemoryMcpTokenCache());

    private static string Key(string tenantId, string serverName) => $"{tenantId}{serverName}";
}
