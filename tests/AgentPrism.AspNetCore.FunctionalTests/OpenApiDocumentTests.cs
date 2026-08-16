using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that AgentPrism's endpoints appear in the OpenAPI document.
/// </summary>
/// <remarks>
/// <c>AgentPrism.AspNetCore</c> deliberately does <strong>not depend</strong> on
/// the <c>Microsoft.AspNetCore.OpenApi</c> package; a library must not force
/// OpenAPI generation onto a consumer's dependency graph. Instead, the
/// endpoints carry metadata from the shared framework (<c>WithName</c>,
/// <c>WithTags</c>, <c>WithSummary</c>, <c>WithDescription</c>); when the
/// consumer calls <c>AddOpenApi()</c> in their own application, the document
/// is produced automatically. This test sets up exactly that scenario.
/// </remarks>
public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task Document_is_generated_and_contains_AgentPrisms_endpoints()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var paths = (await AgentPrismTestHost.ReadJsonAsync(response)).GetProperty("paths");

        paths.TryGetProperty("/agentprism/api/meta", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/api/agents", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/api/agents/{name}", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/api/runs/{runId}/events", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/v1/responses", out _).ShouldBeTrue();
        paths.TryGetProperty("/agentprism/v1/chat/completions", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Document_carries_summary_and_tag_metadata()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        var meta = (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("paths")
            .GetProperty("/agentprism/api/meta")
            .GetProperty("get");

        meta.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
        meta.GetProperty("tags")[0].GetString().ShouldBe("AgentPrism");
        meta.GetProperty("operationId").GetString().ShouldBe("AgentPrismMeta");
    }

    [Fact]
    public async Task Custom_prefix_also_applies_in_the_document()
    {
        await using var host = await AgentPrismTestHost.StartAsync(prefix: "/management", withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        (await AgentPrismTestHost.ReadJsonAsync(response))
            .GetProperty("paths")
            .TryGetProperty("/management/api/meta", out _)
            .ShouldBeTrue();
    }
}
