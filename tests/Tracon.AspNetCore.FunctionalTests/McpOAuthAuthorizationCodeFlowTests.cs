using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The interactive OAuth flow of an MCP server, end to end: <c>/oauth/start</c>, the
/// provider's redirect to <c>/oauth/callback</c>, the token exchange, and the tools the
/// server then serves — against a real MCP server that is its own authorization server.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The MCP SDK puts its OWN <c>state</c> into the authorization address and refuses an
/// answer whose <c>state</c> differs. The provider sends that value back. The coordinator
/// used to key its pending flows by a <c>state</c> of its own that never left the server,
/// so every callback answered "not found or has expired" and no token was ever obtained
/// (kusur-giderme, 2026-10-29; the flow had never completed).
/// </para>
/// <para>
/// A cached token belongs to the resource it was issued for. The SDK sends a valid cached
/// token to whatever endpoint its transport points at (measured on
/// <c>ModelContextProtocol.Core</c> 2.3.0), so the token cache must drop a token when a
/// server's endpoint or client id changes.
/// </para>
/// </remarks>
public sealed class McpOAuthAuthorizationCodeFlowTests
{
    private const string ServerName = "secured";

    [Fact]
    public async Task The_callback_with_the_state_the_provider_echoes_completes_the_flow()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var host = await StartAsync();

        await SaveServerAsync(host, server.Endpoint);

        var page = await AuthorizeAsync(host);

        page.ShouldContain("Authorization complete", Case.Sensitive);
        var exchange = server.TokenRequests.ShouldHaveSingleItem();
        exchange.GrantType.ShouldBe("authorization_code");
        exchange.HasCodeVerifier.ShouldBeTrue();

        // The page answers once the exchange is done; the catalog refresh that makes the
        // tools visible runs right after it, so the list is awaited, not read once.
        await WaitUntil.ValueAsync(
            () => ToolNamesAsync(host),
            static names => names.Any(name => name.Contains("lookup", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_state_the_flow_never_issued_is_refused()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var host = await StartAsync();

        await SaveServerAsync(host, server.Endpoint);
        await StartFlowAsync(host);

        var page = await CallbackAsync(host, state: "not-the-state-the-flow-sent");

        page.ShouldContain("not found or has expired", Case.Sensitive);
        server.TokenRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Changing_the_endpoint_sends_no_token_to_the_new_endpoint()
    {
        await using var original = await OAuthProtectedMcpServer.StartAsync();
        await using var other = await OAuthProtectedMcpServer.StartAsync();
        await using var host = await StartAsync();

        await SaveServerAsync(host, original.Endpoint);
        (await AuthorizeAsync(host)).ShouldContain("Authorization complete", Case.Sensitive);

        await SaveServerAsync(host, other.Endpoint);
        await RefreshAsync(host);

        other.AuthorizationHeaders.ShouldNotContain(
            header => string.Equals(header, $"Bearer {original.AccessToken}", StringComparison.Ordinal));
        (await ToolNamesAsync(host)).ShouldNotContain(name => name.Contains("lookup", StringComparison.Ordinal));
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(
            static builder => builder.UseMcp(),
            configureServices: static services =>
            {
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconMcpOptions>(static options =>
                    options.OAuthCallbackBaseUri = new Uri("http://localhost:5081/tracon"));
            });

    private static async Task SaveServerAsync(TraconTestHost host, Uri endpoint)
    {
        using var saved = await host.Client.PutAsJsonAsync(
            new Uri($"/tracon/api/mcp-servers/{ServerName}", UriKind.Relative),
            new { endpoint = endpoint.ToString(), enabled = true, oauthEnabled = true, oauthClientId = "tracon-client", requiresApproval = false });

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>Starts the flow and returns the state the provider will send back.</summary>
    private static async Task<string> StartFlowAsync(TraconTestHost host)
    {
        using var started = await host.Client.PostAsync(
            new Uri($"/tracon/api/mcp-servers/{ServerName}/oauth/start", UriKind.Relative),
            content: null);

        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());

        using var body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var authorizationUri = new Uri(body.RootElement.GetProperty("authorizationUri").GetString()!);

        // The provider redirects back with the state it received in the authorization address.
        return QueryValue(authorizationUri, "state").ShouldNotBeNull();
    }

    private static async Task<string> AuthorizeAsync(TraconTestHost host)
        => await CallbackAsync(host, await StartFlowAsync(host));

    private static async Task<string> CallbackAsync(TraconTestHost host, string state)
    {
        using var callback = await host.Client.GetAsync(new Uri(
            $"/tracon/api/mcp-servers/{ServerName}/oauth/callback?code=the-code&state={Uri.EscapeDataString(state)}",
            UriKind.Relative));

        callback.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await callback.Content.ReadAsStringAsync();
    }

    private static async Task RefreshAsync(TraconTestHost host)
    {
        using var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
    }

    private static async Task<IReadOnlyList<string>> ToolNamesAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(new Uri("/tracon/api/tools", UriKind.Relative));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. body.RootElement.EnumerateArray().Select(static tool => tool.GetProperty("name").GetString() ?? string.Empty)];
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);

            if (separator > 0 && string.Equals(Uri.UnescapeDataString(pair[..separator]), name, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[(separator + 1)..]);
            }
        }

        return null;
    }
}
