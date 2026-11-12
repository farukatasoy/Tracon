using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The MSBuild gate (<c>TraconValidatePackageReadme</c>, <c>Directory.Build.targets</c>) that stops a pack of a
/// packable project that has no <c>README.md</c> before NuGet's own, vaguer warning does.
/// </summary>
/// <remarks>
/// <para>
/// The probe project lives in a temporary directory outside the repository and imports the repository's targets file
/// through <c>DirectoryBuildTargetsPath</c>. A probe inside the tree would pack under the working-tree gate
/// (<see cref="PackCleanlinessGateTests"/>) and write into the repository's artifact folders, where the release tests
/// count packages. Outside a git work tree that gate does not apply, so the only gate that can stop this pack is the
/// one under test.
/// </para>
/// </remarks>
public sealed class PackReadmeGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task A_packable_project_without_a_README_stops_pack_with_TRACON0001_and_names_the_project()
    {
        using var directory = new TempDirectory();
        var project = WriteProbeProject(directory.Path);

        var result = await PackAsync(project, directory.Path);

        result.ExitCode.ShouldNotBe(0, result.Combined);
        result.Combined.ShouldContain("TRACON0001");

        // The code alone is not a usable failure: the message must name the project and the fix.
        result.Combined.ShouldContain("The package 'ReadmeGateProbe' has no README.md.");
        result.Combined.ShouldContain("Add a README.md file to the project folder.");
    }

    [Fact]
    public async Task A_packable_project_with_a_README_packs()
    {
        using var directory = new TempDirectory();
        var project = WriteProbeProject(directory.Path);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "README.md"), "# Probe\n", TestContext.Current.CancellationToken);

        var result = await PackAsync(project, directory.Path);

        result.ExitCode.ShouldBe(0, result.Combined);
        result.Combined.ShouldNotContain("TRACON0001");
    }

    private static string WriteProbeProject(string directory)
    {
        var project = Path.Combine(directory, "ReadmeGateProbe.csproj");

        File.WriteAllText(
            project,
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>true</IsPackable>
                <Description>A probe project for the README gate.</Description>
                <Authors>Probe</Authors>
              </PropertyGroup>
            </Project>
            """);

        return project;
    }

    private static Task<ProcessResult> PackAsync(string project, string directory)
    {
        var targets = Path.Combine(RepoPaths.Root, "Directory.Build.targets");
        var output = Path.Combine(directory, "out");

        return ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{project}\" -c Release -o \"{output}\" -p:DirectoryBuildTargetsPath=\"{targets}\"",
            directory,
            Timeout);
    }
}
