using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// What a consumer sees when one Tracon package moves to a new release and its
/// siblings stay behind. The internal test suite references the family through
/// <c>ProjectReference</c> and cannot produce this graph at all.
/// </summary>
/// <remarks>
/// <para>
/// Four shapes, one per way the shared node can enter the graph. Two leaves on
/// different releases fail restore (NU1107); a direct reference to the shared
/// node overrides the exact range and restore only warns (NU1608), so the host
/// itself refuses to start; the <c>dotnet new</c> project turns that warning
/// into an error; and a sibling published before the exact range existed
/// (nuget.org <c>1.0.0-preview.2</c>) is caught by the host check alone.
/// </para>
/// <para>
/// A member of <see cref="RepositoryTreeGate"/> so it shares that collection's
/// single <see cref="ReleaseArtifactFixture"/> pack instead of paying for a
/// second one.
/// </para>
/// </remarks>
[Collection(RepositoryTreeGate.Name)]
public sealed class MixedVersionGraphTests(
    TemplateFixture template,
    ReleaseArtifactFixture releaseArtifacts,
    IsolatedPackageCache cache) : IClassFixture<IsolatedPackageCache>
{
    /// <summary>The newest release on nuget.org that shipped with open sibling ranges.</summary>
    private const string PublishedOpenRangeVersion = "1.0.0-preview.2";

    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(3);

    /// <summary>
    /// The older release: the stamp the collection's <see cref="ReleaseArtifactFixture"/>
    /// packed into the local feed.
    /// </summary>
    private string Older
    {
        get
        {
            var probe = Path.Combine(RepoPaths.PackageReleaseDirectory, $"Tracon.Core.{ReleaseArtifactFixture.Version}.nupkg");
            File.Exists(probe).ShouldBeTrue($"The release pack did not write '{probe}':{Environment.NewLine}{releaseArtifacts.PackOutput}");

            return ReleaseArtifactFixture.Version;
        }
    }

    private string Newer => template.Version;

    [Fact]
    public async Task Two_leaves_on_different_releases_fail_restore_with_NU1107()
    {
        using var dir = new TempDirectory();
        await MixedVersionConsumerProject.WriteNuGetConfigAsync(dir.Path);
        await MixedVersionConsumerProject.WriteClassLibraryAsync(
            dir.Path,
            ("Tracon.AspNetCore", Older),
            ("Tracon.Voice", Newer));

        var restore = await RestoreAsync(dir.Path);

        restore.ExitCode.ShouldNotBe(0, restore.Combined);
        restore.Combined.ShouldContain("NU1107", customMessage: restore.Combined);
        restore.Combined.ShouldContain("Tracon.Core", customMessage: restore.Combined);
    }

    [Fact]
    public async Task A_direct_reference_that_overrides_the_range_warns_NU1608_and_the_host_refuses_to_start()
    {
        using var dir = new TempDirectory();
        await MixedVersionConsumerProject.WriteNuGetConfigAsync(dir.Path);
        await MixedVersionConsumerProject.WriteWebHostAsync(
            dir.Path,
            ("Tracon.AspNetCore", Older),
            ("Tracon.Core", Newer));

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, RestoreTimeout, cache.Environment);

        build.ExitCode.ShouldBe(0, build.Combined);
        build.Combined.ShouldContain("NU1608", customMessage: build.Combined);

        var run = await ProcessRunner.RunAsync("dotnet", "run -c Release --no-build", dir.Path, RunTimeout, cache.Environment);

        run.ExitCode.ShouldNotBe(0, run.Combined);
        run.Combined.ShouldNotContain(MixedVersionConsumerProject.StartedLine, customMessage: run.Combined);
        ShouldListAssembly(run.Combined, "Tracon.AspNetCore", Older);
        ShouldListAssembly(run.Combined, "Tracon.Core", Newer);
    }

    [Fact]
    public async Task The_template_project_turns_NU1608_into_a_restore_error()
    {
        using var dir = new TempDirectory();

        var generated = await template.NewAsync("Mix", dir.Path, $"--TraconVersion {Older} --persistence memory --ui false");
        generated.ExitCode.ShouldBe(0, generated.Combined);

        // NewAsync writes a NuGet.config without a source mapping; this shape
        // resolves the local preview.1 and needs the mapping (see the type remarks).
        await MixedVersionConsumerProject.WriteNuGetConfigAsync(dir.Path);
        var project = Path.Combine(dir.Path, "Mix.csproj");
        var text = await File.ReadAllTextAsync(project);
        await File.WriteAllTextAsync(
            project,
            text.Replace(
                "</Project>",
                $"""
                  <ItemGroup>
                    <PackageReference Include="Tracon.Core" Version="{Newer}" />
                  </ItemGroup>

                </Project>
                """,
                StringComparison.Ordinal));

        var restore = await RestoreAsync(dir.Path);

        restore.ExitCode.ShouldNotBe(0, restore.Combined);
        restore.Combined.ShouldContain("error NU1608", customMessage: restore.Combined);
    }

    [Fact]
    public async Task A_published_open_range_sibling_is_refused_by_the_host()
    {
        using var dir = new TempDirectory();
        await MixedVersionConsumerProject.WriteNuGetConfigAsync(dir.Path, "Tracon.AspNetCore");
        await MixedVersionConsumerProject.WriteWebHostAsync(
            dir.Path,
            ("Tracon.AspNetCore", PublishedOpenRangeVersion),
            ("Tracon.Core", Newer));

        var build = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, RestoreTimeout, cache.Environment);
        build.ExitCode.ShouldBe(0, build.Combined);

        var run = await ProcessRunner.RunAsync("dotnet", "run -c Release --no-build", dir.Path, RunTimeout, cache.Environment);

        run.ExitCode.ShouldNotBe(0, run.Combined);
        run.Combined.ShouldNotContain(MixedVersionConsumerProject.StartedLine, customMessage: run.Combined);
        ShouldListAssembly(run.Combined, "Tracon.AspNetCore", PublishedOpenRangeVersion);
        ShouldListAssembly(run.Combined, "Tracon.Core", Newer);
    }

    /// <summary>
    /// The refusal names one assembly and its version per line. The line is
    /// matched whole: <c>1.0.0-preview.2</c> is a prefix of every MinVer height
    /// built after it (<c>1.0.0-preview.2.43</c>).
    /// </summary>
    private static void ShouldListAssembly(string output, string name, string version)
    {
        var line = new Regex(
            $@"^\s+{Regex.Escape(name)} {Regex.Escape(version)}\r?$",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(1));

        line.IsMatch(output).ShouldBeTrue($"Expected a '{name} {version}' line in:{Environment.NewLine}{output}");
    }

    private Task<ProcessResult> RestoreAsync(string directory)
        => ProcessRunner.RunAsync("dotnet", "restore", directory, RestoreTimeout, cache.Environment);
}
