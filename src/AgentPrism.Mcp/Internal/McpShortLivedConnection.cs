using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// <see cref="McpPromptClient"/> ve <see cref="McpResourceClient"/>'in
/// paylastigi kisa omurlu baglanti kurulumu.
/// </summary>
internal static class McpShortLivedConnection
{
    /// <summary>
    /// Bir sunucuya tek seferlik baglanir. Cagiran, isini bitirince
    /// dondurulen istemciyi kapatmakla yukumludur.
    /// </summary>
    public static async ValueTask<(McpOperationStatus Status, McpClient? Client)> ConnectAsync(
        string tenantId,
        string serverName,
        IMcpServerStore servers,
        IConfiguration configuration,
        AgentPrismMcpOptions options,
        McpOAuthTokenCacheRegistry tokenCaches,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        var server = await servers.GetAsync(tenantId, serverName, cancellationToken).ConfigureAwait(false);

        if (server is null)
        {
            return (McpOperationStatus.ServerNotFound, null);
        }

        if (!McpToolNaming.IsValidServerName(server.Name)
            || !McpTransportFactory.IsRemoteHttp(server.Endpoint)
            || McpTransportFactory.RequiresUnconfiguredCallback(server, options))
        {
            return (McpOperationStatus.ConnectionFailed, null);
        }

        var tokenCache = tokenCaches.GetOrCreate(tenantId, server.Name);
        var transportOptions = McpTransportFactory.BuildTransportOptions(server, configuration, options, tokenCache, logger);
        var transport = new HttpClientTransport(transportOptions, loggerFactory);

        using var timeout = new CancellationTokenSource(options.ConnectionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var client = await McpClient
                .CreateAsync(transport, clientOptions: null, loggerFactory, linked.Token)
                .ConfigureAwait(false);

            return (McpOperationStatus.Ok, client);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "MCP sunucusu '{ServerName}' baglanamadi.", server.Name);

            return (McpOperationStatus.ConnectionFailed, null);
        }
    }
}
