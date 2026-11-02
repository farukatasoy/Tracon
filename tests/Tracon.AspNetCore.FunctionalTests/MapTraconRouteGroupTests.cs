using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>MapTracon</c> is refused on anything that is not the application: a route group has no pipeline to hold the middleware
/// the surface needs.
/// </summary>
/// <remarks>
/// On a group the endpoints answered but the request body limit, the <c>Idempotency-Key</c> buffering, the CORS answer and the
/// problem mapping were never added, and every request that carried an <c>Idempotency-Key</c> failed with <c>400</c>. The
/// call used to succeed, so the first sign was a customer's retry failing. Now it fails while the application is being
/// composed.
/// </remarks>
public sealed class MapTraconRouteGroupTests
{
    [Fact]
    public async Task MapTracon_on_a_route_group_is_refused_while_the_application_is_composed()
    {
        var exception = await Should.ThrowAsync<ArgumentException>(
            () => TraconTestHost.StartAsync(
                configureApp: static app => ((IEndpointRouteBuilder)app).MapGroup("/g").MapTracon()));

        exception.ParamName.ShouldBe("endpoints");
        exception.Message.ShouldContain("route group");
        exception.Message.ShouldContain("prefix");
    }

    [Fact]
    public async Task MapTraconMcpServer_on_a_route_group_names_the_group_and_does_not_ask_for_MapTracon_again()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => TraconTestHost.StartAsync(
                configureTracon: static builder => builder.UseMcpServer(),
                configureAfterMap: static app => ((IEndpointRouteBuilder)app).MapGroup("/g").MapTraconMcpServer()));

        exception.Message.ShouldContain("route group");
        exception.Message.ShouldNotContain("must be called before");
    }

    [Fact]
    public async Task MapTracon_on_the_application_still_works_under_a_prefix()
    {
        await using var host = await TraconTestHost.StartAsync(prefix: "/ops/tracon");

        using var reply = await host.Client.GetAsync(new Uri("/ops/tracon/api/meta", UriKind.Relative));

        reply.IsSuccessStatusCode.ShouldBeTrue();
    }
}
