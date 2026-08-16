using System.Text.Json;
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

    /// <summary>
    /// Every operation carries a summary AND a description, and the schemas keep the
    /// property documentation that comes from the XML documentation file.
    /// </summary>
    /// <remarks>
    /// 🚨 This test exists because <c>OpenApiSnapshotTests</c> cannot catch this class
    /// of loss: it only proves the committed file matches what the host produces, so
    /// refreshing the snapshot after a regression hides the regression.
    /// <para>
    /// Measured in Phase 59: registering the document transformer as
    /// <c>AddOpenApi(Configure)</c> instead of a bare <c>AddOpenApi()</c> plus a named
    /// <c>Configure&lt;OpenApiOptions&gt;</c> silently dropped the <c>description</c>
    /// of every schema property — 166 of 226 schemas — because .NET 10's XML
    /// documentation support is an interceptor that only matches the bare call.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Document_carries_operation_descriptions_and_schema_documentation()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        var document = await AgentPrismTestHost.ReadJsonAsync(response);

        var operations = document.GetProperty("paths").EnumerateObject()
            .SelectMany(static path => path.Value.EnumerateObject())
            .Where(static method => HttpMethods.Contains(method.Name))
            .Select(static method => method.Value)
            .ToArray();

        operations.Length.ShouldBeGreaterThan(100);

        operations
            .Where(static operation => !operation.TryGetProperty("description", out _))
            .ShouldBeEmpty("Every AgentPrism endpoint must carry a description; the documentation site publishes it.");

        var documentedProperties = document.GetProperty("components").GetProperty("schemas")
            .EnumerateObject()
            .SelectMany(static schema =>
                schema.Value.TryGetProperty("properties", out var properties)
                    ? properties.EnumerateObject()
                    : Enumerable.Empty<JsonProperty>())
            .Count(static property => property.Value.TryGetProperty("description", out _));

        // The XML documentation transformer contributes these. A wiring change that
        // switches it off takes the count to zero rather than shaving it.
        documentedProperties.ShouldBeGreaterThan(
            500,
            "Schema property documentation comes from the XML documentation file; a near-zero " +
            "count means the XML transformer is no longer registered.");
    }

    private static readonly string[] HttpMethods = ["get", "post", "put", "patch", "delete"];
}
