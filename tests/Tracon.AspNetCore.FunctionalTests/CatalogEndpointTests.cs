using System.Net;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The two read-only catalog endpoints the agent editor is built on:
/// <c>GET /api/tools</c> and <c>GET /api/models</c>.
/// </summary>
/// <remarks>
/// Neither had a functional test. Both carry a promise that is invisible in a
/// unit test: tools are listed WITH their JSON schema and have no write path at
/// all (a security boundary — a tool body can never be authored over HTTP), and
/// <c>/api/models</c> reads its <c>status</c> from the health cache without
/// probing the provider.
/// </remarks>
public sealed class CatalogEndpointTests
{
    private static readonly Uri Tools = new("/tracon/api/tools", UriKind.Relative);
    private static readonly Uri Models = new("/tracon/api/models", UriKind.Relative);

    [Fact]
    public async Task Registered_tools_are_listed_with_their_json_schema()
    {
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddTool(
                (Func<string, string>)(orderId => $"{orderId} is on its way."),
                name: "get_order_status",
                description: "Reports where an order is."));

        using var response = await host.Client.GetAsync(Tools);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tool = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Single(static entry => string.Equals(
                entry.GetProperty("name").GetString(), "get_order_status", StringComparison.Ordinal));

        tool.GetProperty("description").GetString().ShouldBe("Reports where an order is.");

        // The editor cannot offer an argument form without the schema, and a
        // null schema is exactly what a broken registration produces.
        var schema = tool.GetProperty("jsonSchema").GetString();
        schema.ShouldNotBeNullOrWhiteSpace();
        schema!.ShouldContain("orderId", Case.Sensitive);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task The_tool_catalog_has_no_write_path(string method)
    {
        // Tools are defined only in code. This is a security boundary, not a
        // missing feature: a write path here would let anyone who can reach the
        // management API author code the agent then runs.
        await using var host = await TraconTestHost.StartAsync();

        using var request = new HttpRequestMessage(new HttpMethod(method), Tools);
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Model_status_comes_from_the_cache_and_is_Unknown_before_any_health_check()
    {
        // The provider registered here throws on every chat call. If the catalog
        // endpoint probed the provider to fill 'status', this request would not
        // come back as a clean 200 with an Unknown status.
        await using var host = await TraconTestHost.StartAsync(
            static builder => builder.AddModelProvider(new ThrowingModelProvider()));

        using var response = await host.Client.GetAsync(Models);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var provider = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Single(static entry => string.Equals(
                entry.GetProperty("name").GetString(), "kirik", StringComparison.Ordinal));

        var status = provider.GetProperty("status");
        status.ValueKind.ShouldBe(JsonValueKind.String);
        status.GetString().ShouldBe(nameof(ModelProviderHealthStatus.Unknown));

        provider.GetProperty("models").EnumerateArray()
            .Select(static model => model.GetProperty("name").GetString())
            .ShouldContain(static name => name == "kirik-1");
    }
}
