using System.Text.Json;

namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// Measures the full transitive dependency closure a consumer gets from a
/// single <c>PackageReference</c>, using <c>dotnet list package
/// --include-transitive --format json</c> (Phase 95, item 22).
/// </summary>
internal static class DependencyProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Writes a bare classlib referencing <paramref name="packageId"/> at
    /// <paramref name="version"/>, restores it, and returns every package ID
    /// (top-level and transitive) that entered the graph.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResolveGraphAsync(string packageId, string version, string directory)
    {
        await TemplateFixture.WriteLocalNuGetConfigAsync(directory);

        await File.WriteAllTextAsync(Path.Combine(directory, "Probe.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{packageId}" Version="{version}" />
              </ItemGroup>

            </Project>
            """);

        var restoreResult = await ProcessRunner.RunAsync("dotnet", "restore", directory, Timeout);

        if (restoreResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"'dotnet restore' failed for '{packageId}':{Environment.NewLine}{restoreResult.Combined}");
        }

        var listResult = await ProcessRunner.RunAsync(
            "dotnet",
            "list package --include-transitive --format json",
            directory,
            Timeout);

        if (listResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"'dotnet list package' failed for '{packageId}':{Environment.NewLine}{listResult.Combined}");
        }

        return ParsePackageIds(listResult.StandardOutput);
    }

    /// <remarks>
    /// Schema measured directly (2026-08-24, SDK 10.0.100):
    /// <c>projects[0].frameworks[0].topLevelPackages[].id</c> and
    /// <c>...transitivePackages[].id</c>. No other shape is assumed.
    /// </remarks>
    private static IReadOnlyList<string> ParsePackageIds(string json)
    {
        using var document = JsonDocument.Parse(json);

        var framework = document.RootElement
            .GetProperty("projects")[0]
            .GetProperty("frameworks")[0];

        var ids = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var propertyName in new[] { "topLevelPackages", "transitivePackages" })
        {
            if (!framework.TryGetProperty(propertyName, out var packages))
            {
                continue;
            }

            foreach (var package in packages.EnumerateArray())
            {
                ids.Add(package.GetProperty("id").GetString()!);
            }
        }

        return [.. ids];
    }
}
