using System.Net;
using System.Text;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// F-598 — <c>model.allowConcurrentToolCalls</c> moved to <c>model.toolLoop.allowConcurrentToolCalls</c>. The
/// agent endpoints refuse the old key and name the new place, because the JSON reader would otherwise drop
/// it without a word and the agent would run its tool calls one after another.
/// </summary>
public sealed class AgentDefinitionLegacyFieldTests
{
    private const string Legacy = """
        {"name":"legacy-agent","instructions":"x","model":{"provider":"legacy-model","model":"legacy-1","allowConcurrentToolCalls":true}}
        """;

    /// <param name="method">The HTTP method of the endpoint.</param>
    /// <param name="path">The path of the endpoint.</param>
    [Theory]
    [InlineData("POST", "/tracon/api/agents")]
    [InlineData("POST", "/tracon/api/agents/validate")]
    [InlineData("PUT", "/tracon/api/agents/legacy-agent")]
    public async Task The_old_key_is_refused_and_the_answer_names_the_new_place(string method, string path)
    {
        await using var host = await StartAsync();

        using var response = await SendAsync(host, method, path, Legacy);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);

        problem.GetProperty("title").GetString().ShouldBe("Removed field");
        problem.GetProperty("detail").GetString().ShouldNotBeNull()
            .ShouldContain("model.toolLoop.allowConcurrentToolCalls", Case.Sensitive);
    }

    [Fact]
    public async Task The_old_key_is_refused_whatever_its_case_and_value()
    {
        await using var host = await StartAsync();

        foreach (var json in new[]
                 {
                     Legacy.Replace("allowConcurrentToolCalls", "AllowConcurrentToolCalls", StringComparison.Ordinal),
                     Legacy.Replace("true", "false", StringComparison.Ordinal),
                     Legacy.Replace("true", "null", StringComparison.Ordinal),
                 })
        {
            using var response = await SendAsync(host, "POST", "/tracon/api/agents", json);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, json);
        }

        // Nothing was stored.
        using var stored = await host.Client.GetAsync(new Uri("/tracon/api/agents/legacy-agent", UriKind.Relative));
        stored.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_new_place_is_accepted_and_nothing_else_is_refused()
    {
        await using var host = await StartAsync();

        using var response = await SendAsync(host, "POST", "/tracon/api/agents", """
            {"name":"legacy-agent","instructions":"x","model":{"provider":"legacy-model","model":"legacy-1","toolLoop":{"allowConcurrentToolCalls":true}}}
            """);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

        // A key that merely looks like the old one, in another place, is still just an unknown field.
        using var elsewhere = await SendAsync(host, "POST", "/tracon/api/agents/validate", """
            {"name":"other-agent","instructions":"x","metadata":{"allowConcurrentToolCalls":true},"model":{"provider":"legacy-model","model":"legacy-1"}}
            """);

        elsewhere.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(builder => builder
            .AddModelProvider(new FakeModelProvider("legacy-model").WithModel(new ModelDescriptor { Name = "legacy-1" })));

    private static async Task<HttpResponseMessage> SendAsync(TraconTestHost host, string method, string path, string json)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        return await host.Client.SendAsync(request);
    }
}
