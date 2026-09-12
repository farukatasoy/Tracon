using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// <see cref="InMemoryMcpTokenCache"/> instances shared per tenant+server pair.
/// </summary>
/// <remarks>
/// The contract is simple: every caller requesting the same <c>(tenantId,
/// serverName)</c> pair gets the same cache instance. This lets tokens
/// obtained by <see cref="McpOAuthAuthorizationCoordinator"/> during the
/// interactive flow also be visible to <see cref="McpToolCatalog"/>'s
/// background reconnections.
/// </remarks>
internal sealed class McpOAuthTokenCacheRegistry
{
    private readonly ConcurrentDictionary<McpTenantServerKey, InMemoryMcpTokenCache> _caches = new();

    /// <summary>Gets the cache for the given tenant+server pair; creates it if missing.</summary>
    public InMemoryMcpTokenCache GetOrCreate(string tenantId, string serverName)
        => _caches.GetOrAdd(new McpTenantServerKey(tenantId, serverName), static _ => new InMemoryMcpTokenCache());
}
