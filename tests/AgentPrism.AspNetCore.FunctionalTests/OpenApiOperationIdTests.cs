using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Belgedeki <c>operationId</c> degerlerinin benzersiz oldugunu dogrular (Faz 40).
/// </summary>
/// <remarks>
/// Bir istemci ureteci her <c>operationId</c>'yi bir metot adina cevirir; iki uc
/// ayni kimligi tasirsa uretecin metodlarindan biri digerini sessizce ezer.
/// </remarks>
public sealed class OpenApiOperationIdTests
{
    [Fact]
    public async Task Tum_operationId_degerleri_benzersiz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        var document = await AgentPrismTestHost.ReadJsonAsync(response);

        var ids = new List<string>();

        foreach (var (path, method, operation) in OpenApiTestHelpers.EnumerateOperations(document))
        {
            operation.TryGetProperty("operationId", out var idElement).ShouldBeTrue(
                $"{method.ToUpperInvariant()} {path} bir operationId tasimiyor.");

            ids.Add(idElement.GetString()!);
        }

        var duplicates = ids
            .GroupBy(static id => id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToList();

        duplicates.ShouldBeEmpty(
            customMessage: $"Yinelenen operationId degerleri: {string.Join(", ", duplicates)}");
    }
}
