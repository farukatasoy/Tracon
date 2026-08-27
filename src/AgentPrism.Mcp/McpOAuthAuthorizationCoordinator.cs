using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// The <see cref="IMcpOAuthCoordinator"/> implementation: bridges the OAuth
/// Mode 1 (authorization code) flow with the browser redirect the server
/// expects.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModelContextProtocol.Core</c>'s <c>ClientOAuthOptions.AuthorizationCallbackHandler</c>
/// is called <strong>inside</strong> the <c>McpClient.CreateAsync</c> call,
/// before that call completes, and awaits the authorization result (the
/// code). This class bridges that wait across HTTP requests using two
/// <see cref="TaskCompletionSource{TResult}"/> instances:
/// </para>
/// <list type="number">
///   <item><description><see cref="StartAsync"/> starts a connection attempt in the
///   background and waits only until the authorization address is ready.</description></item>
///   <item><description>The administrator grants consent on the provider; the provider
///   redirects to <c>/oauth/callback</c>.</description></item>
///   <item><description><see cref="CompleteAsync"/> forwards the code to the pending
///   background task and awaits the outcome of the token exchange.</description></item>
/// </list>
/// <para>
/// After a successful exchange, the tokens are shared with
/// <see cref="McpToolCatalog"/>'s background reconnections through
/// <see cref="McpOAuthTokenCacheRegistry"/>; they are never written to the database.
/// </para>
/// </remarks>
internal sealed class McpOAuthAuthorizationCoordinator : IMcpOAuthCoordinator
{
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromMinutes(10);

    private readonly IMcpServerStore _servers;
    private readonly IConfiguration _configuration;
    private readonly IOptions<AgentPrismMcpOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpOAuthAuthorizationCoordinator> _logger;
    private readonly McpOAuthTokenCacheRegistry _tokenCaches;
    private readonly McpToolCatalog _catalog;
    private readonly EgressSocketGuard? _egressGuard;
    private readonly string _allowedConfigurationPrefix;

    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = new(StringComparer.Ordinal);

    public McpOAuthAuthorizationCoordinator(
        IMcpServerStore servers,
        IConfiguration configuration,
        IOptions<AgentPrismMcpOptions> options,
        ILoggerFactory loggerFactory,
        McpOAuthTokenCacheRegistry tokenCaches,
        McpToolCatalog catalog,
        EgressSocketGuard? egressGuard = null,
        IOptions<AgentPrismMcpSecurityOptions>? securityOptions = null)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(tokenCaches);
        ArgumentNullException.ThrowIfNull(catalog);

        _servers = servers;
        _configuration = configuration;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpOAuthAuthorizationCoordinator>();
        _tokenCaches = tokenCaches;
        _catalog = catalog;
        _egressGuard = egressGuard;
        _allowedConfigurationPrefix = (securityOptions?.Value ?? new AgentPrismMcpSecurityOptions())
            .AllowedConfigurationPrefix;
    }

    /// <inheritdoc />
    public async ValueTask<McpOAuthStartResult> StartAsync(
        string tenantId,
        string serverName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);

        PurgeExpired();

        var server = await _servers.GetAsync(tenantId, serverName, cancellationToken).ConfigureAwait(false);

        if (server is null)
        {
            return new McpOAuthStartResult { Status = McpOAuthOperationStatus.ServerNotFound };
        }

        var mcpOptions = _options.Value;

        if (!server.OAuthEnabled
            || server.OAuthAuthorizationMode != McpOAuthAuthorizationMode.AuthorizationCode
            || mcpOptions.OAuthCallbackBaseUri is not { } baseUri)
        {
            return new McpOAuthStartResult { Status = McpOAuthOperationStatus.NotConfigured };
        }

        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        var pending = new PendingAuthorization(tenantId, serverName);
        _pending[state] = pending;

        _ = RunAuthorizationAsync(state, server, baseUri, pending);

        using var timeout = new CancellationTokenSource(mcpOptions.ConnectionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var authorizationUri = await pending.AuthorizationUriReady.Task.WaitAsync(linked.Token).ConfigureAwait(false);

            return new McpOAuthStartResult
            {
                Status = McpOAuthOperationStatus.Ok,
                AuthorizationUri = authorizationUri,
                State = state,
            };
        }
        catch (Exception)
        {
            // Either a timeout (the authorization address was never
            // produced) or the background connection attempt failed before
            // reaching the OAuth step (e.g. a DNS/TLS error); both are the
            // same "could not connect" outcome.
            _pending.TryRemove(state, out _);

            return new McpOAuthStartResult { Status = McpOAuthOperationStatus.ConnectionFailed };
        }
    }

    /// <inheritdoc />
    public async ValueTask<McpOAuthCompleteResult> CompleteAsync(
        string state,
        string? code,
        string? iss,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        if (!_pending.TryGetValue(state, out var pending))
        {
            return new McpOAuthCompleteResult { Status = McpOAuthOperationStatus.InvalidState };
        }

        if (string.IsNullOrEmpty(code))
        {
            pending.CodeReceived.TrySetException(
                new InvalidOperationException("The provider did not return an authorization code; the user may have declined."));
        }
        else
        {
            pending.CodeReceived.TrySetResult(new AuthorizationResult { Code = code, State = state, Iss = iss });
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var (success, error) = await pending.Completed.Task.WaitAsync(linked.Token).ConfigureAwait(false);

            return new McpOAuthCompleteResult
            {
                Status = success ? McpOAuthOperationStatus.Ok : McpOAuthOperationStatus.AuthorizationFailed,
                ServerName = pending.ServerName,
                Error = error,
            };
        }
        catch (OperationCanceledException)
        {
            return new McpOAuthCompleteResult
            {
                Status = McpOAuthOperationStatus.AuthorizationFailed,
                ServerName = pending.ServerName,
                Error = "The token exchange timed out.",
            };
        }
    }

    /// <summary>
    /// Starts a <c>McpClient.CreateAsync</c> connection with interactive
    /// OAuth. Completes <see cref="PendingAuthorization.AuthorizationUriReady"/>
    /// once the authorization address is ready, and
    /// <see cref="PendingAuthorization.Completed"/> once the process finishes.
    /// </summary>
    private async Task RunAuthorizationAsync(string state, McpServerDefinition server, Uri baseUri, PendingAuthorization pending)
    {
        try
        {
            var tokenCache = _tokenCaches.GetOrCreate(pending.TenantId, pending.ServerName);

            var transportOptions = new HttpClientTransportOptions
            {
                Name = server.Name,
                Endpoint = server.Endpoint,
                TransportMode = server.Transport == McpTransportMode.Sse
                    ? HttpTransportMode.Sse
                    : HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string>(server.Headers, StringComparer.OrdinalIgnoreCase),
                OAuth = new ClientOAuthOptions
                {
                    ClientId = server.OAuthClientId,
                    ClientSecret = McpTransportFactory.ResolveClientSecret(server, _configuration, _allowedConfigurationPrefix, _logger),
                    Scopes = McpTransportFactory.ParseScopes(server.OAuthScopes),
                    RedirectUri = McpTransportFactory.BuildCallbackUri(baseUri, server.Name),
                    TokenCache = tokenCache,
                    AuthorizationCallbackHandler = async (context, callbackToken) =>
                    {
                        pending.AuthorizationUriReady.TrySetResult(context.AuthorizationUri);

                        return await pending.CodeReceived.Task.WaitAsync(callbackToken).ConfigureAwait(false);
                    },
                },
            };

            var transport = McpTransportFactory.CreateTransport(transportOptions, _egressGuard, _loggerFactory);

            // A reasonable upper bound for the administrator to grant consent on the provider.
            using var timeout = new CancellationTokenSource(AuthorizationWindow);
            var client = await McpClient
                .CreateAsync(transport, clientOptions: null, _loggerFactory, timeout.Token)
                .ConfigureAwait(false);

            try
            {
                pending.Completed.TrySetResult((true, null));

                // New tokens are ready; refresh the catalog now so tools
                // become visible immediately instead of waiting for the next
                // periodic cycle.
                await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var correlationId = SafeErrorText.NewCorrelationId();

            pending.AuthorizationUriReady.TrySetException(ex);
            pending.Completed.TrySetResult((false, SafeErrorText.ForPersistence(ex, correlationId)));

            _logger.LogWarning(ex, "OAuth authorization for MCP server '{ServerName}' failed. (ref: {CorrelationId})", server.Name, correlationId);
        }
        finally
        {
            _pending.TryRemove(state, out _);
        }
    }

    /// <summary>Clears pending entries that have expired (the administrator abandoned the flow).</summary>
    private void PurgeExpired()
    {
        var cutoff = DateTimeOffset.UtcNow - AuthorizationWindow;

        foreach (var (state, pending) in _pending)
        {
            if (pending.CreatedAt < cutoff)
            {
                _pending.TryRemove(state, out _);
                pending.CodeReceived.TrySetCanceled();
            }
        }
    }

    private sealed class PendingAuthorization(string tenantId, string serverName)
    {
        public string TenantId { get; } = tenantId;

        public string ServerName { get; } = serverName;

        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        public TaskCompletionSource<Uri> AuthorizationUriReady { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AuthorizationResult?> CodeReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<(bool Success, string? Error)> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
