using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The address the console opens in a new tab to authorize an MCP server is a web address.
/// </summary>
/// <remarks>
/// The authorization server's metadata names the address, so it is the MCP server's word: a
/// server that publishes <c>javascript:</c> there would run its script in the console's origin
/// if the console opened it. The MCP SDK refuses such an endpoint while it builds the address
/// (measured on <c>ModelContextProtocol.Core</c> 2.3.0); the guard lives in a dependency, so
/// these cases pin it and fail on an upgrade that stops refusing.
/// </remarks>
public sealed class McpOAuthAuthorizationAddressTests
{
    [Fact]
    public async Task A_web_authorization_endpoint_reaches_the_console()
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync();
        await using var host = await StartAsync();

        await OAuthFlowClient.SaveServerAsync(host, server.Endpoint);

        var (status, body) = await StartFlowAsync(host);

        status.ShouldBe(HttpStatusCode.OK, body);
        SchemeOf(body).ShouldBe(Uri.UriSchemeHttp);
    }

    [Theory]
    [InlineData("javascript:alert(document.domain)//")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    public async Task An_authorization_endpoint_that_is_not_a_web_address_never_reaches_the_console(string endpoint)
    {
        await using var server = await OAuthProtectedMcpServer.StartAsync(authorizationEndpoint: endpoint);
        await using var host = await StartAsync();

        await OAuthFlowClient.SaveServerAsync(host, server.Endpoint);

        var (status, body) = await StartFlowAsync(host);

        status.ShouldNotBe(HttpStatusCode.OK, body);
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

    private static async Task<(HttpStatusCode Status, string Body)> StartFlowAsync(TraconTestHost host)
    {
        using var started = await host.Client.PostAsync(
            new Uri($"/tracon/api/mcp-servers/{OAuthFlowClient.ServerName}/oauth/start", UriKind.Relative),
            content: null);

        return (started.StatusCode, await started.Content.ReadAsStringAsync());
    }

    private static string SchemeOf(string body)
    {
        using var document = JsonDocument.Parse(body);

        return new Uri(document.RootElement.GetProperty("authorizationUri").GetString()!).Scheme;
    }
}
