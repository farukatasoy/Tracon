using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The two addresses no bearer token or policy guards are mapped by default and can each be left out.
/// </summary>
/// <remarks>
/// <para>
/// A host that defines no trigger and connects no MCP server through OAuth should not carry an address that anyone on the
/// internet can call. The claim under test is "the address is not there": a <c>404</c> from the router, absent from the
/// published document, and the neighbouring management routes untouched. A refusal from the endpoint itself would still
/// be an address.
/// </para>
/// </remarks>
public sealed class UnauthenticatedAddressSelectionTests
{
    private const string TriggerAccept = "/tracon/api/triggers/default/slack";
    private const string OAuthCallback = "/tracon/api/mcp-servers/github/oauth/callback?state=x&code=y";
    private const string TriggerAcceptPath = "/api/triggers/{tenantId}/{name}";
    private const string OAuthCallbackPath = "/api/mcp-servers/{name}/oauth/callback";

    [Fact]
    public async Task Both_addresses_are_mapped_by_default()
    {
        await using var host = await TraconTestHost.StartAsync(withOpenApi: true);

        // The endpoint itself answers: a trigger nobody defined is a 404 body of its own, an unsigned call is a refusal, and
        // the callback answers its own page. What matters here is that the route exists.
        using var accept = await PostAsync(host, TriggerAccept);
        using var callback = await host.Client.GetAsync(new Uri(OAuthCallback, UriKind.Relative), TestContext.Current.CancellationToken);

        (await IsRouterNotFoundAsync(accept)).ShouldBeFalse(await accept.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        (await IsRouterNotFoundAsync(callback)).ShouldBeFalse(await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var paths = await PublishedPathsAsync(host);

        paths.ShouldContain(TriggerAcceptPath, StringComparer.Ordinal);
        paths.ShouldContain(OAuthCallbackPath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_trigger_accept_address_is_not_there_when_the_option_is_off_and_the_management_routes_stay()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.MapInboundTriggers = false,
            withOpenApi: true);

        using var accept = await PostAsync(host, TriggerAccept);

        accept.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await accept.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty("the router answered, not the endpoint");

        using var listed = await host.Client.GetAsync(new Uri("/tracon/api/triggers", UriKind.Relative), TestContext.Current.CancellationToken);

        listed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var paths = await PublishedPathsAsync(host);

        paths.ShouldNotContain(TriggerAcceptPath, StringComparer.Ordinal);
        paths.ShouldContain("/api/triggers", StringComparer.Ordinal);
        paths.ShouldContain(OAuthCallbackPath, StringComparer.Ordinal);
    }

    [Fact]
    public async Task The_oauth_callback_address_is_not_there_when_the_option_is_off()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.MapMcpOAuthCallback = false,
            withOpenApi: true);

        using var callback = await host.Client.GetAsync(new Uri(OAuthCallback, UriKind.Relative), TestContext.Current.CancellationToken);

        callback.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty("the router answered, not the endpoint");

        var paths = await PublishedPathsAsync(host);

        paths.ShouldNotContain(OAuthCallbackPath, StringComparer.Ordinal);
        paths.ShouldContain(TriggerAcceptPath, StringComparer.Ordinal);
    }

    private static Task<HttpResponseMessage> PostAsync(TraconTestHost host, string path)
        => host.Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), new { }, TestContext.Current.CancellationToken);

    // The router's own 404 has no body; an endpoint that answers 404 writes a problem or a page.
    private static async Task<bool> IsRouterNotFoundAsync(HttpResponseMessage response)
        => response.StatusCode == HttpStatusCode.NotFound
           && (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Length == 0;

    private static async Task<List<string>> PublishedPathsAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var document = await TraconTestHost.ReadJsonAsync(response);

        return [.. document.GetProperty("paths").EnumerateObject().Select(static path => path.Name.Replace("/tracon", string.Empty, StringComparison.Ordinal))];
    }
}
