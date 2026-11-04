using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The filter that refuses an enum number no member has, tried on handlers the published surface does not have: a flags
/// enum, a route parameter, a renamed query parameter and a handler with no enum at all.
/// </summary>
/// <remarks>
/// <see cref="UndefinedEnumParameterTests"/> holds the surface to the rule; these hold the filter to its own.
/// </remarks>
public sealed class EnumQueryParameterFilterTests
{
    [Flags]
    private enum Access
    {
        None = 0,
        Read = 1,
        Write = 2,
    }

    private enum Color
    {
        Red = 1,
        Green = 2,
    }

    [Theory]
    [InlineData("/flags?access=0", HttpStatusCode.OK)]
    [InlineData("/flags?access=3", HttpStatusCode.OK)]
    [InlineData("/flags?access=Read,Write", HttpStatusCode.OK)]
    [InlineData("/flags?access=4", HttpStatusCode.BadRequest)]
    [InlineData("/flags?access=7", HttpStatusCode.BadRequest)]
    public async Task A_flags_enum_takes_any_combination_of_its_members_and_nothing_else(string route, HttpStatusCode expected)
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync(new Uri(route, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [InlineData("/colors/2", HttpStatusCode.OK)]
    [InlineData("/colors/Green", HttpStatusCode.OK)]
    [InlineData("/colors/9", HttpStatusCode.BadRequest)]
    public async Task A_route_parameter_is_held_to_the_same_rule(string route, HttpStatusCode expected)
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync(new Uri(route, UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task A_renamed_query_parameter_is_named_the_way_the_caller_wrote_it()
    {
        await using var app = await StartAsync();

        using var response = await app.GetTestClient().GetAsync(new Uri("/renamed?hue=9", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("'hue' must be one of: Red, Green.", Case.Sensitive);
    }

    [Fact]
    public async Task A_missing_nullable_enum_and_a_handler_without_an_enum_are_left_alone()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        using var missing = await client.GetAsync(new Uri("/optional", UriKind.Relative), TestContext.Current.CancellationToken);
        using var plain = await client.GetAsync(new Uri("/plain?count=99", UriKind.Relative), TestContext.Current.CancellationToken);

        missing.StatusCode.ShouldBe(HttpStatusCode.OK);
        plain.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        var group = app.MapGroup(string.Empty);

        group.AddEndpointFilterFactory(EnumQueryParameter.Filter);
        group.MapGet("/flags", ([FromQuery] Access access) => Results.Ok((int)access));
        group.MapGet("/colors/{color}", (Color color) => Results.Ok((int)color));
        group.MapGet("/renamed", ([FromQuery(Name = "hue")] Color color) => Results.Ok((int)color));
        group.MapGet("/optional", (Color? color) => Results.Ok(color is null));
        group.MapGet("/plain", (int count) => Results.Ok(count));

        await app.StartAsync();

        return app;
    }
}
