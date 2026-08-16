using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that the <c>operationId</c> values in the document are unique (Phase 40).
/// </summary>
/// <remarks>
/// A client generator converts each <c>operationId</c> into a method name; if
/// two endpoints carry the same id, one of the generator's methods silently
/// overwrites the other.
/// </remarks>
public sealed class OpenApiOperationIdTests
{
    [Fact]
    public async Task All_operationId_values_are_unique()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        var document = await AgentPrismTestHost.ReadJsonAsync(response);

        var ids = new List<string>();

        foreach (var (path, method, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            operation.TryGetProperty("operationId", out var idElement).ShouldBeTrue(
                $"{method.ToUpperInvariant()} {path} has no operationId.");

            ids.Add(idElement.GetString()!);
        }

        var duplicates = ids
            .GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToList();

        duplicates.ShouldBeEmpty(
            customMessage: $"Duplicate operationId values: {string.Join(", ", duplicates)}");
    }
}
