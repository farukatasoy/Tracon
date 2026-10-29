using System.Collections.Concurrent;

namespace Tracon;

/// <summary>
/// <see cref="InMemoryMcpTokenCache"/> instances shared per tenant+server pair.
/// </summary>
/// <remarks>
/// <para>
/// Every caller requesting the same <c>(tenantId, serverName)</c> pair for the same
/// server definition gets the same cache instance. This lets tokens obtained by
/// <see cref="McpOAuthAuthorizationCoordinator"/> during the interactive flow also be
/// visible to <see cref="McpToolCatalog"/>'s background reconnections.
/// </para>
/// <para>
/// <strong>A token belongs to the resource it was issued for.</strong> The MCP SDK
/// sends a valid cached token to whatever endpoint its transport points at; it does not
/// check which server the token came from (measured on <c>ModelContextProtocol.Core</c>
/// 2.3.0). A cache keyed by the server's NAME alone therefore sent the token to a new
/// endpoint as soon as an administrator changed it. The cache is bound to the endpoint
/// and the client id it was created for; a definition that differs gets an empty cache
/// and must be authorized again.
/// </para>
/// </remarks>
internal sealed class McpOAuthTokenCacheRegistry
{
    private readonly ConcurrentDictionary<McpTenantServerKey, BoundCache> _caches = new();

    /// <summary>
    /// Gets the cache for the given tenant and server definition; creates it if missing,
    /// and replaces it with an empty one when the definition's endpoint or client id changed.
    /// </summary>
    /// <param name="tenantId">The tenant the server belongs to.</param>
    /// <param name="server">The server definition the tokens are for.</param>
    /// <returns>The cache bound to this definition.</returns>
    public InMemoryMcpTokenCache GetOrCreate(string tenantId, McpServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var key = new McpTenantServerKey(tenantId, server.Name);
        var binding = new TokenBinding(server.Endpoint.AbsoluteUri, server.OAuthClientId);

        while (true)
        {
            var current = _caches.GetOrAdd(key, static (_, bound) => new BoundCache(bound, new InMemoryMcpTokenCache()), binding);

            if (current.Binding == binding)
            {
                return current.Cache;
            }

            // The old cache is not disposed: a connection that is being replaced may still
            // read it, and it holds no unmanaged resource. It is only no longer handed out.
            var replacement = new BoundCache(binding, new InMemoryMcpTokenCache());

            if (_caches.TryUpdate(key, replacement, current))
            {
                return replacement.Cache;
            }
        }
    }

    /// <summary>The definition fields a token is bound to.</summary>
    /// <param name="Endpoint">The server's absolute endpoint address.</param>
    /// <param name="ClientId">The OAuth client id.</param>
    private readonly record struct TokenBinding(string Endpoint, string? ClientId);

    private sealed record BoundCache(TokenBinding Binding, InMemoryMcpTokenCache Cache);
}
