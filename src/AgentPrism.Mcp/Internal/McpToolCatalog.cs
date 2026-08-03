using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// Kiraci basina kesfedilmis MCP tool'larini tutar ve tazeler.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Neden onbellek?</strong> <see cref="IToolRegistry"/> es zamanlidir
/// (<c>List</c>, <c>TryGet</c>); MCP kesfi ise ag uzerinden yapilan bir async
/// islemdir. Kesif arka planda yapilir, sonucu burada saklanir ve defter onu
/// es zamanli okur. Kesif henuz tamamlanmadiysa MCP tool'lari gorunmez ve o
/// tool'a isaret eden bir agent derlenirken acik bir hata verir — sessizce
/// eksik tool'la calismaz.
/// </para>
/// <para>
/// <strong>Baglanti neden saklaniyor?</strong> <c>McpClientTool</c> cagri aninda
/// istemci uzerinden uzak sunucuya gider; istemci kapatilirsa tool cagrilamaz.
/// Bu yuzden baglantilar tazeleme arasinda ayakta tutulur ve yalnizca sunucu
/// tanimi <em>degistiginde</em> yeniden kurulur. Degismeyen bir sunucunun
/// baglantisini her tazelemede kapatmak, o sirada devam eden bir tool cagrisini
/// kirardi.
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

    // Okuma yolu kilitsizdir: her tazeleme yeni bir sozluk kurar ve referansi
    // atomik olarak degistirir. Okuyucular tutarli bir anlik goruntu gorur.
    private volatile Dictionary<string, McpTenantTools> _byTenant = new(StringComparer.Ordinal);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    // ConcurrentDictionary: yazma her zaman _refreshGate altinda tek yazardir,
    // ancak Mod A baglam saglayicisi (McpResourceContextProvider) her agent
    // calistirmasinda kilitsiz okur. Duz Dictionary'de bu okuma bir tazeleme ile
    // yarisirsa bozulurdu.
    private readonly ConcurrentDictionary<string, McpConnection> _connections = new(StringComparer.Ordinal);

    public McpToolCatalog(
        IMcpServerStore servers,
        ITenantStore tenants,
        IConfiguration configuration,
        IOptions<AgentPrismOptions> coreOptions,
        IOptions<AgentPrismMcpOptions> options,
        ILoggerFactory loggerFactory,
        McpOAuthTokenCacheRegistry tokenCaches)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(tenants);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(coreOptions);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(tokenCaches);

        _servers = servers;
        _tenants = tenants;
        _configuration = configuration;
        _coreOptions = coreOptions;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpToolCatalog>();
        _tokenCaches = tokenCaches;
    }

    /// <summary>Bir kiracinin kesfedilmis tool'larini dondurur.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <returns>Tool kumesi; kesif yapilmadiysa bos kume.</returns>
    public McpTenantTools ForTenant(string tenantId)
        => _byTenant.TryGetValue(tenantId, out var tools) ? tools : McpTenantTools.Empty;

    /// <summary>
    /// Bir kiracinin belirli bir sunucuya ait canli baglantisini dondurur (Mod A).
    /// </summary>
    /// <returns>Baglanti su an ayakta ise <see langword="true"/>; sunucu erisilemezse veya kapaliysa <see langword="false"/>.</returns>
    public bool TryGetConnection(string tenantId, string serverName, [NotNullWhen(true)] out McpConnection? connection)
        => _connections.TryGetValue($"{tenantId}{serverName}", out connection);

    /// <summary>
    /// Tum kiracilarin sunucularini tarar ve tool listesini tazeler.
    /// </summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kesfedilen toplam tool sayisi.</returns>
    /// <remarks>
    /// Bir sunucuya ulasilamamasi <strong>hata degildir</strong>: o sunucunun
    /// tool'lari listeden duser, digerleri calismaya devam eder ve bir uyari
    /// loglanir. Uzak bir sunucunun cokmesi AgentPrism'i durdurmamalidir.
    /// </remarks>
    public async ValueTask<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var tenantIds = await ResolveTenantIdsAsync(cancellationToken).ConfigureAwait(false);
            var byTenant = new Dictionary<string, McpTenantTools>(StringComparer.Ordinal);
            var live = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;

            foreach (var tenantId in tenantIds)
            {
                var registrations = new List<AgentPrismToolRegistration>();

                foreach (var server in await _servers.ListAsync(tenantId, cancellationToken).ConfigureAwait(false))
                {
                    if (!server.Enabled)
                    {
                        continue;
                    }

                    var key = $"{tenantId}{server.Name}";
                    live.Add(key);

                    var connection = await EnsureConnectionAsync(tenantId, key, server, cancellationToken).ConfigureAwait(false);

                    if (connection is not null)
                    {
                        registrations.AddRange(connection.Tools);
                    }
                }

                if (registrations.Count > 0)
                {
                    byTenant[tenantId] = McpTenantTools.Create(registrations, _logger);
                    total += registrations.Count;
                }
            }

            await CloseRemovedConnectionsAsync(live).ConfigureAwait(false);

            _byTenant = byTenant;

            return total;
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
    /// Kesif yapilacak kiracilarin listesini cikarir.
    /// </summary>
    /// <remarks>
    /// Kayitli kiracilara varsayilan kiraci her zaman eklenir: kiraci kaydi
    /// zorunlu degildir ve tek kiracili bir kurulumda <c>tenants</c> tablosu
    /// bos olabilir.
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
            _logger.LogWarning(ex, "Kiraci listesi okunamadi; MCP kesfi yalnizca varsayilan kiraci icin yapilacak.");
        }

        return ids;
    }

    private async ValueTask<McpConnection?> EnsureConnectionAsync(
        string tenantId,
        string key,
        McpServerDefinition server,
        CancellationToken cancellationToken)
    {
        var fingerprint = McpConnection.ComputeFingerprint(server);

        if (_connections.TryGetValue(key, out var existing))
        {
            if (string.Equals(existing.FingerprintValue, fingerprint, StringComparison.Ordinal))
            {
                // Tanim degismedi: baglanti ayakta kalir, yalnizca tool listesi
                // tazelenir. Degismeyen bir baglantiyi kapatmak devam eden bir
                // tool cagrisini kirardi.
                return await existing.RefreshCatalogAsync(_options.Value, _logger, cancellationToken)
                    .ConfigureAwait(false)
                    ? existing
                    : null;
            }

            await existing.DisposeAsync().ConfigureAwait(false);
            _connections.TryRemove(key, out _);
        }

        var tokenCache = _tokenCaches.GetOrCreate(tenantId, server.Name);

        var connection = await McpConnection
            .ConnectAsync(server, _configuration, _options.Value, tokenCache, _loggerFactory, _logger, cancellationToken)
            .ConfigureAwait(false);

        if (connection is null)
        {
            return null;
        }

        _connections[key] = connection;

        return connection;
    }

    private async ValueTask CloseRemovedConnectionsAsync(HashSet<string> live)
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
/// <see cref="IMcpToolRefresher"/> uygulamasi. HTTP katmani tazelemeyi bu
/// arayuz uzerinden tetikler; boylece <c>AgentPrism.AspNetCore</c> MCP paketine
/// bagli kalmaz.
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
    public ValueTask<int> RefreshAsync(CancellationToken cancellationToken = default)
        => _catalog.RefreshAsync(cancellationToken);
}
