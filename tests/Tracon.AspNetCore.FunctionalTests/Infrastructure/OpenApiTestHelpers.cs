using System.Text.Json;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Shared helpers for walking the operation list in an OpenAPI document (Phase 40).
/// </summary>
internal static class OpenApiTestHelpers
{
    private static readonly string[] HttpMethodNames =
        ["get", "post", "put", "delete", "patch", "head", "options", "trace"];

    /// <summary>Returns every (path, method, operation) triple in the document.</summary>
    public static IEnumerable<(string Path, string Method, JsonElement Operation)> EnumerateOperations(
        JsonElement document)
    {
        foreach (var pathEntry in document.GetProperty("paths").EnumerateObject())
        {
            foreach (var methodEntry in pathEntry.Value.EnumerateObject())
            {
                if (Array.IndexOf(HttpMethodNames, methodEntry.Name) < 0)
                {
                    continue;
                }

                yield return (pathEntry.Name, methodEntry.Name, methodEntry.Value);
            }
        }
    }

    /// <summary>Finds an operation in the document by its <c>operationId</c>.</summary>
    public static JsonElement FindByOperationId(JsonElement document, string operationId)
    {
        foreach (var (_, _, operation) in EnumerateOperations(document))
        {
            if (operation.TryGetProperty("operationId", out var id) &&
                string.Equals(id.GetString(), operationId, StringComparison.Ordinal))
            {
                return operation;
            }
        }

        throw new InvalidOperationException($"No operation found with operationId '{operationId}'.");
    }
}
