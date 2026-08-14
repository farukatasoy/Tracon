using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentPrism;

/// <summary>
/// Tek bir uzak MCP sunucusuna acilmis baglanti; kesfedilmis tool'lari ve
/// (Mod A/B icin) kaynak onbellegini tasir.
/// </summary>
internal sealed class McpConnection : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly string _serverName;
    private readonly bool _requiresApproval;

    // Mod B'nin (read_resource tool) kendi cagri anindaki turdeki gunlukleyici
    // ve ayarlara erisebilmesi icin en son RefreshCatalogAsync/ConnectAsync
    // cagrisindan saklanir.
    private AgentPrismMcpOptions _options;
    private ILogger _logger;

    // Kaynak onbellegi: URI -> son okunan icerik. Abonelik yalniz bu kaydi
    // gecersiz kilar (Invalidated = true); icerik yeniden okunmaz, bir sonraki
    // istekte taze cekilir (docs/22-MCP-DERINLESMESI.md, bolum 22.2).
    private readonly Dictionary<string, CachedResource> _resourceCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _subscribedUris = new(StringComparer.Ordinal);
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private readonly SemaphoreSlim _resourceGate = new(1, 1);

    private List<Resource> DeclaredResourceCache { get; set; } = [];

    private McpConnection(
        McpClient client,
        string serverName,
        bool requiresApproval,
        string fingerprint,
        AgentPrismMcpOptions options,
        ILogger logger)
    {
        _client = client;
        _serverName = serverName;
        _requiresApproval = requiresApproval;
        _options = options;
        _logger = logger;
        FingerprintValue = fingerprint;
        Tools = [];
    }

    /// <summary>Baglantinin kuruldugu tanimin parmak izi.</summary>
    public string FingerprintValue { get; }

    /// <summary>Bu sunucudan kesfedilmis tool kayitlari (uzak tool'lar + varsa <c>read_resource</c>).</summary>
    public IReadOnlyList<AgentPrismToolRegistration> Tools { get; private set; }

    /// <summary>Sunucunun bildirdigi yetenekler.</summary>
    public ServerCapabilities ServerCapabilities => _client.ServerCapabilities;

    /// <summary>Sunucu <c>prompts</c> yetenegini bildiriyor mu.</summary>
    public bool SupportsPrompts => ServerCapabilities.Prompts is not null;

    /// <summary>Sunucu <c>resources</c> yetenegini bildiriyor mu.</summary>
    public bool SupportsResources => ServerCapabilities.Resources is not null;

    /// <summary>
    /// Sunucu tanimindan baglantiyi yeniden kurmayi gerektiren alanlarin parmak izi.
    /// </summary>
    /// <remarks>
    /// <c>RequiresApproval</c> ve OAuth alanlari parmak izine <strong>dahildir</strong>:
    /// bunlardan biri degisince baglanti yeniden kurulmalidir. <c>Description</c>
    /// dahil degildir; baglantiyi etkilemez.
    /// </remarks>
    public static string ComputeFingerprint(McpServerDefinition server)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{server.Endpoint}|{server.Transport}|{server.AuthorizationConfigurationKey}|" +
            $"{server.RequiresApproval}|{server.OAuthEnabled}|{server.OAuthClientId}|" +
            $"{server.OAuthClientSecretConfigurationKey}|{server.OAuthScopes}|{server.OAuthAuthorizationMode}|" +
            $"{string.Join(",", server.Headers.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))}");

    /// <summary>
    /// Sunucuya baglanir ve tool'larini kesfeder.
    /// </summary>
    /// <returns>
    /// Baglanti (kurulamadiysa <see langword="null"/>) ve baglanma girisiminin
    /// GERCEK bir aglayici hatasiyla (zaman asimi, baglanti reddi) mi yoksa
    /// kasitli bir atlamayla mi basarisiz oldugu (<c>Unreachable</c>).
    /// </returns>
    public static async ValueTask<(McpConnection? Connection, bool Unreachable)> ConnectAsync(
        McpServerDefinition server,
        IConfiguration configuration,
        AgentPrismMcpOptions options,
        ITokenCache tokenCache,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (ShouldSkipConnection(server, options, logger))
        {
            return (null, false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectionTimeout);

        try
        {
            var transportOptions = McpTransportFactory.BuildTransportOptions(server, configuration, options, tokenCache, logger);
            var transport = new HttpClientTransport(transportOptions, loggerFactory);
            var client = await McpClient
                .CreateAsync(transport, clientOptions: null, loggerFactory, timeout.Token)
                .ConfigureAwait(false);

            var connection = new McpConnection(
                client,
                server.Name,
                server.RequiresApproval,
                ComputeFingerprint(server),
                options,
                logger);

            var (refreshed, unreachable) = await connection.RefreshCatalogAsync(options, logger, cancellationToken).ConfigureAwait(false);

            if (refreshed)
            {
                return (connection, false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);

            return (null, unreachable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Uzak sunucunun cokmesi AgentPrism'i durdurmaz: o sunucunun
            // tool'lari listeden duser, digerleri calismaya devam eder. AMA
            // bu GERCEK bir aglayici hatasidir (zaman asimi/baglanti reddi) —
            // caginan taraf bunu "sunucu bilerek atlandi"dan ayirt edebilmelidir
            // (HATA-006, MT-CORE-006).
            logger.LogWarning(
                ex,
                "MCP sunucusu '{ServerName}' ({Endpoint}) baglanamadi; tool'lari bu tazelemede listelenmeyecek.",
                server.Name,
                server.Endpoint);

            return (null, true);
        }
    }

    /// <summary>Baglanti girisiminin hic yapilmamasini gerektiren durumlari denetler.</summary>
    private static bool ShouldSkipConnection(McpServerDefinition server, AgentPrismMcpOptions options, ILogger logger)
    {
        if (!McpToolNaming.IsValidServerName(server.Name))
        {
            logger.LogWarning(
                "MCP sunucusu '{ServerName}' atlandi: ad yalnizca harf, rakam, alt cizgi ve tire icerebilir. " +
                "Tool adlari sunucu adiyla oneklendigi icin bu kisit saglayici tarafindan zorunlu kilinir.",
                server.Name);

            return true;
        }

        if (!McpTransportFactory.IsRemoteHttp(server.Endpoint))
        {
            logger.LogWarning(
                "MCP sunucusu '{ServerName}' atlandi: yalnizca http ve https adresleri kabul edilir. " +
                "Yerel surec (stdio) aktarimi bilerek desteklenmez.",
                server.Name);

            return true;
        }

        if (McpTransportFactory.RequiresUnconfiguredCallback(server, options))
        {
            logger.LogWarning(
                "MCP sunucusu '{ServerName}' atlandi: OAuth acik ama AgentPrism:Mcp:OAuthCallbackBaseUri " +
                "ayarlanmamis. Geri donus adresi olmadan saglayicida kayitli bir yonlendirme kurulamaz.",
                server.Name);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Sunucunun tool ve (destekleniyorsa) kaynak listesini yeniden okur.
    /// </summary>
    /// <returns>
    /// Okuma basarili ise <see langword="true"/>; degilse ayrica bunun GERCEK
    /// bir aglayici hatasindan mi kaynaklandigini bildirir (<c>Unreachable</c>,
    /// HATA-006, MT-CORE-006).
    /// </returns>
    public async ValueTask<(bool Refreshed, bool Unreachable)> RefreshCatalogAsync(
        AgentPrismMcpOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        _options = options;
        _logger = logger;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectionTimeout);

        try
        {
            // Yetenek denetimi zorunludur: sunucu tools bildirmiyorsa istek
            // hic gonderilmez (docs/22-MCP-DERINLESMESI.md, "Doğrulanmış API").
            var discoveredTools = ServerCapabilities.Tools is null
                ? []
                : await _client.ListToolsAsync(options: null, timeout.Token).ConfigureAwait(false);

            var toolRegistrations = Project(discoveredTools, options, logger);

            DeclaredResourceCache = SupportsResources
                ? [
                    .. (await _client.ListResourcesAsync(options: null, timeout.Token).ConfigureAwait(false))
                        .Select(static resource => resource.ProtocolResource),
                  ]
                : [];

            if (DeclaredResourceCache.Count > 0)
            {
                toolRegistrations.Add(CreateReadResourceTool());
            }

            Tools = toolRegistrations;

            return (true, false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "MCP sunucusu '{ServerName}' tool/kaynak listesi okunamadi; onceki liste dusuruldu.",
                _serverName);

            Tools = [];
            DeclaredResourceCache = [];

            return (false, true);
        }
    }

    /// <summary>Sunucunun prompt listesini getirir (Faz 22.1).</summary>
    public async ValueTask<IList<McpClientPrompt>> ListPromptsAsync(CancellationToken cancellationToken)
        => await _client.ListPromptsAsync(options: null, cancellationToken).ConfigureAwait(false);

    /// <summary>Bir prompt'un icerigini argumanlarla cozer (Faz 22.1).</summary>
    public async ValueTask<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
        => await _client.GetPromptAsync(name, arguments, options: null, cancellationToken).ConfigureAwait(false);

    /// <summary>Sunucunun bildirdigi kaynak listesini dondurur (ham protokol tipi).</summary>
    public IReadOnlyList<Resource> DeclaredResources => DeclaredResourceCache;

    /// <summary>
    /// Mod A: bir kaynagi onbellek uzerinden okur. Yalniz bildirilen URI'ler
    /// okunabilir; kabul edilen bir okuma ilk kullanimda kaynagi (destekleniyorsa)
    /// abone yapar.
    /// </summary>
    public async ValueTask<(McpOperationStatus Status, McpResourceContent? Content)> ReadDeclaredResourceAsync(
        string uri,
        int maxBytesPerResource,
        AgentPrismMcpOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!SupportsResources)
        {
            return (McpOperationStatus.CapabilityUnsupported, null);
        }

        if (!DeclaredResourceCache.Exists(resource => string.Equals(resource.Uri, uri, StringComparison.Ordinal)))
        {
            return (McpOperationStatus.UriNotDeclared, null);
        }

        await _resourceGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_resourceCache.TryGetValue(uri, out var cached) && !cached.Invalidated)
            {
                return (McpOperationStatus.Ok, ToContent(uri, cached));
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.ConnectionTimeout);

            var result = await _client.ReadResourceAsync(uri, options: null, timeout.Token).ConfigureAwait(false);
            var fresh = BuildCacheEntry(result, maxBytesPerResource);
            _resourceCache[uri] = fresh;

            await EnsureSubscribedAsync(uri, logger, cancellationToken).ConfigureAwait(false);

            return (McpOperationStatus.Ok, ToContent(uri, fresh));
        }
        catch (McpProtocolException ex) when (ex.ErrorCode == McpErrorCode.ResourceNotFound)
        {
            return (McpOperationStatus.ItemNotFound, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "MCP sunucusu '{ServerName}' kaynagi '{Uri}' okunamadi.", _serverName, uri);

            return (McpOperationStatus.ConnectionFailed, null);
        }
        finally
        {
            _resourceGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Tools = [];
        DeclaredResourceCache = [];
        _resourceCache.Clear();

        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }

        _subscriptions.Clear();
        _subscribedUris.Clear();
        _resourceGate.Dispose();

        await _client.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sunucu <c>resources.subscribe</c> destekliyorsa ve bu URI icin henuz
    /// abonelik kurulmadiysa, abone olur. Aboneligin tek isi bildirim geldiginde
    /// onbellek kaydini gecersiz kilmaktir; icerik bildirimde YENIDEN OKUNMAZ.
    /// </summary>
    private async ValueTask EnsureSubscribedAsync(string uri, ILogger logger, CancellationToken cancellationToken)
    {
        if (ServerCapabilities.Resources?.Subscribe is not true || !_subscribedUris.Add(uri))
        {
            return;
        }

        try
        {
            var subscription = await _client
                .SubscribeToResourceAsync(uri, (_, _) => Invalidate(uri), options: null, cancellationToken)
                .ConfigureAwait(false);

            _subscriptions.Add(subscription);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Abonelik kurulamamasi okumayi basarisiz kilmaz: kaynak bir
            // sonraki calistirmaya kadar onbellekte kalir, gecersiz kilinmaz.
            _subscribedUris.Remove(uri);
            logger.LogWarning(ex, "MCP sunucusu '{ServerName}' kaynagi '{Uri}' icin abonelik kurulamadi.", _serverName, uri);
        }
    }

    private ValueTask Invalidate(string uri)
    {
        if (_resourceCache.TryGetValue(uri, out var cached))
        {
            cached.Invalidated = true;
        }

        return ValueTask.CompletedTask;
    }

    private static McpResourceContent ToContent(string uri, CachedResource cached)
        => new()
        {
            Uri = uri,
            MimeType = cached.MimeType,
            Text = cached.Text,
            IsBinary = cached.IsBinary,
            ByteSize = cached.ByteSize,
            Truncated = cached.Truncated,
        };

    private static CachedResource BuildCacheEntry(ReadResourceResult result, int maxBytesPerResource)
    {
        var contents = result.Contents.FirstOrDefault();

        return contents switch
        {
            TextResourceContents text => BuildTextEntry(text, maxBytesPerResource),
            BlobResourceContents blob => new CachedResource
            {
                MimeType = blob.MimeType,
                Text = null,
                IsBinary = true,
                ByteSize = blob.DecodedData.Length,
                Truncated = false,
            },
            _ => new CachedResource { MimeType = null, Text = string.Empty, IsBinary = false, ByteSize = 0, Truncated = false },
        };
    }

    private static CachedResource BuildTextEntry(TextResourceContents text, int maxBytesPerResource)
    {
        var raw = text.Text ?? string.Empty;
        var byteSize = System.Text.Encoding.UTF8.GetByteCount(raw);

        var (trimmed, truncated) = McpResourceTrimming.Trim(raw, maxBytesPerResource);

        return new CachedResource
        {
            MimeType = text.MimeType,
            Text = trimmed,
            IsBinary = false,
            ByteSize = byteSize,
            Truncated = truncated,
        };
    }

    private AgentPrismToolRegistration CreateReadResourceTool()
    {
        var qualifiedName = $"{_serverName}_read_resource";

        var function = AIFunctionFactory.Create(
            ReadResourceToolBodyAsync,
            name: qualifiedName,
            description: $"'{_serverName}' MCP sunucusunun bildirdigi kayitli bir kaynagi okur. " +
                          "Yalnizca sunucunun kaynak listesinde bulunan URI'ler kabul edilir.");

        return new AgentPrismToolRegistration(function, requiresApproval: _requiresApproval, source: _serverName);
    }

    [Description("Kayitli bir MCP kaynagini okur.")]
    private async Task<string> ReadResourceToolBodyAsync(
        [Description("Okunacak kaynagin URI'si; sunucunun bildirdigi kaynak listesinden birine esit olmalidir.")]
        string uri,
        CancellationToken cancellationToken)
    {
        var (status, content) = await ReadDeclaredResourceAsync(
            uri,
            _options.MaxResourceBytesPerResource,
            _options,
            _logger,
            cancellationToken).ConfigureAwait(false);

        return status switch
        {
            McpOperationStatus.Ok when content is { IsBinary: true } =>
                $"[ikili icerik, {content.ByteSize} bayt, {content.MimeType ?? "bilinmeyen tur"}]",
            McpOperationStatus.Ok => content?.Text ?? string.Empty,
            McpOperationStatus.UriNotDeclared =>
                throw new InvalidOperationException($"'{uri}' '{_serverName}' sunucusunun bildirdigi kaynaklar arasinda degil."),
            McpOperationStatus.CapabilityUnsupported =>
                throw new InvalidOperationException($"'{_serverName}' sunucusu kaynak okumayi desteklemiyor."),
            McpOperationStatus.ItemNotFound =>
                throw new InvalidOperationException($"'{uri}' '{_serverName}' sunucusunda bulunamadi."),
            _ => throw new InvalidOperationException($"'{uri}' okunamadi."),
        };
    }

    private List<AgentPrismToolRegistration> Project(
        IList<McpClientTool> discovered,
        AgentPrismMcpOptions options,
        ILogger logger)
    {
        var registrations = new List<AgentPrismToolRegistration>(discovered.Count);

        foreach (var tool in discovered)
        {
            if (registrations.Count >= options.MaxToolsPerServer)
            {
                logger.LogWarning(
                    "MCP sunucusu '{ServerName}' {Limit} tool sinirini asti; fazlasi atiliyor. " +
                    "Sinir AgentPrism:Mcp:MaxToolsPerServer ile degistirilir.",
                    _serverName,
                    options.MaxToolsPerServer);

                break;
            }

            if (McpToolNaming.TryQualify(_serverName, tool.Name) is not { } qualified)
            {
                logger.LogWarning(
                    "MCP sunucusu '{ServerName}' tool'u '{ToolName}' atlandi: ad yalnizca harf, rakam, " +
                    "alt cizgi ve tire icerebilir.",
                    _serverName,
                    tool.Name);

                continue;
            }

            registrations.Add(new AgentPrismToolRegistration(
                tool.WithName(qualified),
                // Uzak tool tanimi kodda degil, sunucuda yasar ve sunucu onu
                // istedigi zaman degistirebilir. Onay varsayilani bu yuzden acik.
                requiresApproval: _requiresApproval,
                source: _serverName));
        }

        return registrations;
    }

    private sealed class CachedResource
    {
        public required string? MimeType { get; init; }

        public required string? Text { get; init; }

        public required bool IsBinary { get; init; }

        public required int ByteSize { get; init; }

        public required bool Truncated { get; init; }

        public bool Invalidated { get; set; }
    }
}
