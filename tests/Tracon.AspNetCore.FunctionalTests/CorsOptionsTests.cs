using System.Net;
using Microsoft.AspNetCore.Builder;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Tests for cross-origin access to the Tracon endpoints
/// (<see cref="TraconEndpointOptions.AllowedOrigins"/>, Phase 61).
/// </summary>
/// <remarks>
/// CORS is a BROWSER-enforced restriction: the server still answers the
/// request either way, but only a response carrying
/// <c>Access-Control-Allow-Origin</c> is readable by the calling page's
/// script. These tests check for the header's presence, which is the
/// server-side half of that contract.
/// </remarks>
public sealed class CorsOptionsTests
{
    private const string AllowedOrigin = "https://shop.example.com";
    private const string DisallowedOrigin = "https://evil.example.com";

    private static readonly Uri Meta = new("/tracon/api/meta", UriKind.Relative);

    [Fact]
    public async Task AllowedOrigins_empty_by_default_no_cors_header_is_sent()
    {
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, Meta);
        request.Headers.Add("Origin", AllowedOrigin);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Request_from_an_allowed_origin_gets_the_header()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.AllowedOrigins.Add(AllowedOrigin));

        using var request = new HttpRequestMessage(HttpMethod.Get, Meta);
        request.Headers.Add("Origin", AllowedOrigin);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain(AllowedOrigin, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Request_from_a_disallowed_origin_does_not_get_the_header()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.AllowedOrigins.Add(AllowedOrigin));

        using var request = new HttpRequestMessage(HttpMethod.Get, Meta);
        request.Headers.Add("Origin", DisallowedOrigin);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task Preflight_request_from_an_allowed_origin_succeeds()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.AllowedOrigins.Add(AllowedOrigin));

        using var request = new HttpRequestMessage(HttpMethod.Options, Meta);
        request.Headers.Add("Origin", AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Access-Control-Allow-Origin").ShouldContain(AllowedOrigin, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Cors_does_not_affect_a_request_outside_the_Tracon_prefix()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureEndpoints: static options => options.AllowedOrigins.Add(AllowedOrigin),
            configureApp: static app => app.MapGet("/outside", () => "ok"));

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/outside", UriKind.Relative));
        request.Headers.Add("Origin", AllowedOrigin);

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }
}
