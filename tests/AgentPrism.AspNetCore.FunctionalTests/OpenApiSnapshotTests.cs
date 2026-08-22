using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies that the committed <c>docs/openapi/agentprism.json</c> file stays
/// identical to the document produced by the running host (Phase 40, section 40.4).
/// </summary>
/// <remarks>
/// This pattern turns the document drifting from the code into a build gate.
/// To refresh the file:
/// <c>AGENTPRISM_OPENAPI_REFRESH=1 dotnet test tests/AgentPrism.AspNetCore.FunctionalTests
/// -c Release --filter FullyQualifiedName~OpenApiSnapshotTests</c>.
/// </remarks>
public sealed class OpenApiSnapshotTests
{
    private const string RefreshEnvVar = "AGENTPRISM_OPENAPI_REFRESH";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    [Fact]
    public async Task Document_matches_the_committed_snapshot()
    {
        var current = await GenerateAsync();

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnapshotPath)!);
            await File.WriteAllTextAsync(SnapshotPath, current);
        }

        File.Exists(SnapshotPath).ShouldBeTrue(
            $"'{SnapshotPath}' does not exist. Generate it first with '{RefreshEnvVar}=1' (see the class description).");

        var committed = await File.ReadAllTextAsync(SnapshotPath);

        current.ShouldBe(
            committed,
            customMessage: "The OpenAPI document differs from 'docs/openapi/agentprism.json'. Endpoint " +
                           $"metadata changed; to refresh: {RefreshEnvVar}=1 dotnet test " +
                           "tests/AgentPrism.AspNetCore.FunctionalTests -c Release " +
                           "--filter FullyQualifiedName~OpenApiSnapshotTests");
    }

#pragma warning disable MEAI001
    private static async Task<string> GenerateAsync()
    {
        // Section 84.3: the document is generated with every optional endpoint turned
        // on. A default-off endpoint (like /api/diagnostics) is still part of the
        // surface a consumer sees once they enable it, and every generated artifact
        // (this document, the .NET and TypeScript clients, the http-api/ site pages)
        // is built from this one file.
        await using var host = await AgentPrismTestHost.StartAsync(
            withOpenApi: true,
            configureEndpoints: options => options.EnableDiagnosticsEndpoint = true,
            configureServices: static services =>
            {
                services.Configure<AgentPrismImageOptions>(options =>
                {
                    options.Enabled = true;
                    options.Provider = "openapi";
                    options.Model = "image-1";
                });
                services.AddSingleton<IImageGenerator, OpenApiImageGenerator>();
            });

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
            ?? throw new InvalidOperationException("AgentPrism.slnx not found.");
    }

    private sealed class OpenApiImageGenerator : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResponse());

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
#pragma warning restore MEAI001
}
