using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The address an A2A agent card gives for its interface is one a caller outside the host can use.
/// </summary>
/// <remarks>
/// <para>
/// A caller reads the card and then calls the address in it. A relative path is only an address to a client that
/// resolves it against the card's own URL; the official JavaScript SDK builds a <see cref="Uri"/> from the value as
/// it stands and throws on a path. The card is therefore an absolute URL, built from what the request says about how it
/// arrived: the scheme and host the framework exposes, the path base the application runs under.
/// </para>
/// <para>
/// 🚨 Nothing here reads a forwarding header itself. A host that runs the forwarded headers middleware has the framework
/// rewrite the scheme and host before the endpoint sees them; a host that does not gets the address of the connection,
/// and a header a client wrote changes nothing.
/// </para>
/// </remarks>
public sealed class A2AAgentCardAddressTests
{
    private const string Agent = "kod-agent";

    // A name other than a loopback one is answered only to a caller that presents a credential; the proxy case needs one.
    private const string Token = "card-address-test-token";

    private static readonly Uri CardUri = new($"/tracon/a2a/{Agent}/.well-known/agent-card.json", UriKind.Relative);

    [Fact]
    public async Task The_card_gives_an_absolute_address_for_a_request_that_arrived_directly()
    {
        await using var host = await StartAsync();

        var address = await InterfaceAddressAsync(host.Client, CardUri);

        address.ShouldBe($"http://localhost/tracon/a2a/{Agent}");
    }

    [Fact]
    public async Task The_card_gives_the_address_a_forwarding_proxy_stands_for_when_the_host_honours_forwarded_headers()
    {
        await using var host = await StartAsync(static app => app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
        }));

        using var request = new HttpRequestMessage(HttpMethod.Get, CardUri);
        request.Headers.Add(TraconTestHost.RemoteIpHeader, "127.0.0.1");
        request.Headers.Add("X-Forwarded-Host", "agents.example.com");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var address = await InterfaceAddressAsync(host.Client, request);

        address.ShouldBe($"https://agents.example.com/tracon/a2a/{Agent}");
    }

    [Fact]
    public async Task The_card_ignores_forwarded_headers_the_host_does_not_honour()
    {
        await using var host = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, CardUri);
        request.Headers.Add("X-Forwarded-Host", "attacker.example.net");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var address = await InterfaceAddressAsync(host.Client, request);

        address.ShouldBe($"http://localhost/tracon/a2a/{Agent}");
    }

    [Fact]
    public async Task The_card_includes_the_path_base_the_application_runs_under()
    {
        await using var host = await StartAsync(static app =>
        {
            // Routing is placed after the path base on purpose: the framework would otherwise put it first.
            app.UsePathBase("/gateway");
            app.UseRouting();
        });

        var address = await InterfaceAddressAsync(host.Client, new Uri($"/gateway{CardUri}", UriKind.Relative));

        address.ShouldBe($"http://localhost/gateway/tracon/a2a/{Agent}");
    }

    [Fact]
    public async Task The_address_a_proxy_and_a_path_base_produce_together_is_the_one_the_endpoint_answers_on()
    {
        await using var host = await StartAsync(static app =>
        {
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto,
            });
            app.UsePathBase("/gateway");
            app.UseRouting();
        });

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/gateway{CardUri}", UriKind.Relative));
        request.Headers.Add(TraconTestHost.RemoteIpHeader, "127.0.0.1");
        request.Headers.Add("X-Forwarded-Host", "agents.example.com:8443");
        request.Headers.Add("X-Forwarded-Proto", "https");

        var address = await InterfaceAddressAsync(host.Client, request);

        address.ShouldBe($"https://agents.example.com:8443/gateway/tracon/a2a/{Agent}");
    }

    [Fact]
    public async Task The_card_names_every_header_its_address_depends_on_so_a_cache_keeps_the_variants_apart()
    {
        await using var host = await StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, CardUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var vary = string.Join(",", response.Headers.Vary);

        vary.ShouldContain("Host", Case.Insensitive);
        vary.ShouldContain("X-Forwarded-Host", Case.Insensitive);
        vary.ShouldContain("X-Forwarded-Proto", Case.Insensitive);
    }

    private static Task<TraconTestHost> StartAsync(Action<WebApplication>? configureApp = null)
        => TraconTestHost.StartAsync(
            configureTracon: static builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(static options => options.ExposedAgents.Add(Agent)),
            configureEndpoints: static options => options.AuthToken = Token,
            configureApp: configureApp,
            configureAfterMap: static app => app.MapTraconA2A());

    private static async Task<string> InterfaceAddressAsync(HttpClient client, Uri cardUri)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, cardUri);

        return await InterfaceAddressAsync(client, request);
    }

    private static async Task<string> InterfaceAddressAsync(HttpClient client, HttpRequestMessage request)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Token);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var card = await TraconTestHost.ReadJsonAsync(response);
        var interfaces = card.GetProperty("supportedInterfaces");

        interfaces.GetArrayLength().ShouldBe(1);

        return interfaces[0].GetProperty("url").GetString().ShouldNotBeNull();
    }
}
