using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The calls an administrator's browser makes through an MCP server's OAuth flow, against a
/// <see cref="TraconTestHost"/>.
/// </summary>
internal static class OAuthFlowClient
{
    /// <summary>The name the server is saved under.</summary>
    public const string ServerName = "secured";

    /// <summary>The client id the server is saved with.</summary>
    public const string ClientId = "tracon-client";

    /// <summary>Saves the OAuth-protected server.</summary>
    /// <param name="host">The host that receives the call.</param>
    /// <param name="endpoint">The server's MCP endpoint.</param>
    /// <param name="tenant">The tenant header to send; <see langword="null"/> for the default tenant.</param>
    /// <returns>A task that completes when the server is saved.</returns>
    public static async Task SaveServerAsync(TraconTestHost host, Uri endpoint, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri($"/tracon/api/mcp-servers/{ServerName}", UriKind.Relative))
        {
            Content = JsonContent.Create(new
            {
                endpoint = endpoint.ToString(),
                enabled = true,
                oauthEnabled = true,
                oauthClientId = ClientId,
                requiresApproval = false,
            }),
        };

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var saved = await host.Client.SendAsync(request);

        saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    /// <summary>Starts the flow and returns the state the provider will send back.</summary>
    /// <param name="host">The host that starts the flow, which then owns it.</param>
    /// <param name="tenant">The tenant header to send; <see langword="null"/> for the default tenant.</param>
    /// <returns>The <c>state</c> of the authorization address.</returns>
    public static async Task<string> StartFlowAsync(TraconTestHost host, string? tenant = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/mcp-servers/{ServerName}/oauth/start", UriKind.Relative));

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var started = await host.Client.SendAsync(request);

        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());

        using var body = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var authorizationUri = new Uri(body.RootElement.GetProperty("authorizationUri").GetString()!);

        // The provider redirects back with the state it received in the authorization address.
        return QueryValue(authorizationUri, "state").ShouldNotBeNull();
    }

    /// <summary>Plays the provider's redirect: the browser opens the callback address.</summary>
    /// <param name="host">The host the redirect reaches.</param>
    /// <param name="state">The state the provider echoes.</param>
    /// <param name="code">The authorization code.</param>
    /// <returns>The page the browser shows.</returns>
    public static async Task<string> CallbackAsync(TraconTestHost host, string state, string code = "the-code")
    {
        using var callback = await host.Client.GetAsync(new Uri(
            $"/tracon/api/mcp-servers/{ServerName}/oauth/callback?code={Uri.EscapeDataString(code)}&state={Uri.EscapeDataString(state)}",
            UriKind.Relative));

        callback.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await callback.Content.ReadAsStringAsync();
    }

    /// <summary>Runs the whole flow on one host: start, then the redirect to the same host.</summary>
    /// <param name="host">The host that starts the flow and receives the redirect.</param>
    /// <returns>The page the browser shows.</returns>
    public static async Task<string> AuthorizeAsync(TraconTestHost host)
        => await CallbackAsync(host, await StartFlowAsync(host));

    /// <summary>Asks a host to refresh its MCP tool catalog.</summary>
    /// <param name="host">The host.</param>
    /// <returns>A task that completes when the refresh is done.</returns>
    public static async Task RefreshAsync(TraconTestHost host)
    {
        using var refreshed = await host.Client.PostAsync(new Uri("/tracon/api/mcp-servers/refresh", UriKind.Relative), content: null);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await refreshed.Content.ReadAsStringAsync());
    }

    /// <summary>Lists the names of the tools a host offers.</summary>
    /// <param name="host">The host.</param>
    /// <param name="tenant">The tenant header to send; <see langword="null"/> for the default tenant.</param>
    /// <returns>The tool names.</returns>
    public static async Task<IReadOnlyList<string>> ToolNamesAsync(TraconTestHost host, string? tenant = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/tracon/api/tools", UriKind.Relative));

        if (tenant is not null)
        {
            request.Headers.Add("X-Tracon-Tenant", tenant);
        }

        using var response = await host.Client.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return [.. body.RootElement.EnumerateArray().Select(static tool => tool.GetProperty("name").GetString() ?? string.Empty)];
    }

    /// <summary>Says whether a tool list holds the OAuth-protected server's tool.</summary>
    /// <param name="names">The tool names.</param>
    /// <returns><see langword="true"/> when the <c>lookup</c> tool is there.</returns>
    public static bool HasLookup(IReadOnlyList<string> names)
        => names.Any(static name => name.Contains("lookup", StringComparison.Ordinal));

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
