using System.IO.Compression;
using System.Text.RegularExpressions;
using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// The content contract a <c>v*</c> tag push commits to before it is pushed
/// (Phase 97, 97.2). Scoped to its own <see cref="ReleaseArtifactFixture"/>
/// (not the shared <see cref="TemplateFixture"/>) because it forces a
/// specific version onto the pack - a second, separate <c>dotnet pack</c>
/// only these facts pay for.
/// </summary>
/// <remarks>
/// <c>scripts/kapi.py yayin</c> runs the SAME rehearsal as a CLI command (for
/// a human before tagging, and for CI's <c>release-dryrun</c> job on every
/// push); these facts are the automated, <c>dotnet test</c>-gated half of the
/// same contract.
/// </remarks>
public sealed class ReleaseArtifactTests(ReleaseArtifactFixture fixture) : IClassFixture<ReleaseArtifactFixture>
{
    private static readonly Regex PrereleaseDependency = new(
        "id=\"(?<id>[^\"]+)\" version=\"[^\"]*-[^\"]*\"",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    private static readonly Regex RepositoryCommit = new(
        "<repository[^>]+commit=\"[0-9a-f]{7,}\"[^>]*/>",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void EveryPackageCarriesRequestedVersion()
    {
        var missing = PackableProjects.Ids()
            .Where(id => !File.Exists(NupkgPath(id)))
            .ToList();

        missing.ShouldBeEmpty($"Expected every package at version {ReleaseArtifactFixture.Version}.\n{fixture.PackOutput}");
    }

    [Fact]
    public void PackageIdsMatchProjectSet()
    {
        var expected = PackableProjects.Ids().ToHashSet(StringComparer.Ordinal);
        var suffix = $".{ReleaseArtifactFixture.Version}.nupkg";
        var produced = Directory.EnumerateFiles(RepoPaths.PackageReleaseDirectory, $"*{suffix}")
            .Select(path => Path.GetFileName(path)[..^suffix.Length])
            .ToHashSet(StringComparer.Ordinal);

        var extra = produced.Except(expected, StringComparer.Ordinal).ToList();
        var missing = expected.Except(produced, StringComparer.Ordinal).ToList();

        extra.ShouldBeEmpty($"Unexpected package(s) produced: [{string.Join(", ", extra)}]");
        missing.ShouldBeEmpty($"Expected package(s) not produced: [{string.Join(", ", missing)}]");
    }

    /// <summary>K-008: only AgentPrism.AspNetCore may depend on a pre-release third-party package.</summary>
    [Fact]
    public void OnlyAspNetCoreDeclaresPrereleaseDependency()
    {
        var offenders = new List<string>();

        foreach (var id in PackableProjects.Ids())
        {
            if (string.Equals(id, "AgentPrism.AspNetCore", StringComparison.Ordinal))
            {
                continue;
            }

            var nuspec = ReadNuspec(id);
            var prereleaseThirdPartyDependencies = PrereleaseDependency.Matches(nuspec)
                .Select(match => match.Groups["id"].Value)
                .Where(dependencyId => !dependencyId.StartsWith("AgentPrism", StringComparison.Ordinal))
                .ToList();

            if (prereleaseThirdPartyDependencies.Count > 0)
            {
                offenders.Add($"{id}: {string.Join(", ", prereleaseThirdPartyDependencies)}");
            }
        }

        offenders.ShouldBeEmpty(
            $"K-008 boundary violated - only AgentPrism.AspNetCore may declare a pre-release dependency:\n{string.Join("\n", offenders)}");
    }

    [Fact]
    public void EveryPackageCarriesIcon()
    {
        var missing = PackableProjects.Ids()
            .Where(id => !ZipEntryNames(NupkgPath(id)).Contains("icon.png"))
            .ToList();

        missing.ShouldBeEmpty($"Missing 'icon.png': [{string.Join(", ", missing)}]");
    }

    [Fact]
    public void EveryPackageCarriesMetadata()
    {
        var failures = new List<string>();

        foreach (var id in PackableProjects.Ids())
        {
            var nuspec = ReadNuspec(id);
            var entries = ZipEntryNames(NupkgPath(id));
            var profile = PackableProjects.ProfileOf(id);

            if (!nuspec.Contains("<readme>README.md</readme>", StringComparison.Ordinal) || !entries.Contains("README.md"))
            {
                failures.Add($"{id}: missing README.md");
            }

            if (!nuspec.Contains("""<license type="expression">MIT</license>""", StringComparison.Ordinal))
            {
                failures.Add($"{id}: missing MIT license expression");
            }

            if (!RepositoryCommit.IsMatch(nuspec))
            {
                failures.Add($"{id}: missing repository/commit metadata");
            }

            var snupkgExists = File.Exists(SnupkgPath(id));

            // Templates ship no compiled output at all (IncludeBuildOutput=false) and
            // set IncludeSymbols=false itself - an empty symbol package fails NU5017.
            // Every other profile has either an assembly or dependencies, so its
            // symbol package is non-empty and gets produced.
            if (profile == PackageProfile.Content)
            {
                if (snupkgExists)
                {
                    failures.Add($"{id}: unexpected .snupkg for a content package");
                }
            }
            else if (!snupkgExists)
            {
                failures.Add($"{id}: missing .snupkg");
            }

            if (profile is PackageProfile.Library or PackageProfile.Tool)
            {
                var expectedXmlDocCount = profile == PackageProfile.Library ? PackableProjects.TargetFrameworksOf(id).Count : 1;
                var xmlDocCount = entries.Count(name => name.EndsWith($"/{id}.xml", StringComparison.Ordinal));

                if (xmlDocCount != expectedXmlDocCount)
                {
                    failures.Add($"{id}: expected {expectedXmlDocCount} XML doc file(s), found {xmlDocCount}");
                }
            }
        }

        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    private static string NupkgPath(string id) => Path.Combine(RepoPaths.PackageReleaseDirectory, $"{id}.{ReleaseArtifactFixture.Version}.nupkg");

    private static string SnupkgPath(string id) => Path.Combine(RepoPaths.PackageReleaseDirectory, $"{id}.{ReleaseArtifactFixture.Version}.snupkg");

    private static string ReadNuspec(string id)
    {
        using var archive = ZipFile.OpenRead(NupkgPath(id));
        var entry = archive.GetEntry($"{id}.nuspec") ?? throw new InvalidOperationException($"'{id}.nuspec' is missing from '{id}'.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static HashSet<string> ZipEntryNames(string nupkgPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        return archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
    }
}
