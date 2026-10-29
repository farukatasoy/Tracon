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
/// <para>
/// <strong>Two instances.</strong> The flow lives in the process that started it: the SDK's connection
/// call waits there, and so does the PKCE verifier. With
/// <see cref="McpOAuthTokenStore.Memory"/> only that instance can finish the flow. With
/// <see cref="McpOAuthTokenStore.DistributedCache"/> the provider's redirect may reach any instance:
/// the instance that receives it leaves the code in the distributed cache
/// (<see cref="McpOAuthRendezvous"/>), the owner takes it and exchanges it, and the instance that
/// received the redirect answers the browser with the owner's result.
/// </para>
/// </remarks>
internal sealed class McpOAuthAuthorizationCoordinator : IMcpOAuthCoordinator
{
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromMinutes(10);

    // The instance that gets a callback it does not know cannot tell a callback that belongs to
    // another instance from one that expired or was forged; the text names both.
    private const string UnknownStateInMemoryMode =
        "The authorization session was not found or has expired. Try again. " +
        "If this application runs on more than one instance, this instance may not have started the authorization; " +
        "set Tracon:Mcp:OAuthTokenStore=DistributedCache.";

    private const string ExchangeTimedOut = "The token exchange timed out.";

    private readonly IMcpServerStore _servers;
    private readonly IConfiguration _configuration;
    private readonly IOptions<TraconMcpOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<McpOAuthAuthorizationCoordinator> _logger;
    private readonly McpOAuthTokenCacheRegistry _tokenCaches;
    private readonly McpToolCatalog _catalog;
    private readonly EgressSocketGuard? _egressGuard;
    private readonly McpKeySpace _keySpace;
    private readonly McpOAuthRendezvous? _rendezvous;

    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = new(StringComparer.Ordinal);

    public McpOAuthAuthorizationCoordinator(
        IMcpServerStore servers,
        IConfiguration configuration,
        IOptions<TraconMcpOptions> options,
        ILoggerFactory loggerFactory,
        McpOAuthTokenCacheRegistry tokenCaches,
        McpToolCatalog catalog,
        McpOAuthDistributedBackend backend,
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
        ArgumentNullException.ThrowIfNull(backend);

        _servers = servers;
        _configuration = configuration;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<McpOAuthAuthorizationCoordinator>();
        _tokenCaches = tokenCaches;
        _catalog = catalog;
        _egressGuard = egressGuard;
        _keySpace = McpKeySpace.From(securityOptions, coreOptions);
        _rendezvous = backend.Enabled
            ? new McpOAuthRendezvous(backend, loggerFactory.CreateLogger<McpOAuthRendezvous>())
            : null;
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
            return _rendezvous is null
                ? new McpOAuthCompleteResult { Status = McpOAuthOperationStatus.InvalidState, Error = UnknownStateInMemoryMode }
                : await CompleteOnOtherInstanceAsync(_rendezvous, state, code, iss, cancellationToken).ConfigureAwait(false);
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
                Error = ExchangeTimedOut,
            };
        }
    }

    /// <summary>
    /// Handles a callback whose flow another instance started: leaves the code in the distributed
    /// cache for that instance and answers with the result it leaves back.
    /// </summary>
    private async ValueTask<McpOAuthCompleteResult> CompleteOnOtherInstanceAsync(
        McpOAuthRendezvous rendezvous,
        string state,
        string? code,
        string? iss,
        CancellationToken cancellationToken)
    {
        // 🚨 This is the callback endpoint (no bearer token, no rate limit) writing to a shared cache. Nothing is written
        // until the state is shaped like one the SDK generates, the values are small, and an
        // instance announced a flow under that state.
        if (!McpOAuthRendezvous.IsAcceptableState(state) || !McpOAuthRendezvous.AreAcceptableValues(code, iss))
        {
            return new McpOAuthCompleteResult { Status = McpOAuthOperationStatus.InvalidState };
        }

        try
        {
            if (!await rendezvous.IsAnnouncedAsync(state, cancellationToken).ConfigureAwait(false))
            {
                return new McpOAuthCompleteResult { Status = McpOAuthOperationStatus.InvalidState };
            }

            await rendezvous
                .DepositCallbackAsync(state, string.IsNullOrEmpty(code) ? null : code, iss, cancellationToken)
                .ConfigureAwait(false);

            var outcome = await rendezvous.AwaitOutcomeAsync(state, cancellationToken).ConfigureAwait(false);

            if (outcome is null)
            {
                return new McpOAuthCompleteResult
                {
                    Status = McpOAuthOperationStatus.AuthorizationFailed,
                    Error = ExchangeTimedOut,
                };
            }

            if (outcome.Success)
            {
                // The token is in the shared cache; this instance lists the server's tools now
                // instead of at the next refresh interval.
                RefreshCatalogInBackground();
            }

            return new McpOAuthCompleteResult
            {
                Status = outcome.Success ? McpOAuthOperationStatus.Ok : McpOAuthOperationStatus.AuthorizationFailed,
                ServerName = outcome.ServerName,
                Error = outcome.Error,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new McpOAuthCompleteResult
            {
                Status = McpOAuthOperationStatus.AuthorizationFailed,
                Error = ExchangeTimedOut,
            };
        }
        catch (Exception ex)
        {
            var correlationId = SafeErrorText.NewCorrelationId();

            _logger.LogWarning(
                ex,
                "Could not hand an MCP OAuth callback to the instance that started the authorization. (ref: {CorrelationId})",
                correlationId);

            return new McpOAuthCompleteResult
            {
                Status = McpOAuthOperationStatus.AuthorizationFailed,
                Error = SafeErrorText.ForPersistence(ex, correlationId),
            };
        }
    }

    private void RefreshCatalogInBackground()
        => _ = Task.Run(async () =>
        {
            try
            {
                await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "The MCP tool catalog could not be refreshed after an authorization that finished on another instance.");
            }
        });

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

                        pending.Register(state);
                        _pending[state] = pending;

                        if (_rendezvous is not null)
                        {
                            // The marker goes in BEFORE the address goes out: no redirect can come
                            // earlier than the administrator's click, and another instance accepts
                            // a redirect only for a state it finds announced.
                            await _rendezvous.AnnounceAsync(state, callbackToken).ConfigureAwait(false);
                        }

                        pending.AuthorizationUriReady.TrySetResult(context.AuthorizationUri);

                        using var stopPolling = CancellationTokenSource.CreateLinkedTokenSource(callbackToken);
                        var polling = _rendezvous is null
                            ? Task.CompletedTask
                            : TakeCodeFromOtherInstanceAsync(_rendezvous, state, pending, stopPolling.Token);

                        try
                        {
                            return await pending.CodeReceived.Task.WaitAsync(callbackToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            await stopPolling.CancelAsync().ConfigureAwait(false);
                            await polling.ConfigureAwait(false);
                        }
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
                if (pending.Completed.TrySetResult((true, null)))
                {
                    await PublishOutcomeAsync(pending, success: true, error: null).ConfigureAwait(false);
                }

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

            var error = SafeErrorText.ForPersistence(ex, correlationId);

            pending.AuthorizationUriReady.TrySetException(ex);

            if (pending.Completed.TrySetResult((false, error)))
            {
                await PublishOutcomeAsync(pending, success: false, error).ConfigureAwait(false);
            }

            _logger.LogWarning(ex, "OAuth authorization for MCP server '{ServerName}' failed. (ref: {CorrelationId})", server.Name, correlationId);
        }
        finally
        {
            Forget(pending);

            if (_rendezvous is not null)
            {
                foreach (var state in pending.States)
                {
                    await _rendezvous.ForgetAsync(state).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Takes the code an instance that received the provider's redirect left in the distributed
    /// cache, and hands it to the flow as if the redirect had arrived here.
    /// </summary>
    private async Task TakeCodeFromOtherInstanceAsync(
        McpOAuthRendezvous rendezvous,
        string state,
        PendingAuthorization pending,
        CancellationToken cancellationToken)
    {
        try
        {
            var envelope = await rendezvous.AwaitCallbackAsync(state, cancellationToken).ConfigureAwait(false);

            // Set before the code is handed over: the outcome goes back to the instance that is waiting for it.
            pending.RendezvousState = state;

            if (McpOAuthRendezvous.ToAuthorizationResult(state, envelope) is { } result)
            {
                pending.CodeReceived.TrySetResult(result);
            }
            else
            {
                pending.CodeReceived.TrySetException(
                    new InvalidOperationException("The provider did not return an authorization code; the user may have declined."));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The flow ended by another way: the code came here, or the time was over.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Waiting for the callback of an MCP OAuth flow in the distributed cache failed.");
            pending.CodeReceived.TrySetException(ex);
        }
    }

    /// <summary>Leaves the result of the flow for the instance that received the redirect, when one did.</summary>
    private async ValueTask PublishOutcomeAsync(PendingAuthorization pending, bool success, string? error)
    {
        if (_rendezvous is not null && pending.RendezvousState is { } state)
        {
            await _rendezvous
                .PublishOutcomeAsync(state, new McpOutcomeEnvelope(success, pending.ServerName, error))
                .ConfigureAwait(false);
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
        private readonly List<string> _states = [];

        public string TenantId { get; } = tenantId;

        public string ServerName { get; } = serverName;

        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// The state the SDK put into the authorization address; set before
        /// <see cref="AuthorizationUriReady"/> completes, so a reader that awaited it sees it.
        /// </summary>
        public string? State { get; private set; }

        /// <summary>
        /// Gets the state of the redirect another instance handed over; the result of the flow goes
        /// back under it. <see langword="null"/> when the redirect came to this instance.
        /// </summary>
        public string? RendezvousState { get; set; }

        /// <summary>Gets every state the flow registered, a scope step-up adding one.</summary>
        public IReadOnlyCollection<string> States
        {
            get
            {
                lock (_states)
                {
                    return [.. _states];
                }
            }
        }

        /// <summary>Records a state the SDK put into an authorization address of this flow.</summary>
        /// <param name="state">The state.</param>
        public void Register(string state)
        {
            lock (_states)
            {
                _states.Add(state);
            }

            State = state;
        }

        public TaskCompletionSource<Uri> AuthorizationUriReady { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AuthorizationResult?> CodeReceived { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<(bool Success, string? Error)> Completed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
