using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// An agent name made only of whitespace, written in the <c>model</c> field of an OpenAI-compatible request, selects no agent.
/// </summary>
/// <remarks>
/// The endpoints refused an empty name and let a blank one through to the catalog, whose argument guard throws for it, so
/// <c>"model": " "</c> was a 500 where <c>"model": ""</c> was a 400. The same request is a mistake either way.
/// </remarks>
public sealed class BlankAgentNameTests
{
    [Theory]
    [InlineData("/tracon/v1/chat/completions", " ")]
    [InlineData("/tracon/v1/chat/completions", "\u000c")]
    [InlineData("/tracon/v1/chat/completions", " ")]
    [InlineData("/tracon/v1/responses", " ")]
    [InlineData("/tracon/v1/responses", "\u000c")]
    [InlineData("/tracon/v1/responses", " ")]
    public async Task A_blank_model_selects_no_agent_and_is_a_400_like_an_empty_one(string route, string model)
    {
        await using var host = await TraconTestHost.StartAsync();

        object body = route.EndsWith("responses", StringComparison.Ordinal)
            ? new { model, input = "hello" }
            : new { model, messages = new[] { new { role = "user", content = "hello" } } };

        using var response = await host.Client.PostAsJsonAsync(new Uri(route, UriKind.Relative), body);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("No agent selected", Case.Sensitive);
    }

    [Theory]
    [InlineData("/tracon/v1/chat/completions")]
    [InlineData("/tracon/v1/responses")]
    public async Task A_blank_entity_id_falls_back_to_a_model_that_names_an_agent(string route)
    {
        // `metadata.entity_id` wins over `model` when it names something; a blank one names nothing, so it must not win.
        await using var host = await TraconTestHost.StartAsync(static builder => builder.AddAgent(TestData.Definition()));

        object body = route.EndsWith("responses", StringComparison.Ordinal)
            ? new { model = "kod-agent", input = "hello", metadata = new { entity_id = " " } }
            : new { model = "kod-agent", messages = new[] { new { role = "user", content = "hello" } }, metadata = new { entity_id = " " } };

        using var response = await host.Client.PostAsJsonAsync(new Uri(route, UriKind.Relative), body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }
}
