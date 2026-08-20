using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// Holds and refreshes discovered MCP tools per tenant.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why a cache?</strong> <see cref="IToolRegistry"/> is synchronous
/// (<c>List</c>, <c>TryGet</c>); MCP discovery, on the other hand, is an
/// async operation over the network. Discovery happens in the background,
/// its result is stored here, and the registry reads it synchronously. If
/// discovery has not completed yet, MCP tools are not visible, and an agent
/// referencing such a tool fails with an explicit error while compiling — it
/// never silently runs with a missing tool.
/// </para>
/// <para>
/// <strong>Why is the connection kept?</strong> A <c>McpClientTool</c> call
/// goes to the remote server through the client at call time; if the client
/// is closed, the tool cannot be called. This is why connections are kept
/// alive between refreshes and are only re-established when the server
/// definition <em>changes</em>. Closing an unchanged server's connection on
/// every refresh would break a tool call in progress.
/// </para>
/// </remarks>
internal sealed class McpToolCatalog : IAsyncDisposable
{
    private readonly IMcpServerStore _servers;
    private readonly ITenantStore _tenants;
    private readonly IConfiguration _configuration;
    private readonly IOptions<AgentPrismOptions> _coreOptions;
    private readonly IOptions<AgentPrismMcpOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpToolCatalog> _logger;
    private readonly McpOAuthTokenCacheRegistry _tokenCaches;
    private readonly IToolAuthorizationHandler _authorizationHandler;
    private readonly IRunAttributionContext? _attribution;
    private readonly EgressSocketGuard? _egressGuard;
    private readonly string _allowedConfigurationPrefix;

    // The read path is lock-free: every refresh builds a new dictionary and
    // swaps the reference atomically. Readers always see a consistent snapshot.
    private volatile Dictionary<string, McpTenantTools> _byTenant = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // ConcurrentDictionary: writes are always single-writer under
    // _refreshGate, but the Mode A context provider (McpResourceContextProvider)
    // reads lock-free on every agent run. A plain Dictionary would corrupt if
    // this read raced a refresh.
    private readonly ConcurrentDictionary<McpTenantServerKey, McpConnection> _connections = new();

    public McpToolCatalog(
        IMcpServerStore servers,
        ITenantStore tenants,
        IConfiguration configuration,
        IOptions<AgentPrismOptions> coreOptions,
        IOptions<AgentPrismMcpOptions> options,
        ILoggerFactory loggerFactory,
        McpOAuthTokenCacheRegistry tokenCaches,
        IToolAuthorizationHandler authorizationHandler,
        IRunAttributionContext? attribution,
        EgressSocketGuard? egressGuard = null,
        IOptions<AgentPrismMcpSecurityOptions>? securityOptions = null)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(coreOptions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(tokenCaches);
        ArgumentNullException.ThrowIfNull(authorizationHandler);

        _servers = servers;
        _tenants = tenants;
        _configuration = configuration;
        _coreOptions = coreOptions;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpToolCatalog>();
        _tokenCaches = tokenCaches;
        _authorizationHandler = authorizationHandler;
        _attribution = attribution;
        _egressGuard = egressGuard;
        _allowedConfigurationPrefix = (securityOptions?.Value ?? new AgentPrismMcpSecurityOptions())
            .AllowedConfigurationPrefix;
    }

    /// <summary>Returns a tenant's discovered tools.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The tool set; the empty set if discovery has not run yet.</returns>
    public McpTenantTools ForTenant(string tenantId)
        => _byTenant.TryGetValue(tenantId, out var tools) ? tools : McpTenantTools.Empty;

    /// <summary>
    /// Returns a tenant's live connection to a specific server (Mode A).
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the connection is currently up; <see langword="false"/> if the server is unreachable or disabled.
    /// </returns>
    public bool TryGetConnection(string tenantId, string serverName, [NotNullWhen(true)] out McpConnection? connection)
        => _connections.TryGetValue(new McpTenantServerKey(tenantId, serverName), out connection);

    /// <summary>
    /// Scans every tenant's servers and refreshes the tool list.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The outcome of this refresh.</returns>
    /// <remarks>
    /// Being unable to reach a server is <strong>not an error</strong>: that
    /// server's tools drop out of the list, the others keep working, and a
    /// warning is logged. A remote server crashing must not stop AgentPrism —
    /// but the caller can learn about it through
    /// <see cref="McpRefreshOutcome.HadUnreachableServers"/>.
    /// </remarks>
    public async ValueTask<McpRefreshOutcome> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var tenantIds = await ResolveTenantIdsAsync(cancellationToken).ConfigureAwait(false);
            var byTenant = new Dictionary<string, McpTenantTools>(StringComparer.Ordinal);
            var live = new HashSet<McpTenantServerKey>();
            var total = 0;
            var hadUnreachableServers = false;

            foreach (var tenantId in tenantIds)
            {
                var registrations = new List<AgentPrismToolRegistration>();

                foreach (var server in await _servers.ListAsync(tenantId, cancellationToken).ConfigureAwait(false))
                {
                    if (!server.Enabled)
                    {
                        continue;
                    }

                    var key = new McpTenantServerKey(tenantId, server.Name);
                    live.Add(key);

                    var (connection, unreachable) = await EnsureConnectionAsync(tenantId, key, server, cancellationToken).ConfigureAwait(false);

                    hadUnreachableServers |= unreachable;

                    if (connection is not null)
                    {
                        registrations.AddRange(connection.Tools);
                    }
                }

                if (registrations.Count > 0)
                {
                    byTenant[tenantId] = McpTenantTools.Create(
                        registrations,
                        _logger,
                        _authorizationHandler,
                        _coreOptions.Value.Tools.DefaultTimeout,
                        _attribution,
                        _loggerFactory.CreateLogger<AuthorizingAIFunction>(),
                        _loggerFactory.CreateLogger<TimeoutAIFunction>());
                    total += registrations.Count;
                }
            }

            await CloseRemovedConnectionsAsync(live).ConfigureAwait(false);

            _byTenant = byTenant;

            return new McpRefreshOutcome { ToolCount = total, HadUnreachableServers = hadUnreachableServers };
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _connections.Clear();
        _refreshGate.Dispose();
    }

    /// <summary>
    /// Derives the list of tenants to run discovery for.
    /// </summary>
    /// <remarks>
    /// The default tenant is always added to the registered tenants: tenant
    /// registration is not mandatory, and in a single-tenant setup the
    /// <c>tenants</c> table may be empty.
    /// </remarks>
    private async ValueTask<IReadOnlyCollection<string>> ResolveTenantIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { _coreOptions.Value.DefaultTenantId };

        try
        {
            foreach (var tenant in await _tenants.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(tenant.Slug);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the tenant list; MCP discovery will run only for the default tenant.");
        }

        return ids;
    }

    private async ValueTask<(McpConnection? Connection, bool Unreachable)> EnsureConnectionAsync(
        string tenantId,
        McpTenantServerKey key,
        McpServerDefinition server,
        CancellationToken cancellationToken)
    {
        var fingerprint = McpConnection.ComputeFingerprint(server);

        if (_connections.TryGetValue(key, out var existing))
        {
            if (string.Equals(existing.FingerprintValue, fingerprint, StringComparison.Ordinal))
            {
                // The definition has not changed: the connection stays up,
                // only the tool list is refreshed. Closing an unchanged
                // connection would break a tool call in progress.
                var (refreshed, unreachable) = await existing.RefreshCatalogAsync(_options.Value, _logger, cancellationToken)
                    .ConfigureAwait(false);

                return refreshed ? (existing, false) : (null, unreachable);
            }

            await existing.DisposeAsync().ConfigureAwait(false);
            _connections.TryRemove(key, out _);
        }

        var tokenCache = _tokenCaches.GetOrCreate(tenantId, server.Name);

        var (connection, connectUnreachable) = await McpConnection
            .ConnectAsync(
                server,
                _configuration,
                _options.Value,
                tokenCache,
                _loggerFactory,
                _logger,
                _egressGuard,
                _allowedConfigurationPrefix,
                cancellationToken)
            .ConfigureAwait(false);

        if (connection is null)
        {
            return (null, connectUnreachable);
        }

        _connections[key] = connection;

        return (connection, false);
    }

    private async ValueTask CloseRemovedConnectionsAsync(HashSet<McpTenantServerKey> live)
    {
        var removed = _connections.Keys.Where(key => !live.Contains(key)).ToList();

        foreach (var key in removed)
        {
            if (_connections.TryRemove(key, out var connection))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// The <see cref="IMcpToolRefresher"/> implementation. The HTTP layer
/// triggers refresh through this interface, so <c>AgentPrism.AspNetCore</c>
/// does not depend on the MCP package.
/// </summary>
internal sealed class McpToolRefresher : IMcpToolRefresher
{
    private readonly McpToolCatalog _catalog;

    public McpToolRefresher(McpToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public ValueTask<McpRefreshOutcome> RefreshAsync(CancellationToken cancellationToken = default)
        => _catalog.RefreshAsync(cancellationToken);
}
