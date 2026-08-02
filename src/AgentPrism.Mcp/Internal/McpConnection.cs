using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// Tek bir uzak MCP sunucusuna acilmis baglanti ve o sunucudan kesfedilmis tool'lar.
/// </summary>
internal sealed class McpConnection : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly string _serverName;
    private readonly bool _requiresApproval;

    private McpConnection(McpClient client, string serverName, bool requiresApproval, string fingerprint)
    {
        _client = client;
        _serverName = serverName;
        _requiresApproval = requiresApproval;
        FingerprintValue = fingerprint;
        Tools = [];
    }

    /// <summary>Baglantinin kuruldugu tanimin parmak izi.</summary>
    public string FingerprintValue { get; }

    /// <summary>Bu sunucudan kesfedilmis tool kayitlari.</summary>
    public IReadOnlyList<AgentPrismToolRegistration> Tools { get; private set; }

    /// <summary>
    /// Sunucu tanimindan baglantiyi yeniden kurmayi gerektiren alanlarin parmak izi.
    /// </summary>
    /// <remarks>
    /// <c>RequiresApproval</c> parmak izine <strong>dahildir</strong>: onay
    /// zorunlulugu degisince tool'lar yeniden sarilmalidir. <c>Description</c>
    /// dahil degildir; baglantiyi etkilemez.
    /// </remarks>
    public static string ComputeFingerprint(McpServerDefinition server)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{server.Endpoint}|{server.Transport}|{server.AuthorizationConfigurationKey}|" +
            $"{server.RequiresApproval}|{string.Join(",", server.Headers.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))}");

    /// <summary>
    /// Sunucuya baglanir ve tool'larini kesfeder.
    /// </summary>
    /// <returns>Baglanti; kurulamadiysa <see langword="null"/>.</returns>
    public static async ValueTask<McpConnection?> ConnectAsync(
        McpServerDefinition server,
        IConfiguration configuration,
        AgentPrismMcpOptions options,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!McpToolNaming.IsValidServerName(server.Name))
        {
            logger.LogWarning(
                "MCP sunucusu '{ServerName}' atlandi: ad yalnizca harf, rakam, alt cizgi ve tire icerebilir. " +
                "Tool adlari sunucu adiyla oneklendigi icin bu kisit saglayici tarafindan zorunlu kilinir.",
                server.Name);

            return null;
        }

        if (!IsRemoteHttp(server.Endpoint))
        {
            logger.LogWarning(
                "MCP sunucusu '{ServerName}' atlandi: yalnizca http ve https adresleri kabul edilir. " +
                "Yerel surec (stdio) aktarimi bilerek desteklenmez.",
                server.Name);

            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectionTimeout);

        try
        {
            var transportOptions = new HttpClientTransportOptions
            {
                Name = server.Name,
                Endpoint = server.Endpoint,
                TransportMode = server.Transport == McpTransportMode.Sse
                    ? HttpTransportMode.Sse
                    : HttpTransportMode.StreamableHttp,
                AdditionalHeaders = BuildHeaders(server, configuration, logger),
            };

            var transport = new HttpClientTransport(transportOptions, loggerFactory);
            var client = await McpClient
                .CreateAsync(transport, clientOptions: null, loggerFactory, timeout.Token)
                .ConfigureAwait(false);

            var connection = new McpConnection(
                client,
                server.Name,
                server.RequiresApproval,
                ComputeFingerprint(server));

            if (await connection.RefreshToolsAsync(options, logger, cancellationToken).ConfigureAwait(false))
            {
                return connection;
            }

            await connection.DisposeAsync().ConfigureAwait(false);

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Uzak sunucunun cokmesi AgentPrism'i durdurmaz: o sunucunun
            // tool'lari listeden duser, digerleri calismaya devam eder.
            logger.LogWarning(
                ex,
                "MCP sunucusu '{ServerName}' ({Endpoint}) baglanamadi; tool'lari bu tazelemede listelenmeyecek.",
                server.Name,
                server.Endpoint);

            return null;
        }
    }

    /// <summary>Sunucunun tool listesini yeniden okur.</summary>
    /// <returns>Okuma basarili ise <see langword="true"/>.</returns>
    public async ValueTask<bool> RefreshToolsAsync(
        AgentPrismMcpOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ConnectionTimeout);

        try
        {
            var discovered = await _client.ListToolsAsync(options: null, timeout.Token).ConfigureAwait(false);

            Tools = Project(discovered, options, logger);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "MCP sunucusu '{ServerName}' tool listesi okunamadi; onceki liste dusuruldu.",
                _serverName);

            Tools = [];

            return false;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Tools = [];
        await _client.DisposeAsync().ConfigureAwait(false);
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

    [SuppressMessage(
        "Design",
        "MA0089:Optimize string method usage",
        Justification = "Sema karsilastirmasi buyuk/kucuk harfe duyarsiz olmalidir.")]
    private static bool IsRemoteHttp(Uri endpoint)
        => endpoint.IsAbsoluteUri
            && (string.Equals(endpoint.Scheme, "https", StringComparison.OrdinalIgnoreCase)
                || string.Equals(endpoint.Scheme, "http", StringComparison.OrdinalIgnoreCase));
}
