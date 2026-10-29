using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace Tracon;

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
/// <strong>A pending flow is keyed by the SDK's OWN <c>state</c>.</strong> The SDK puts
/// a <c>state</c> of its own into the authorization address and refuses an answer that
/// carries another one; the provider sends that value back to <c>/oauth/callback</c>. This
/// class once keyed its flows by a <c>state</c> it generated itself, a value that never
/// left the server, so no callback could match and no token was ever obtained.
/// </para>
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
    private readonly IOptions<TraconMcpOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpOAuthAuthorizationCoordinator> _logger;
    private readonly McpOAuthTokenCacheRegistry _tokenCaches;
    private readonly McpToolCatalog _catalog;
    private readonly EgressSocketGuard? _egressGuard;
    private readonly McpKeySpace _keySpace;

    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = new(StringComparer.Ordinal);

    public McpOAuthAuthorizationCoordinator(
        IMcpServerStore servers,
        IConfiguration configuration,
        IOptions<TraconMcpOptions> options,
        ILoggerFactory loggerFactory,
        McpOAuthTokenCacheRegistry tokenCaches,
        McpToolCatalog catalog,
        IOptions<TraconOptions> coreOptions,
        EgressSocketGuard? egressGuard = null,
        IOptions<TraconMcpSecurityOptions>? securityOptions = null)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(coreOptions);
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
        _keySpace = McpKeySpace.From(securityOptions, coreOptions);
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

        // The flow is registered under the SDK's state once the SDK has built the
        // authorization address (RunAuthorizationAsync's callback handler).
        var pending = new PendingAuthorization(tenantId, serverName);

        _ = RunAuthorizationAsync(server, baseUri, pending);

        using var timeout = new CancellationTokenSource(mcpOptions.ConnectionTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var authorizationUri = await pending.AuthorizationUriReady.Task.WaitAsync(linked.Token).ConfigureAwait(false);

            return new McpOAuthStartResult
            {
                Status = McpOAuthOperationStatus.Ok,
                AuthorizationUri = authorizationUri,
                State = pending.State,
            };
        }
        catch (Exception)
        {
            // Either a timeout (the authorization address was never
            // produced) or the background connection attempt failed before
            // reaching the OAuth step (e.g. a DNS/TLS error); both are the
            // same "could not connect" outcome.
            Forget(pending);

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
    private async Task RunAuthorizationAsync(McpServerDefinition server, Uri baseUri, PendingAuthorization pending)
    {
        try
        {
            var tokenCache = _tokenCaches.GetOrCreate(pending.TenantId, server);

            var transportOptions = new HttpClientTransportOptions
            {
                Name = server.Name,
                Endpoint = server.Endpoint,
                TransportMode = server.Transport == McpTransportMode.Sse
                    ? HttpTransportMode.Sse
                    : HttpTransportMode.StreamableHttp,
                // The same builder as the background connection: a copy here
                // missed every header resolved from configuration.
                AdditionalHeaders = McpHeaderBuilder.Build(server, _configuration, _keySpace, _logger),
                OAuth = new ClientOAuthOptions
                {
                    ClientId = server.OAuthClientId,
                    ClientSecret = McpTransportFactory.ResolveClientSecret(server, _configuration, _keySpace, _logger),
                    Scopes = McpTransportFactory.ParseScopes(server.OAuthScopes),
                    RedirectUri = McpTransportFactory.BuildCallbackUri(baseUri, server.Name),
                    TokenCache = tokenCache,
                    AuthorizationCallbackHandler = async (context, callbackToken) =>
                    {
                        // The callback carries the state the SDK put into this address, so
                        // the flow is found by that value (see the class remarks).
                        var state = ReadState(context.AuthorizationUri)
                            ?? throw new InvalidOperationException(
                                "The MCP SDK built an authorization address without a 'state' parameter.");

                        pending.State = state;
                        _pending[state] = pending;
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
            Forget(pending);
        }
    }

    /// <summary>Removes every entry that points at the flow.</summary>
    /// <remarks>
    /// The SDK can call the authorization handler more than once in one flow (a scope
    /// step-up builds a new address with a new state); each value it registered goes.
    /// </remarks>
    private void Forget(PendingAuthorization pending)
    {
        foreach (var (state, entry) in _pending)
        {
            if (ReferenceEquals(entry, pending))
            {
                _pending.TryRemove(state, out _);
            }
        }
    }

    /// <summary>Reads the <c>state</c> query parameter of an authorization address.</summary>
    /// <param name="authorizationUri">The address the SDK built.</param>
    /// <returns>The value; <see langword="null"/> when the address carries none.</returns>
    internal static string? ReadState(Uri authorizationUri)
    {
        foreach (var pair in authorizationUri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0
                && string.Equals(Uri.UnescapeDataString(pair[..separator]), "state", StringComparison.Ordinal)
                && Uri.UnescapeDataString(pair[(separator + 1)..]) is { Length: > 0 } state)
            {
                return state;
            }
        }

        return null;
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

        /// <summary>
        /// The state the SDK put into the authorization address; set before
        /// <see cref="AuthorizationUriReady"/> completes, so a reader that awaited it sees it.
        /// </summary>
        public string? State { get; set; }

        public TaskCompletionSource<Uri> AuthorizationUriReady { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AuthorizationResult?> CodeReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<(bool Success, string? Error)> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
