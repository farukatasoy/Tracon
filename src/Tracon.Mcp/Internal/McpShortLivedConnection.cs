using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Tracon;

/// <summary>
/// The short-lived connection setup shared by <see cref="McpPromptClient"/> and
/// <see cref="McpResourceClient"/>.
/// </summary>
internal static class McpShortLivedConnection
{
    /// <summary>
    /// Connects to a server once. The caller must dispose the returned client when
    /// it finishes its work.
    /// </summary>
    public static async ValueTask<(McpOperationStatus Status, McpClient? Client)> ConnectAsync(
        string tenantId,
        string serverName,
        IMcpServerStore servers,
        IConfiguration configuration,
        TraconMcpOptions options,
        McpOAuthTokenCacheRegistry tokenCaches,
        ILoggerFactory loggerFactory,
        ILogger logger,
        EgressSocketGuard? egressGuard,
        McpKeySpace keySpace,
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
        var transportOptions = McpTransportFactory.BuildTransportOptions(
            server,
            configuration,
            options,
            keySpace,
            tokenCache,
            logger);
        var transport = McpTransportFactory.CreateTransport(transportOptions, egressGuard, loggerFactory);

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
            logger.LogWarning(ex, "Could not connect to MCP server '{ServerName}'.", server.Name);

            return (McpOperationStatus.ConnectionFailed, null);
        }
    }
}
