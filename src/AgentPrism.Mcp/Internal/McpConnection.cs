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
/// An open connection to a single remote MCP server; carries its discovered
/// tools and (for Mode A/B) its resource cache.
/// </summary>
internal sealed class McpConnection : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly string _serverName;
    private readonly bool _requiresApproval;

    // Kept from the most recent RefreshCatalogAsync/ConnectAsync call so that
    // Mode B (the read_resource tool) can access the logger and options of
    // the type at its own call time.
    private AgentPrismMcpOptions _options;
    private ILogger _logger;

    // Resource cache: URI -> the last content read. A subscription only
    // invalidates this entry (Invalidated = true); the content is not
    // re-read immediately, it is fetched fresh on the next request
    // (docs/22-MCP-DERINLESMESI.md, section 22.2).
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

    /// <summary>The fingerprint of the definition the connection was established from.</summary>
    public string FingerprintValue { get; }

    /// <summary>The tool registrations discovered from this server (remote tools + <c>read_resource</c> if present).</summary>
    public IReadOnlyList<AgentPrismToolRegistration> Tools { get; private set; }

    /// <summary>The capabilities reported by the server.</summary>
    public ServerCapabilities ServerCapabilities => _client.ServerCapabilities;

    /// <summary>Whether the server reports the <c>prompts</c> capability.</summary>
    public bool SupportsPrompts => ServerCapabilities.Prompts is not null;

    /// <summary>Whether the server reports the <c>resources</c> capability.</summary>
    public bool SupportsResources => ServerCapabilities.Resources is not null;

    /// <summary>
    /// The fingerprint of the fields in the server definition that require
    /// the connection to be re-established.
    /// </summary>
    /// <remarks>
    /// <c>RequiresApproval</c> and the OAuth fields are <strong>included</strong>
    /// in the fingerprint: when any of them changes, the connection must be
    /// re-established. <c>Description</c> is not included; it does not affect
    /// the connection.
    /// </remarks>
    public static string ComputeFingerprint(McpServerDefinition server)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{server.Endpoint}|{server.Transport}|{server.AuthorizationConfigurationKey}|" +
            $"{server.RequiresApproval}|{server.OAuthEnabled}|{server.OAuthClientId}|" +
            $"{server.OAuthClientSecretConfigurationKey}|{server.OAuthScopes}|{server.OAuthAuthorizationMode}|" +
            $"{string.Join(",", server.Headers.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => $"{pair.Key}={pair.Value}"))}");

    /// <summary>
    /// Connects to the server and discovers its tools.
    /// </summary>
    /// <returns>
    /// The connection (<see langword="null"/> if it could not be established),
    /// and whether the connection attempt failed with a REAL network error
    /// (timeout, connection refused) or with a deliberate skip (<c>Unreachable</c>).
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
            // A remote server crashing does not stop AgentPrism: that server's
            // tools drop out of the list, the others keep working. BUT this is
            // a REAL network error (timeout/connection refused) — the caller
            // must be able to tell this apart from "the server was deliberately
            // skipped" (HATA-006, MT-CORE-006).
            logger.LogWarning(
                ex,
                "MCP server '{ServerName}' ({Endpoint}) could not connect; its tools will not be listed in this refresh.",
                server.Name,
                server.Endpoint);

            return (null, true);
        }
    }

    /// <summary>Checks for conditions that require the connection attempt to never be made at all.</summary>
    private static bool ShouldSkipConnection(McpServerDefinition server, AgentPrismMcpOptions options, ILogger logger)
    {
        if (!McpToolNaming.IsValidServerName(server.Name))
        {
            logger.LogWarning(
                "MCP server '{ServerName}' skipped: the name may contain only letters, digits, underscores, and hyphens. " +
                "Tool names are prefixed with the server name, so the provider enforces this constraint.",
                server.Name);

            return true;
        }

        if (!McpTransportFactory.IsRemoteHttp(server.Endpoint))
        {
            logger.LogWarning(
                "MCP server '{ServerName}' skipped: only http and https addresses are accepted. " +
                "Local process (stdio) transport is deliberately not supported.",
                server.Name);

            return true;
        }

        if (McpTransportFactory.RequiresUnconfiguredCallback(server, options))
        {
            logger.LogWarning(
                "MCP server '{ServerName}' skipped: OAuth is enabled but AgentPrism:Mcp:OAuthCallbackBaseUri " +
                "is not set. Without a callback address, no redirect registered with the provider can be established.",
                server.Name);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Re-reads the server's tool list and (if supported) resource list.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the read succeeded; otherwise also reports
    /// whether this stemmed from a real network error (<c>Unreachable</c>).
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
            // The capability check is mandatory: if the server does not
            // declare tools, the request is never sent at all
            // (docs/22-MCP-DERINLESMESI.md, "Verified API").
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
                "Could not read the tool/resource list of MCP server '{ServerName}'; the previous list was dropped.",
                _serverName);

            Tools = [];
            DeclaredResourceCache = [];

            return (false, true);
        }
    }

    /// <summary>Retrieves the server's prompt list.</summary>
    public async ValueTask<IList<McpClientPrompt>> ListPromptsAsync(CancellationToken cancellationToken)
        => await _client.ListPromptsAsync(options: null, cancellationToken).ConfigureAwait(false);

    /// <summary>Resolves a prompt's content with arguments.</summary>
    public async ValueTask<GetPromptResult> GetPromptAsync(
        string name,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
        => await _client.GetPromptAsync(name, arguments, options: null, cancellationToken).ConfigureAwait(false);

    /// <summary>Returns the resource list reported by the server (raw protocol type).</summary>
    public IReadOnlyList<Resource> DeclaredResources => DeclaredResourceCache;

    /// <summary>
    /// Mode A: reads a resource through the cache. Only declared URIs can be
    /// read; an accepted read subscribes to the resource (if supported) on
    /// first use.
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
            logger.LogWarning(ex, "Could not read resource '{Uri}' of MCP server '{ServerName}'.", uri, _serverName);

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
    /// Subscribes if the server supports <c>resources.subscribe</c> and no
    /// subscription has been established for this URI yet. The only job of
    /// the subscription is to invalidate the cache entry when a notification
    /// arrives; the content is NOT re-read on notification.
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
            // Failing to subscribe does not fail the read: the resource
            // stays in the cache until the next run and is not invalidated.
            _subscribedUris.Remove(uri);
            logger.LogWarning(ex, "Could not subscribe to resource '{Uri}' of MCP server '{ServerName}'.", uri, _serverName);
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
            description: $"Reads a resource declared by the '{_serverName}' MCP server. " +
                          "Only URIs present in the server's resource list are accepted.");

        return new AgentPrismToolRegistration(function, requiresApproval: _requiresApproval, source: _serverName);
    }

    [Description("Reads a declared MCP resource.")]
    private async Task<string> ReadResourceToolBodyAsync(
        [Description("The URI of the resource to read; must match one from the server's declared resource list.")]
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
                $"[binary content, {content.ByteSize} bytes, {content.MimeType ?? "unknown type"}]",
            McpOperationStatus.Ok => content?.Text ?? string.Empty,
            McpOperationStatus.UriNotDeclared =>
                throw new InvalidOperationException($"'{uri}' is not among the resources declared by server '{_serverName}'."),
            McpOperationStatus.CapabilityUnsupported =>
                throw new InvalidOperationException($"Server '{_serverName}' does not support reading resources."),
            McpOperationStatus.ItemNotFound =>
                throw new InvalidOperationException($"'{uri}' was not found on server '{_serverName}'."),
            _ => throw new InvalidOperationException($"'{uri}' could not be read."),
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
                    "MCP server '{ServerName}' exceeded the {Limit} tool limit; the excess is being dropped. " +
                    "The limit is changed with AgentPrism:Mcp:MaxToolsPerServer.",
                    _serverName,
                    options.MaxToolsPerServer);

                break;
            }

            if (McpToolNaming.TryQualify(_serverName, tool.Name) is not { } qualified)
            {
                logger.LogWarning(
                    "Tool '{ToolName}' of MCP server '{ServerName}' was skipped: the name may contain only " +
                    "letters, digits, underscores, and hyphens.",
                    tool.Name,
                    _serverName);

                continue;
            }

            registrations.Add(new AgentPrismToolRegistration(
                tool.WithName(qualified),
                // The remote tool definition lives on the server, not in code,
                // and the server can change it at any time. This is why
                // approval defaults to required.
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
