using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace Tracon;

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
/// <para>
/// A compiled agent holds tool objects bound to one connection. Each snapshot
/// therefore records the <see cref="McpConnection.Generation"/> behind every tool,
/// and the compiled-agent cache key carries it through
/// <see cref="McpToolRegistry.GetBindingFingerprint"/>: when a connection is
/// replaced or closed, the next resolve compiles again instead of serving an agent
/// whose tools call a closed connection.
/// </para>
/// </remarks>
internal sealed class McpToolCatalog : IAsyncDisposable
{
    private readonly IMcpServerStore _servers;
    private readonly ITenantStore _tenants;
    private readonly IConfiguration _configuration;
    private readonly IOptions<TraconOptions> _coreOptions;
    private readonly IOptions<TraconMcpOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpToolCatalog> _logger;
    private readonly McpOAuthTokenCacheRegistry _tokenCaches;
    private readonly IToolAuthorizationHandler _authorizationHandler;
    private readonly TenantToolPolicyGate _policyGate;
    private readonly IToolArgumentsValidator _validator;
    private readonly IRunAttributionContext? _attribution;
    private readonly EgressSocketGuard? _egressGuard;
    private readonly McpKeySpace _keySpace;
    private readonly TimeProvider _timeProvider;

    // The read path is lock-free: every refresh builds a new dictionary and
    // swaps the reference atomically. Readers always see a consistent snapshot.
    private volatile Dictionary<string, McpTenantTools> _byTenant = new(StringComparer.Ordinal);

    // One server that never answers costs its ConnectionTimeout. The pass holds the
    // refresh gate for as long as its slowest wave of connections, so the servers are
    // connected side by side: sequentially, one tenant's dead servers added their
    // timeouts to every other tenant's refresh, manual refresh, and OAuth completion.
    private const int MaxParallelConnections = 8;

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
        IOptions<TraconOptions> coreOptions,
        IOptions<TraconMcpOptions> options,
        ILoggerFactory loggerFactory,
        McpOAuthTokenCacheRegistry tokenCaches,
        IToolAuthorizationHandler authorizationHandler,
        TenantToolPolicyGate policyGate,
        IToolArgumentsValidator validator,
        IRunAttributionContext? attribution,
        EgressSocketGuard? egressGuard = null,
        IOptions<TraconMcpSecurityOptions>? securityOptions = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(coreOptions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(tokenCaches);
        ArgumentNullException.ThrowIfNull(authorizationHandler);
        ArgumentNullException.ThrowIfNull(policyGate);
        ArgumentNullException.ThrowIfNull(validator);

        _servers = servers;
        _tenants = tenants;
        _configuration = configuration;
        _coreOptions = coreOptions;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpToolCatalog>();
        _tokenCaches = tokenCaches;
        _authorizationHandler = authorizationHandler;
        _policyGate = policyGate;
        _validator = validator;
        _attribution = attribution;
        _egressGuard = egressGuard;
        _keySpace = McpKeySpace.From(securityOptions, coreOptions);
        _timeProvider = timeProvider ?? TimeProvider.System;
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
    /// warning is logged. A remote server crashing must not stop Tracon —
    /// but the caller can learn about it through
    /// <see cref="McpRefreshOutcome.HadUnreachableServers"/>.
    /// </remarks>
    public async ValueTask<McpRefreshOutcome> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Taken BEFORE the servers are listed: a server that is saved and authorized while
            // this refresh runs is not in the list and must not look deleted (see ForgetDeletedServersAsync).
            var knownTokenKeys = _tokenCaches.Keys;
            var tenantIds = await ResolveTenantIdsAsync(cancellationToken).ConfigureAwait(false);
            var byTenant = new Dictionary<string, McpTenantTools>(StringComparer.Ordinal);
            var live = new HashSet<McpTenantServerKey>();
            var registered = new HashSet<McpTenantServerKey>();
            var total = 0;
            var hadUnreachableServers = false;
            var plans = new List<TenantPlan>(tenantIds.Count);

            foreach (var tenantId in tenantIds)
            {
                var plan = new TenantPlan(tenantId);

                foreach (var server in await _servers.ListAsync(tenantId, cancellationToken).ConfigureAwait(false))
                {
                    // Every registered server, reachable or not: a tool named like
                    // one of theirs that is missing from the snapshot then reads as
                    // an UNRESOLVED remote source, never as a code tool.
                    plan.ServerNames.Add(server.Name);
                    registered.Add(new McpTenantServerKey(tenantId, server.Name));

                    if (!server.Enabled)
                    {
                        continue;
                    }

                    var key = new McpTenantServerKey(tenantId, server.Name);
                    live.Add(key);
                    plan.Connects.Add(new ServerConnect(key, server));
                }

                plans.Add(plan);
            }

            // Each key is its own work item and owns its result slot; nothing else is shared.
            await Parallel.ForEachAsync(
                    plans.SelectMany(static plan => plan.Connects.Select(connect => (plan.TenantId, connect))),
                    new ParallelOptions { MaxDegreeOfParallelism = MaxParallelConnections, CancellationToken = cancellationToken },
                    async (item, token) =>
                    {
                        (item.connect.Connection, item.connect.Unreachable) =
                            await EnsureConnectionAsync(item.TenantId, item.connect.Key, item.connect.Server, token).ConfigureAwait(false);
                    })
                .ConfigureAwait(false);

            foreach (var plan in plans)
            {
                var registrations = new List<TraconToolRegistration>();
                var sources = new Dictionary<TraconToolRegistration, string>(ReferenceEqualityComparer.Instance);
                var generations = new Dictionary<TraconToolRegistration, long>(ReferenceEqualityComparer.Instance);

                foreach (var connect in plan.Connects)
                {
                    hadUnreachableServers |= connect.Unreachable;

                    if (connect.Connection is { } connection)
                    {
                        registrations.AddRange(connection.Tools);

                        foreach (var registration in connection.Tools)
                        {
                            sources[registration] = connection.SourceFingerprint;
                            generations[registration] = connection.Generation;
                        }
                    }
                }

                if (registrations.Count > 0 || plan.ServerNames.Count > 0)
                {
                    byTenant[plan.TenantId] = McpTenantTools.Create(
                        registrations,
                        _logger,
                        _authorizationHandler,
                        _policyGate,
                        _validator,
                        _coreOptions.Value.Tools.DefaultTimeout,
                        _attribution,
                        _loggerFactory.CreateLogger<AuthorizingAIFunction>(),
                        _loggerFactory.CreateLogger<TimeoutAIFunction>(),
                        _loggerFactory.CreateLogger<ValidatingAIFunction>(),
                        _coreOptions.Value.Tools.DefaultMaxOutputBytes,
                        sources,
                        plan.ServerNames,
                        generations,
                        _timeProvider);
                    total += registrations.Count;
                }
            }

            await CloseRemovedConnectionsAsync(live).ConfigureAwait(false);

            _byTenant = byTenant;

            // After the swap: forgetting a token is cleanup, and a request that is cancelled while the
            // cache answers must not leave the list of this pass unpublished.
            try
            {
                await ForgetDeletedServersAsync(knownTokenKeys, tenantIds, registered, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The next refresh forgets them.
            }

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

        // _refreshGate is not disposed: a refresh that is inside the gate when the catalog is disposed still
        // releases it in its finally, and a closed gate would turn that Release into an ObjectDisposedException
        // that replaces the refresh's own outcome. It is a binary lock with no wait handle, so it holds nothing.
        _connections.Clear();
    }

    /// <summary>
    /// Derives the list of tenants to run discovery for.
    /// </summary>
    /// <remarks>
    /// The default tenant is always added to the registered tenants: tenant
    /// registration is not mandatory, and in a single-tenant setup the
    /// <c>tenants</c> table may be empty.
    /// <para>
    /// A registered slug outside the tenant alphabet is skipped and logged. A store refuses such a value with an
    /// exception, and one stale row must not stop discovery for every other tenant.
    /// </para>
    /// </remarks>
    private async ValueTask<IReadOnlyCollection<string>> ResolveTenantIdsAsync(CancellationToken cancellationToken)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { _coreOptions.Value.DefaultTenantId };

        try
        {
            var skipped = 0;

            foreach (var tenant in await _tenants.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                if (AmbientTenantScope.TryNormalize(tenant.Slug, out var canonicalSlug))
                {
                    ids.Add(canonicalSlug);
                }
                else
                {
                    skipped++;
                }
            }

            if (skipped > 0)
            {
                // The slug is not logged: it is stored text that may hold control characters.
                _logger.LogWarning(
                    "{Skipped} registered tenant slug(s) hold a character outside the tenant alphabet; MCP discovery skips them. The tenant routes refuse such a slug, so correct or remove the rows in the tenants table with SQL.",
                    skipped);
            }
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            _logger.LogWarning(ex, "Could not read the tenant list; MCP discovery will run only for the default tenant.");
        }

        return ids;
    }

    /// <summary>One tenant's servers in one refresh pass.</summary>
    /// <param name="TenantId">The tenant.</param>
    private sealed record TenantPlan(string TenantId)
    {
        /// <summary>Every registered server, enabled or not.</summary>
        public List<string> ServerNames { get; } = [];

        /// <summary>The enabled servers, in the order the store listed them.</summary>
        public List<ServerConnect> Connects { get; } = [];
    }

    /// <summary>One enabled server of a pass and what connecting to it produced.</summary>
    /// <param name="Key">The server's key.</param>
    /// <param name="Server">The server's definition.</param>
    private sealed record ServerConnect(McpTenantServerKey Key, McpServerDefinition Server)
    {
        /// <summary>The live connection, or <see langword="null"/> when there is none.</summary>
        public McpConnection? Connection { get; set; }

        /// <summary>Whether the server could not be reached or listed.</summary>
        public bool Unreachable { get; set; }
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
                // connection would break a tool call in progress. A failed
                // listing that kept the last list still serves this connection's
                // tools, and still counts as unreachable.
                var refresh = await existing.RefreshCatalogAsync(_options.Value, _logger, _timeProvider, cancellationToken)
                    .ConfigureAwait(false);

                return refresh switch
                {
                    McpCatalogRefresh.Listed => (existing, false),
                    McpCatalogRefresh.Kept => (existing, true),
                    _ => (null, true),
                };
            }

            await existing.DisposeAsync().ConfigureAwait(false);
            _connections.TryRemove(key, out _);
        }

        var tokenCache = _tokenCaches.GetOrCreate(tenantId, server);

        var (connection, connectUnreachable) = await McpConnection
            .ConnectAsync(
                server,
                _configuration,
                _options.Value,
                tokenCache,
                _loggerFactory,
                _logger,
                _egressGuard,
                _keySpace,
                _timeProvider,
                cancellationToken)
            .ConfigureAwait(false);

        if (connection is null)
        {
            return (null, connectUnreachable);
        }

        _connections[key] = connection;

        return (connection, false);
    }

    /// <summary>
    /// Forgets the OAuth token of a server that was deleted: one this process held a token cache
    /// for, whose tenant was listed in this pass, and that the tenant no longer lists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A disabled server is still listed, so its token stays. A tenant that was not listed in this
    /// pass (the tenant list failed and only the default tenant was read) is skipped: a failure to
    /// list must not turn into "every server of that tenant was deleted" and a forgotten token,
    /// which the administrator would have to authorize again.
    /// </para>
    /// <para>
    /// In the distributed mode the entry is removed from the shared cache, so every instance sees
    /// the token gone. An instance that never held a cache for the server removes nothing; the entry
    /// then ends by itself (<see cref="TraconMcpOptions.OAuthTokenSlidingLifetime"/>).
    /// </para>
    /// </remarks>
    private async ValueTask ForgetDeletedServersAsync(
        IReadOnlyCollection<McpTenantServerKey> knownTokenKeys,
        IReadOnlyCollection<string> listedTenants,
        HashSet<McpTenantServerKey> registered,
        CancellationToken cancellationToken)
    {
        foreach (var key in knownTokenKeys)
        {
            if (listedTenants.Contains(key.TenantId, StringComparer.Ordinal) && !registered.Contains(key))
            {
                await _tokenCaches.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
            }
        }
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
/// triggers refresh through this interface, so <c>Tracon.AspNetCore</c>
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
