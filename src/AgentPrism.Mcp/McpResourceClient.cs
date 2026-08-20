using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AgentPrism;

/// <summary>An implementation of <see cref="IMcpResourceClient"/>.</summary>
/// <remarks>
/// Each call creates a separate short-lived connection. This has the same rationale
/// as <see cref="McpPromptClient"/>. <see cref="ReadResourceAsync"/> accepts only
/// URIs declared by the server. Reading an arbitrary URI carries an SSRF risk.
/// </remarks>
internal sealed class McpResourceClient : IMcpResourceClient
{
    private readonly IMcpServerStore _servers;
    private readonly IConfiguration _configuration;
    private readonly IOptions<AgentPrismMcpOptions> _options;
    private readonly McpOAuthTokenCacheRegistry _tokenCaches;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpResourceClient> _logger;

    public McpResourceClient(
        IMcpServerStore servers,
        IConfiguration configuration,
        IOptions<AgentPrismMcpOptions> options,
        McpOAuthTokenCacheRegistry tokenCaches,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tokenCaches);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _servers = servers;
        _configuration = configuration;
        _options = options;
        _tokenCaches = tokenCaches;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpResourceClient>();
    }

    /// <inheritdoc />
    public async ValueTask<McpResourceListResult> ListResourcesAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        var (status, client) = await ConnectAsync(tenantId, serverName, cancellationToken).ConfigureAwait(false);

        if (status != McpOperationStatus.Ok || client is null)
        {
            return new McpResourceListResult { Status = status };
        }

        try
        {
            if (client.ServerCapabilities.Resources is null)
            {
                return new McpResourceListResult { Status = McpOperationStatus.CapabilityUnsupported };
            }

            var resources = await client.ListResourcesAsync(options: null, cancellationToken).ConfigureAwait(false);

            return new McpResourceListResult
            {
                Status = McpOperationStatus.Ok,
                Resources = resources.Select(Project).ToList(),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read the resource list from MCP server '{ServerName}'.", serverName);

            return new McpResourceListResult { Status = McpOperationStatus.ConnectionFailed };
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<(McpOperationStatus Status, McpResourceContent? Content)> ReadResourceAsync(
        string tenantId,
        string serverName,
        string uri,
        CancellationToken cancellationToken = default)
    {
        var (status, client) = await ConnectAsync(tenantId, serverName, cancellationToken).ConfigureAwait(false);

        if (status != McpOperationStatus.Ok || client is null)
        {
            return (status, null);
        }

        try
        {
            if (client.ServerCapabilities.Resources is null)
            {
                return (McpOperationStatus.CapabilityUnsupported, null);
            }

            var declared = await client.ListResourcesAsync(options: null, cancellationToken).ConfigureAwait(false);

            if (!declared.Any(resource => string.Equals(resource.Uri, uri, StringComparison.Ordinal)))
            {
                return (McpOperationStatus.UriNotDeclared, null);
            }

            var result = await client.ReadResourceAsync(uri, options: null, cancellationToken).ConfigureAwait(false);

            return Project(uri, result.Contents.FirstOrDefault(), _options.Value.MaxResourceBytesPerResource);
        }
        catch (McpProtocolException ex) when (ex.ErrorCode == McpErrorCode.ResourceNotFound)
        {
            return (McpOperationStatus.ItemNotFound, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not read resource '{Uri}' from MCP server '{ServerName}'.", serverName, uri);

            return (McpOperationStatus.ConnectionFailed, null);
        }
        finally
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ValueTask<(McpOperationStatus Status, McpClient? Client)> ConnectAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken)
        => McpShortLivedConnection.ConnectAsync(
            tenantId,
            serverName,
            _servers,
            _configuration,
            _options.Value,
            _tokenCaches,
            _loggerFactory,
            _logger,
            cancellationToken);

    private static McpResourceSummary Project(McpClientResource resource)
        => new()
        {
            Uri = resource.Uri,
            Name = resource.Name,
            MimeType = resource.MimeType,
            Description = resource.Description,
        };

    private static (McpOperationStatus Status, McpResourceContent? Content) Project(
        string uri,
        ResourceContents? contents,
        int maxBytes)
    {
        switch (contents)
        {
            case TextResourceContents text:
                var (trimmed, truncated) = McpResourceTrimming.Trim(text.Text ?? string.Empty, maxBytes);

                return (McpOperationStatus.Ok, new McpResourceContent
                {
                    Uri = uri,
                    MimeType = text.MimeType,
                    Text = trimmed,
                    IsBinary = false,
                    ByteSize = System.Text.Encoding.UTF8.GetByteCount(text.Text ?? string.Empty),
                    Truncated = truncated,
                });

            case BlobResourceContents blob:
                return (McpOperationStatus.Ok, new McpResourceContent
                {
                    Uri = uri,
                    MimeType = blob.MimeType,
                    Text = null,
                    IsBinary = true,
                    ByteSize = blob.DecodedData.Length,
                    Truncated = false,
                });

            default:
                return (McpOperationStatus.ItemNotFound, null);
        }
    }
}
