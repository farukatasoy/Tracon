using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// A real MCP server on loopback that is also its own OAuth authorization server: it
/// answers <c>/mcp</c> only with the access token it issued, publishes the protected
/// resource and authorization server metadata the MCP SDK discovers, and issues a token
/// for any authorization code. It exposes one tool, <c>lookup</c>.
/// </summary>
/// <remarks>
/// <para>
/// It records every <c>Authorization</c> header that reached <c>/mcp</c> and every token
/// request, so a test can prove which token went where and whether PKCE was used.
/// </para>
/// <para>
/// The refresh token ROTATES, as providers that harden against theft do: a refresh grant makes the
/// refresh token it used worthless and issues a new one, and a refresh token that was already used is
/// refused with <c>invalid_grant</c> (counted in <see cref="RejectedRefreshes"/>). Two instances that
/// refresh with the same token therefore show up as a rejection.
/// </para>
/// </remarks>
internal sealed class OAuthProtectedMcpServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<string> _authorizationHeaders;
    private readonly ConcurrentQueue<TokenRequest> _tokenRequests;
    private readonly Lock _refreshGate = new();
    private int _disposed;
    private int _refreshGrants;
    private int _rejectedRefreshes;
    private string _currentRefreshToken = string.Empty;

    private OAuthProtectedMcpServer(
        WebApplication app,
        Uri endpoint,
        string accessToken,
        ConcurrentQueue<string> authorizationHeaders,
        ConcurrentQueue<TokenRequest> tokenRequests)
    {
        _app = app;
        Endpoint = endpoint;
        AccessToken = accessToken;
        _authorizationHeaders = authorizationHeaders;
        _tokenRequests = tokenRequests;
    }

    /// <summary>The MCP endpoint address.</summary>
    public Uri Endpoint { get; }

    /// <summary>The access token this server issues and accepts.</summary>
    public string AccessToken { get; }

    /// <summary>Every <c>Authorization</c> header value that reached <c>/mcp</c>, empty when absent.</summary>
    public IReadOnlyCollection<string> AuthorizationHeaders => [.. _authorizationHeaders];

    /// <summary>Every request that reached the token endpoint.</summary>
    public IReadOnlyCollection<TokenRequest> TokenRequests => [.. _tokenRequests];

    /// <summary>Gets how many refresh grants the server accepted.</summary>
    public int RefreshGrants => Volatile.Read(ref _refreshGrants);

    /// <summary>Gets how many refresh grants the server refused because the refresh token had been used already.</summary>
    public int RejectedRefreshes => Volatile.Read(ref _rejectedRefreshes);

    /// <summary>Gets the refresh token the server accepts now.</summary>
    public string CurrentRefreshToken
    {
        get
        {
            lock (_refreshGate)
            {
                return _currentRefreshToken;
            }
        }
    }

    /// <summary>Starts a server.</summary>
    /// <param name="refreshDelay">How long a refresh grant takes to answer; it widens the window two refreshes can meet in.</param>
    /// <returns>The running server.</returns>
    public static async Task<OAuthProtectedMcpServer> StartAsync(TimeSpan refreshDelay = default)
    {
        var accessToken = $"access-{Guid.NewGuid():N}";
        var authorizationHeaders = new ConcurrentQueue<string>();
        var tokenRequests = new ConcurrentQueue<TokenRequest>();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        builder.Services.AddMcpServer()
            .WithHttpTransport()
            .WithTools([McpServerTool.Create(
                [Description("Looks the value up.")] () => "forty-two",
                new McpServerToolCreateOptions { Name = "lookup" })]);

        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");

        string Base() => app.Urls.First().TrimEnd('/');

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/mcp", StringComparison.Ordinal))
            {
                await next(context);

                return;
            }

            var header = context.Request.Headers.Authorization.ToString();
            authorizationHeaders.Enqueue(header);

            if (!string.Equals(header, $"Bearer {accessToken}", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.Headers.WWWAuthenticate =
                    $"Bearer resource_metadata=\"{Base()}/.well-known/oauth-protected-resource/mcp\"";

                return;
            }

            await next(context);
        });

        app.MapGet("/.well-known/oauth-protected-resource/{**rest}", () => Results.Json(ResourceMetadata(Base())));
        app.MapGet("/.well-known/oauth-protected-resource", () => Results.Json(ResourceMetadata(Base())));
        app.MapGet("/.well-known/oauth-authorization-server", () => Results.Json(ServerMetadata(Base())));
        app.MapGet("/.well-known/openid-configuration", () => Results.Json(ServerMetadata(Base())));
        OAuthProtectedMcpServer? self = null;

        app.MapPost("/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();

            tokenRequests.Enqueue(new TokenRequest(
                grantType,
                form["code"].ToString(),
                !string.IsNullOrEmpty(form["code_verifier"].ToString())));

            if (string.Equals(grantType, "refresh_token", StringComparison.Ordinal))
            {
                if (refreshDelay > TimeSpan.Zero)
                {
                    await Task.Delay(refreshDelay, context.RequestAborted); // delay: simulated
                }

                if (!self!.TryRotate(form["refresh_token"].ToString(), out var rotated))
                {
                    return Results.Json(
                        new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = "invalid_grant" },
                        statusCode: StatusCodes.Status400BadRequest);
                }

                return Results.Json(TokenResponse(accessToken, rotated));
            }

            return Results.Json(TokenResponse(accessToken, self!.Issue()));
        });

        app.MapMcp("/mcp");
        await app.StartAsync();

        self = new OAuthProtectedMcpServer(app, new Uri($"{Base()}/mcp"), accessToken, authorizationHeaders, tokenRequests);

        return self;
    }

    /// <summary>Stops the server. The second call does nothing.</summary>
    /// <returns>A task that completes when the server is stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static Dictionary<string, object> TokenResponse(string accessToken, string refreshToken)
        => new(StringComparer.Ordinal)
        {
            ["access_token"] = accessToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["refresh_token"] = refreshToken,
        };

    private string Issue()
    {
        lock (_refreshGate)
        {
            _currentRefreshToken = $"refresh-{Guid.NewGuid():N}";

            return _currentRefreshToken;
        }
    }

    private bool TryRotate(string presented, out string rotated)
    {
        lock (_refreshGate)
        {
            if (!string.Equals(presented, _currentRefreshToken, StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _rejectedRefreshes);
                rotated = string.Empty;

                return false;
            }

            _currentRefreshToken = $"refresh-{Guid.NewGuid():N}";
            rotated = _currentRefreshToken;
            Interlocked.Increment(ref _refreshGrants);

            return true;
        }
    }

    private static Dictionary<string, object> ResourceMetadata(string address)
        => new(StringComparer.Ordinal)
        {
            ["resource"] = $"{address}/mcp",
            ["authorization_servers"] = new[] { address },
        };

    private static Dictionary<string, object> ServerMetadata(string address)
        => new(StringComparer.Ordinal)
        {
            ["issuer"] = address,
            ["authorization_endpoint"] = $"{address}/authorize",
            ["token_endpoint"] = $"{address}/token",
            ["response_types_supported"] = new[] { "code" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
        };

    /// <summary>One request that reached the token endpoint.</summary>
    /// <param name="GrantType">The <c>grant_type</c> form value.</param>
    /// <param name="Code">The <c>code</c> form value.</param>
    /// <param name="HasCodeVerifier">Whether a PKCE <c>code_verifier</c> was sent.</param>
    internal sealed record TokenRequest(string GrantType, string Code, bool HasCodeVerifier);
}
