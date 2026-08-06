using System.Text.Json;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// OpenAPI belgesindeki uc listesini dolasmak icin ortak yardimcilar (Faz 40).
/// </summary>
internal static class OpenApiTestHelpers
{
    private static readonly string[] HttpMethodNames =
        ["get", "post", "put", "delete", "patch", "head", "options", "trace"];

    /// <summary>Belgedeki her (yol, metot, operasyon) uclusunu dondurur.</summary>
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

    /// <summary>Belgedeki bir operasyonu <c>operationId</c> ile bulur.</summary>
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

        throw new InvalidOperationException($"'{operationId}' operationId'sine sahip bir uc bulunamadi.");
    }
}
