using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that every endpoint in the document carries at least two tags (Phase 40, section 40.3).
/// </summary>
/// <remarks>
/// The first tag is always <c>AgentPrism</c> — this lets a consumer whose own
/// endpoints mix into the same document separate AgentPrism endpoints by this tag.
/// The second is the domain tag (<c>Agents</c>, <c>Runs</c>, ...) and is assigned by
/// file boundary; a client generator uses it as a separate class/module.
/// </remarks>
public sealed class OpenApiTagCoverageTests
{
    [Fact]
    public async Task Every_endpoint_has_at_least_two_tags_and_the_first_is_AgentPrism()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        var document = await AgentPrismTestHost.ReadJsonAsync(response);

        var invalid = new List<string>();

        foreach (var (path, method, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            var tags = operation.TryGetProperty("tags", out var tagsElement)
                ? tagsElement.EnumerateArray().Select(static tag => tag.GetString()).ToList()
                : [];

            if (tags.Count < 2 || !string.Equals(tags[0], "AgentPrism", StringComparison.Ordinal))
            {
                invalid.Add($"{method.ToUpperInvariant()} {path} -> [{string.Join(", ", tags)}]");
            }
        }

        invalid.ShouldBeEmpty(
            customMessage: "Endpoints with missing or wrongly ordered tags (expected: [\"AgentPrism\", <domain>]): " +
                           string.Join("; ", invalid));
    }
}
