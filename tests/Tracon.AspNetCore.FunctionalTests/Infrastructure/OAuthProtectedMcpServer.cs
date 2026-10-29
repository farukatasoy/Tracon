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
/// It records every <c>Authorization</c> header that reached <c>/mcp</c> and every token
/// request, so a test can prove which token went where and whether PKCE was used.
/// </remarks>
internal sealed class OAuthProtectedMcpServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentQueue<string> _authorizationHeaders;
    private readonly ConcurrentQueue<TokenRequest> _tokenRequests;
    private int _disposed;

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

    /// <summary>Starts a server.</summary>
    /// <returns>The running server.</returns>
    public static async Task<OAuthProtectedMcpServer> StartAsync()
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
        app.MapPost("/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            tokenRequests.Enqueue(new TokenRequest(
                form["grant_type"].ToString(),
                form["code"].ToString(),
                !string.IsNullOrEmpty(form["code_verifier"].ToString())));

            return Results.Json(new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["access_token"] = accessToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = 3600,
                ["refresh_token"] = $"refresh-{Guid.NewGuid():N}",
            });
        });

        app.MapMcp("/mcp");
        await app.StartAsync();

        return new OAuthProtectedMcpServer(app, new Uri($"{Base()}/mcp"), accessToken, authorizationHeaders, tokenRequests);
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
