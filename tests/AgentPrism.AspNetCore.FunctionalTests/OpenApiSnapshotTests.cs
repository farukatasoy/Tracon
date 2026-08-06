using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Islenmis <c>docs/openapi/agentprism.json</c> dosyasinin calisan barindiricinin
/// urettigi belgeyle ayni kaldigini dogrular (Faz 40, bolum 40.4).
/// </summary>
/// <remarks>
/// Bu desen, dokumanin koddan sapmasini derleme kapisina cevirir. Dosyayi
/// yenilemek icin:
/// <c>AGENTPRISM_OPENAPI_REFRESH=1 dotnet test tests/AgentPrism.AspNetCore.FunctionalTests
/// -c Release --filter FullyQualifiedName~OpenApiSnapshotTests</c>.
/// </remarks>
public sealed class OpenApiSnapshotTests
{
    private const string RefreshEnvVar = "AGENTPRISM_OPENAPI_REFRESH";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    [Fact]
    public async Task Belge_islenmis_anlik_goruntuyle_ayni()
    {
        var current = await GenerateAsync();

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, current);
        }

        File.Exists(SnapshotPath).ShouldBeTrue(
            $"'{SnapshotPath}' yok. Once '{RefreshEnvVar}=1' ile uretin (bkz. sinif aciklamasi).");

        var committed = await File.ReadAllTextAsync(SnapshotPath);

        current.ShouldBe(
            committed,
            customMessage: "OpenAPI belgesi 'docs/openapi/agentprism.json' ile farkli. Uc ustverisi " +
                           $"degisti; yenilemek icin: {RefreshEnvVar}=1 dotnet test " +
                           "tests/AgentPrism.AspNetCore.FunctionalTests -c Release " +
                           "--filter FullyQualifiedName~OpenApiSnapshotTests");
    }

    private static async Task<string> GenerateAsync()
    {
        await using var host = await AgentPrismTestHost.StartAsync(withOpenApi: true);

        using var response = await host.Client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        response.EnsureSuccessStatusCode();

        var document = await AgentPrismTestHost.ReadJsonAsync(response);

        return JsonSerializer.Serialize(document, WriteOptions) + Environment.NewLine;
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string SnapshotPath { get; } =
        Path.Combine(RepositoryRoot, "docs", "openapi", "agentprism.json");

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentPrism.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("AgentPrism.slnx bulunamadi.");
    }
}
