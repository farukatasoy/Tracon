using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Authentication;

namespace Tracon;

/// <summary>
/// The token caches of the MCP servers, one per tenant and server pair, in the mode
/// <see cref="TraconMcpOptions.OAuthTokenStore"/> selects.
/// </summary>
/// <remarks>
/// <para>
/// Every caller requesting the same <c>(tenantId, serverName)</c> pair for the same
/// server definition gets the same cache instance. This lets tokens obtained by
/// <see cref="McpOAuthAuthorizationCoordinator"/> during the interactive flow also be
/// visible to <see cref="McpToolCatalog"/>'s background reconnections. In
/// <see cref="McpOAuthTokenStore.Memory"/> mode the instance is an <see cref="InMemoryMcpTokenCache"/>; in
/// <see cref="McpOAuthTokenStore.DistributedCache"/> mode it is a <see cref="DistributedMcpTokenCache"/>
/// that reads and writes the application's distributed cache, and the instance is shared per
/// process only so that one process knows which refresh lease it holds.
/// </para>
/// <para>
/// <strong>A token belongs to the resource it was issued for.</strong> The MCP SDK
/// sends a valid cached token to whatever endpoint its transport points at; it does not
/// check which server the token came from (measured on <c>ModelContextProtocol.Core</c>
/// 2.3.0). A cache keyed by the server's NAME alone therefore sent the token to a new
/// endpoint as soon as an administrator changed it. The cache is bound to the endpoint
/// and the client id it was created for; a definition that differs gets an empty cache
/// and must be authorized again. The distributed cache keeps the binding next to the token.
/// </para>
/// </remarks>
internal sealed class McpOAuthTokenCacheRegistry
{
    private readonly McpOAuthDistributedBackend _backend;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<McpTenantServerKey, BoundCache> _caches = new();

    /// <summary>Initializes a registry that keeps tokens in memory.</summary>
    public McpOAuthTokenCacheRegistry()
        : this(McpOAuthDistributedBackend.Disabled, NullLoggerFactory.Instance)
    {
    }

    /// <summary>Initializes a registry in the mode the backend stands for.</summary>
    /// <param name="backend">The distributed services, or the statement that the mode is off.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public McpOAuthTokenCacheRegistry(McpOAuthDistributedBackend backend, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _backend = backend;
        _logger = loggerFactory.CreateLogger<DistributedMcpTokenCache>();
    }

    /// <summary>
    /// Gets the cache for the given tenant and server definition; creates it if missing,
    /// and replaces it with an empty one when the definition's endpoint or client id changed.
    /// </summary>
    /// <param name="tenantId">The tenant the server belongs to.</param>
    /// <param name="server">The server definition the tokens are for.</param>
    /// <returns>The cache bound to this definition.</returns>
    public ITokenCache GetOrCreate(string tenantId, McpServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);

        var key = new McpTenantServerKey(tenantId, server.Name);
        var binding = McpTokenBinding.Of(server);

        while (true)
        {
            var current = _caches.GetOrAdd(key, static (pair, state) => state.Registry.Create(pair, state.Binding), (Registry: this, Binding: binding));

            if (current.Binding == binding)
            {
                return current.Cache;
            }

            // The old cache is not disposed: a connection that is being replaced may still
            // read it, and it holds no unmanaged resource. It is only no longer handed out.
            // In the distributed mode the old token stays in the cache under the same key
            // until the new definition's first token replaces it; no read returns it, because
            // the stored binding is checked on every read.
            var replacement = Create(key, binding);

            if (_caches.TryUpdate(key, replacement, current))
            {
                return replacement.Cache;
            }
        }
    }

    /// <summary>Gets the tenant and server pairs this registry has handed a cache out for, with the binding of each.</summary>
    public IReadOnlyCollection<KeyValuePair<McpTenantServerKey, McpTokenBinding>> Bindings
        => [.. _caches.Select(static entry => KeyValuePair.Create(entry.Key, entry.Value.Binding))];

    /// <summary>Forgets the token of a server that no longer exists.</summary>
    /// <param name="key">The tenant and server.</param>
    /// <param name="deleted">The binding the server had when the caller saw it listed, the one that is gone.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the token is gone, or when removing it failed.</returns>
    /// <remarks>
    /// <para>
    /// Only the token of <paramref name="deleted"/> is forgotten. The caller decided "deleted" from a list it read earlier; a
    /// server of the same name that was created and authorized since has another identifier, its token is in the entry now, and
    /// it is not the one to forget. In the distributed mode the shared entry is read first and removed only when it still carries
    /// the deleted binding or cannot be read (a value nobody can use). The read and the removal are two calls, so this is as
    /// exact as the shared cache's lease for a refresh is: the window is a call, not a refresh pass.
    /// </para>
    /// <para>A failure is logged and does not throw: the entry ends by itself (<see cref="TraconMcpOptions.OAuthTokenSlidingLifetime"/>).</para>
    /// </remarks>
    public async ValueTask RemoveAsync(McpTenantServerKey key, McpTokenBinding deleted, CancellationToken cancellationToken)
    {
        if (_caches.TryGetValue(key, out var current))
        {
            if (current.Binding != deleted)
            {
                return;
            }

            _caches.TryRemove(KeyValuePair.Create(key, current));
        }

        if (!_backend.Enabled)
        {
            return;
        }

        try
        {
            var cacheKey = McpOAuthCacheKeys.Token(key);
            var bytes = await _backend.Cache.GetAsync(cacheKey, cancellationToken).ConfigureAwait(false);

            if (bytes is not null
                && McpOAuthEnvelope.TryOpen(_backend.Protector, bytes, McpOAuthJsonContext.Default.McpStoredTokens, out var stored)
                && stored!.Binding != deleted)
            {
                return;
            }

            await _backend.Cache.RemoveAsync(cacheKey, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(
                ex,
                "The OAuth token of deleted MCP server '{ServerName}' could not be removed from the distributed cache; it ends by itself.",
                key.ServerName);
        }
    }

    private BoundCache Create(McpTenantServerKey key, McpTokenBinding binding)
        => new(
            binding,
            _backend.Enabled
                ? new DistributedMcpTokenCache(_backend, key, binding, _logger)
                : new InMemoryMcpTokenCache());

    private sealed record BoundCache(McpTokenBinding Binding, ITokenCache Cache);
}
