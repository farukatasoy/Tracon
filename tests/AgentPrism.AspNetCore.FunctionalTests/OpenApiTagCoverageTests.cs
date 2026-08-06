using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Belgedeki her ucun en az iki etiket tasidigini dogrular (Faz 40, bolum 40.3).
/// </summary>
/// <remarks>
/// Etiketlerin ilki her zaman <c>AgentPrism</c>'dir — bir tuketicinin kendi
/// uclariyla karisan bir belgede AgentPrism uclarini bu etiketle ayirmasi icindir.
/// Ikincisi alan etiketidir (<c>Agents</c>, <c>Runs</c>, ...) ve dosya sinirina gore
/// verilir; bir istemci ureteci bunu ayri sinif/modul olarak kullanir.
/// </remarks>
public sealed class OpenApiTagCoverageTests
{
    [Fact]
    public async Task Her_ucun_en_az_iki_etiketi_var_ve_ilki_AgentPrism()
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
            customMessage: "Eksik veya yanlis sirali etiketli uclar (beklenen: [\"AgentPrism\", <alan>]): " +
                           string.Join("; ", invalid));
    }
}
