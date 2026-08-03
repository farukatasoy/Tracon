using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;

namespace AgentPrism;

/// <summary>
/// <see cref="IMcpOAuthCoordinator"/> uygulamasi: OAuth Mod 1 (yetkilendirme
/// kodu) akisini, sunucu tarafinda beklenen bir tarayici yonlendirmesiyle
/// koprulemektedir.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModelContextProtocol.Core</c>'un <c>ClientOAuthOptions.AuthorizationCallbackHandler</c>'i
/// <c>McpClient.CreateAsync</c> cagrisi <strong>icinde</strong>, o cagriyi
/// tamamlamadan once cagrilir ve yetkilendirme sonucunu (kod) bekler. Bu sinif
/// o beklemeyi iki <see cref="TaskCompletionSource{TResult}"/> ile HTTP
/// istekleri arasina koprule:
/// </para>
/// <list type="number">
///   <item><description><see cref="StartAsync"/> bir baglanti girisimini arka planda baslatir ve
///   yalnizca yetkilendirme adresi hazir olana kadar bekler.</description></item>
///   <item><description>Yonetici saglayicida onay verir; saglayici <c>/oauth/callback</c>'e doner.</description></item>
///   <item><description><see cref="CompleteAsync"/> kodu arka plandaki bekleyen goreve iletir ve
///   token degisiminin sonucunu bekler.</description></item>
/// </list>
/// <para>
/// Basarili bir degisim sonrasi token'lar <see cref="McpOAuthTokenCacheRegistry"/>
/// uzerinden <see cref="McpToolCatalog"/>'un arka plan yeniden baglanmalariyla
/// paylasilir; hicbir zaman veritabanina yazilmaz.
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

    private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = new(StringComparer.Ordinal);

    public McpOAuthAuthorizationCoordinator(
        IMcpServerStore servers,
        IConfiguration configuration,
        IOptions<AgentPrismMcpOptions> options,
        ILoggerFactory loggerFactory,
        McpOAuthTokenCacheRegistry tokenCaches,
        McpToolCatalog catalog)
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
            // Ya zaman asimi (yetkilendirme adresi hic uretilemedi) ya da arka
            // plan baglanti girisimi OAuth adimina varmadan basarisiz oldu
            // (ornegin DNS/TLS hatasi); ikisi de ayni "baglanilamadi" sonucudur.
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
                new InvalidOperationException("Saglayici yetkilendirme kodu dondurmedi; kullanici reddetmis olabilir."));
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
                Error = "Token degisimi zaman asimina ugradi.",
            };
        }
    }

    /// <summary>
    /// Bir <c>McpClient.CreateAsync</c> baglantisini etkilesimli OAuth ile
    /// baslatir. Yetkilendirme adresi hazir olunca <see cref="PendingAuthorization.AuthorizationUriReady"/>'i,
    /// islem bitince <see cref="PendingAuthorization.Completed"/>'i tamamlar.
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
                    ClientSecret = McpTransportFactory.ResolveClientSecret(server, _configuration, _logger),
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

            var transport = new HttpClientTransport(transportOptions, _loggerFactory);

            // Yoneticinin saglayicida onay vermesi icin makul bir ust sinir.
            using var timeout = new CancellationTokenSource(AuthorizationWindow);
            var client = await McpClient
                .CreateAsync(transport, clientOptions: null, _loggerFactory, timeout.Token)
                .ConfigureAwait(false);

            try
            {
                pending.Completed.TrySetResult((true, null));

                // Yeni token'lar hazir; katalogu hemen tazele ki tool'lar bir
                // sonraki periyodik dongu yerine simdi gorunsun.
                await _catalog.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            pending.AuthorizationUriReady.TrySetException(ex);
            pending.Completed.TrySetResult((false, ex.Message));

            _logger.LogWarning(ex, "MCP sunucusu '{ServerName}' icin OAuth yetkilendirmesi basarisiz oldu.", server.Name);
        }
        finally
        {
            _pending.TryRemove(state, out _);
        }
    }

    /// <summary>Suresi dolmus (yoneticinin akisi terk ettigi) bekleyen kayitlari temizler.</summary>
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
