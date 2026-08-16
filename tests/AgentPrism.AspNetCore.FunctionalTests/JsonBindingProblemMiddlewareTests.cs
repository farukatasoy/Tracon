using System.Net;
using System.Net.Http.Json;
using System.Text;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that when body binding hits a <c>JsonException</c>, the library falls
/// into its own <c>400</c> <c>ProblemDetails</c> contract instead of a generic
/// <c>500</c> (HATA-S2-006, HATA-S2-007).
/// </summary>
/// <remarks>
/// Three endpoints were deliberately chosen: one used to use automatic binding
/// via <c>[FromBody]</c> (<c>ApiKeyEndpoints</c>), one used implicit binding
/// WITHOUT the <c>[FromBody]</c> ATTRIBUTE (<c>GovernanceEndpoints</c> — this is
/// exactly why HATA-S2-007's grep-based estimate of "10 files using
/// <c>[FromBody]</c>" missed this endpoint), and one is a numeric field type
/// mismatch (a JSON error class outside the enum case). All three now read the
/// body by hand through <c>RequestBodyBinding.ReadAsync</c> — this works
/// INDEPENDENTLY of environment (Development/Production); <c>JsonBindingProblemMiddleware</c>
/// is only a defense layer (Development-only) for a future endpoint that forgets
/// to read the body by hand.
/// </remarks>
public sealed class JsonBindingProblemMiddlewareTests
{
    [Fact]
    public async Task Unrecognized_enum_value_in_api_key_body_returns_400()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            "/agentprism/api/api-keys",
            new { name = "invalid", scopes = new[] { "runs:all" } });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("title").GetString().ShouldBe("Invalid request body");
        (json.GetProperty("detail").GetString() ?? string.Empty).ShouldContain("ApiKeyScope");
    }

    [Fact]
    public async Task Unrecognized_enum_value_in_implicit_binding_also_returns_400()
    {
        // GovernanceEndpoints.SaveAsync takes the body WITHOUT the [FromBody]
        // ATTRIBUTE (implicit binding); this is exactly why HATA-S2-006's
        // estimate of "10 files using [FromBody]" missed this endpoint (HATA-S2-007).
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/mcp-servers/stdio-test", UriKind.Relative),
            new { endpoint = "stdio://some-command", transport = "Stdio" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("title").GetString().ShouldBe("Invalid request body");
        (json.GetProperty("detail").GetString() ?? string.Empty).ShouldContain("McpTransportMode");
    }

    [Fact]
    public async Task Text_sent_to_a_numeric_field_also_returns_400_on_the_retention_endpoint()
    {
        // The defect class is not enum-specific: any type mismatch triggers the
        // same JsonException -> BadHttpRequestException chain.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var content = new StringContent(
            """{ "maxAgeDays": "not-a-number", "archive": true }""",
            Encoding.UTF8,
            "application/json");

        using var response = await host.Client.PutAsync(
            new Uri("/agentprism/api/retention/runs", UriKind.Relative),
            content);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var json = await AgentPrismTestHost.ReadJsonAsync(response);
        json.GetProperty("title").GetString().ShouldBe("Invalid request body");
    }
}
