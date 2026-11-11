using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The content contract a <c>v*</c> tag push commits to before it is pushed.
/// Reads the <see cref="ReleaseArtifactFixture"/> packages
/// (not the shared <see cref="TemplateFixture"/> ones) because that pack forces
/// a specific version - a second, separate <c>dotnet pack</c>, owned by the
/// <see cref="RepositoryTreeGate"/> collection so it still runs only once.
/// </summary>
/// <remarks>
/// <c>scripts/gate.py release</c> runs the SAME rehearsal as a CLI command (for
/// a human before tagging, and for CI's <c>release-dryrun</c> job on every
/// push); these facts are the automated, <c>dotnet test</c>-gated half of the
/// same contract.
/// </remarks>
[Collection(RepositoryTreeGate.Name)]
public sealed class ReleaseArtifactTests(ReleaseArtifactFixture fixture)
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

    /// <summary>Only Tracon.AspNetCore may depend on a pre-release third-party package.</summary>
    [Fact]
    public void OnlyAspNetCoreDeclaresPrereleaseDependency()
    {
        var offenders = new List<string>();

        foreach (var id in PackableProjects.Ids())
        {
            if (string.Equals(id, "Tracon.AspNetCore", StringComparison.Ordinal))
            {
                continue;
            }

            var nuspec = ReadNuspec(id);
            var prereleaseThirdPartyDependencies = PrereleaseDependency.Matches(nuspec)
                .Select(match => match.Groups["id"].Value)
                .Where(dependencyId => !dependencyId.StartsWith("Tracon", StringComparison.Ordinal))
                .ToList();

            if (prereleaseThirdPartyDependencies.Count > 0)
            {
                offenders.Add($"{id}: {string.Join(", ", prereleaseThirdPartyDependencies)}");
            }
        }

        offenders.ShouldBeEmpty(
            $"Pre-release boundary violated - only Tracon.AspNetCore may declare a pre-release dependency:\n{string.Join("\n", offenders)}");
    }

    /// <summary>
    /// Every Tracon-to-Tracon dependency is an EXACT range on the package's own
    /// version, in every framework group - the nuspec half of the one-version-line
    /// rule.
    /// </summary>
    /// <remarks>
    /// <c>dotnet pack</c> writes a <c>ProjectReference</c> as a LOWER bound
    /// (<c>version="x"</c> means <c>&gt;= x</c>), so upgrading one package
    /// restores a mixed graph with zero warnings and fails at run time with
    /// <see cref="MissingMethodException"/>. The pin is applied by the
    /// <c>TraconPinSiblingDependencies</c> target in <c>src/Directory.Build.props</c>,
    /// which edits an SDK-private item; if the SDK renames that item the target
    /// becomes a silent no-op and this fact is the only thing that notices.
    /// Violations are listed per group, never de-duplicated, so a red run shows
    /// exactly which package, framework, and edge regressed.
    /// </remarks>
    [Fact]
    public void EverySiblingDependencyIsExactAndMatchesOwnVersion()
    {
        var packageIds = PackableProjects.Ids().ToHashSet(StringComparer.Ordinal);
        var violations = new List<string>();
        var siblingEdges = 0;

        foreach (var id in PackableProjects.Ids())
        {
            var metadata = XDocument.Parse(ReadNuspec(id)).Root?.Elements().SingleOrDefault(element => IsNamed(element, "metadata"))
                ?? throw new InvalidOperationException($"'{id}.nuspec' has no <metadata> element.");
            var version = metadata.Elements().Single(element => IsNamed(element, "version")).Value;

            foreach (var (group, dependency) in Dependencies(metadata))
            {
                var dependencyId = dependency.Attribute("id")?.Value ?? string.Empty;
                var range = dependency.Attribute("version")?.Value ?? string.Empty;
                var location = $"{id} · {group} · {dependencyId} · {range}";

                if (!IsTraconPackage(dependencyId))
                {
                    // A pre-release third-party range is exact on purpose; see
                    // EveryPrereleaseThirdPartyDependencyIsExact.
                    if (range.StartsWith('[') && !IsPrereleaseRange(range))
                    {
                        violations.Add($"{location}: a stable third-party dependency must keep NuGet's lower bound");
                    }

                    continue;
                }

                siblingEdges++;

                if (!packageIds.Contains(dependencyId))
                {
                    violations.Add($"{location}: not a packable project under src/");
                }

                if (!IsExactRangeOn(range, version))
                {
                    violations.Add($"{location}: expected [{version}]");
                }

                if (!string.Equals(dependency.Attribute("exclude")?.Value, "Build,Analyzers", StringComparison.Ordinal))
                {
                    violations.Add($"{location}: exclude=\"Build,Analyzers\" was lost");
                }
            }
        }

        siblingEdges.ShouldBeGreaterThan(0, "No Tracon dependency was found in any nuspec; the parser no longer matches the nuspec shape.");
        violations.ShouldBeEmpty(
            $"{violations.Count} sibling dependency group(s) are not pinned to the package's own version:\n{string.Join("\n", violations)}");
    }

    /// <summary>
    /// Every pre-release third-party dependency is an EXACT range.
    /// </summary>
    /// <remarks>
    /// A pre-release upstream breaks between its own previews. Measured
    /// (2026-09-24): the published <c>Tracon.AspNetCore</c> 1.0.0-preview.2 with
    /// MAF Hosting 1.22.0-preview restored with zero warnings and then failed at
    /// run time - <c>Microsoft.Agents.AI.Hosting.AgentSessionStore</c> moved to
    /// another assembly without a type forwarder. An open lower bound invites
    /// exactly that upgrade; an exact range makes NuGet report it at restore
    /// (NU1608 for a direct reference, NU1107 for a transitive one). A stable
    /// third-party dependency keeps its lower bound.
    /// </remarks>
    [Fact]
    public void EveryPrereleaseThirdPartyDependencyIsExact()
    {
        var violations = new List<string>();
        var prereleaseEdges = 0;

        foreach (var id in PackableProjects.Ids())
        {
            var metadata = XDocument.Parse(ReadNuspec(id)).Root?.Elements().SingleOrDefault(element => IsNamed(element, "metadata"))
                ?? throw new InvalidOperationException($"'{id}.nuspec' has no <metadata> element.");

            foreach (var (group, dependency) in Dependencies(metadata))
            {
                var dependencyId = dependency.Attribute("id")?.Value ?? string.Empty;
                var range = dependency.Attribute("version")?.Value ?? string.Empty;

                if (IsTraconPackage(dependencyId) || !IsPrereleaseRange(range))
                {
                    continue;
                }

                prereleaseEdges++;

                if (ExactRangeVersion(range) is null)
                {
                    violations.Add($"{id} · {group} · {dependencyId} · {range}: expected an exact range [x]");
                }
            }
        }

        prereleaseEdges.ShouldBeGreaterThan(
            0, "Tracon.AspNetCore declares pre-release dependencies and none was found; the parser no longer matches the nuspec shape.");
        violations.ShouldBeEmpty(
            $"{violations.Count} pre-release third-party dependency group(s) keep an open lower bound:\n{string.Join("\n", violations)}");
    }

    /// <summary>
    /// Tracon.UI ships third-party JavaScript, so the package and every assembly
    /// that embeds it carry the notices those licences require.
    /// </summary>
    /// <remarks>
    /// The bundle embeds React, React DOM, <c>scheduler</c>, TanStack Query and
    /// <c>openapi-fetch</c>. Vite drops their licence comments, so before this
    /// fact the package carried their code and none of their notices. The
    /// assembly is what a consumer deploys, so the notice travels inside it;
    /// the package-root copy is what a licence scanner reads. Both must equal
    /// the file the frontend build checks against the real module graph.
    /// </remarks>
    [Fact]
    public void UiPackageCarriesThirdPartyNotices()
    {
        const string Id = "Tracon.UI";
        const string Notice = "THIRD-PARTY-NOTICES.txt";

        var noticePath = Path.Combine(RepoPaths.Root, "src", Id, Notice);
        File.Exists(noticePath).ShouldBeTrue($"src/{Id}/{Notice} does not exist; the frontend build writes it with `node scripts/third-party-notices.mjs --write`.");

        var expected = File.ReadAllText(noticePath);
        expected.ShouldContain("react-dom", customMessage: $"src/{Id}/{Notice} names no bundled package.");

        using var package = ZipFile.OpenRead(NupkgPath(Id));
        var rootEntry = package.GetEntry(Notice);
        rootEntry.ShouldNotBeNull($"{Id} does not carry {Notice} at the package root.");
        ReadText(rootEntry).ShouldBe(expected, $"The packed {Notice} differs from src/{Id}/{Notice}.");

        var assemblies = package.Entries
            .Where(entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal)
                && entry.FullName.EndsWith($"/{Id}.dll", StringComparison.Ordinal))
            .ToList();
        assemblies.ShouldNotBeEmpty($"{Id} carries no assembly.");

        foreach (var assembly in assemblies)
        {
            EmbeddedResourceText(assembly, $"Tracon.UI.wwwroot/{Notice}").ShouldBe(
                expected, $"{assembly.FullName} does not embed the same {Notice}.");
        }
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    /// <summary>The text of one manifest resource, or <see langword="null"/> when the assembly lacks it.</summary>
    private static string? EmbeddedResourceText(ZipArchiveEntry entry, string resourceName)
    {
        using var buffer = new MemoryStream();

        using (var stream = entry.Open())
        {
            stream.CopyTo(buffer);
        }

        buffer.Position = 0;

        using var portableExecutable = new PEReader(buffer);
        var metadata = portableExecutable.GetMetadataReader();

        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);

            if (!string.Equals(metadata.GetString(resource.Name), resourceName, StringComparison.Ordinal))
            {
                continue;
            }

            var directory = portableExecutable.PEHeaders.CorHeader?.ResourcesDirectory
                ?? throw new InvalidOperationException($"{entry.FullName} has no CLI header.");
            var section = portableExecutable.GetSectionData(directory.RelativeVirtualAddress).GetReader();
            section.Offset = checked((int)resource.Offset);
            var length = section.ReadInt32();

            return System.Text.Encoding.UTF8.GetString(section.ReadBytes(length));
        }

        return null;
    }

    /// <summary>A NuGet range is pre-release when either bound carries a pre-release label.</summary>
    private static bool IsPrereleaseRange(string range) => range.Contains('-', StringComparison.Ordinal);

    /// <summary>The version of an exact range (<c>[v]</c> or <c>[v, v]</c>), or <see langword="null"/>.</summary>
    private static string? ExactRangeVersion(string range)
    {
        if (range.Length < 3 || range[0] != '[' || range[^1] != ']')
        {
            return null;
        }

        var bounds = range[1..^1].Split(',', StringSplitOptions.TrimEntries);

        return bounds.Length is 1 or 2 && bounds.All(bound => string.Equals(bound, bounds[0], StringComparison.Ordinal)) && bounds[0].Length > 0
            ? bounds[0]
            : null;
    }

    /// <summary>The nuspec namespace changes with the schema version, so elements are matched by local name.</summary>
    private static bool IsNamed(XElement element, string localName)
        => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static bool IsTraconPackage(string id)
        => string.Equals(id, "Tracon", StringComparison.Ordinal) || id.StartsWith("Tracon.", StringComparison.Ordinal);

    /// <summary><c>[v]</c> and <c>[v, v]</c> are the two spellings of an exact NuGet range.</summary>
    private static bool IsExactRangeOn(string range, string version)
        => string.Equals(ExactRangeVersion(range), version, StringComparison.Ordinal);

    private static IEnumerable<(string Group, XElement Dependency)> Dependencies(XElement metadata)
    {
        var dependencies = metadata.Elements().SingleOrDefault(element => IsNamed(element, "dependencies"));

        if (dependencies is null)
        {
            yield break;
        }

        foreach (var child in dependencies.Elements())
        {
            if (IsNamed(child, "dependency"))
            {
                yield return ("(no framework group)", child);
            }
            else if (IsNamed(child, "group"))
            {
                var framework = child.Attribute("targetFramework")?.Value ?? "(no framework group)";

                foreach (var dependency in child.Elements().Where(element => IsNamed(element, "dependency")))
                {
                    yield return (framework, dependency);
                }
            }
        }
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
        var rootLicence = File.ReadAllText(Path.Combine(RepoPaths.Root, "LICENSE.md"));

        foreach (var id in PackableProjects.Ids())
        {
            var nuspec = ReadNuspec(id);
            var entries = ZipEntryNames(NupkgPath(id));
            var profile = PackableProjects.ProfileOf(id);

            if (!nuspec.Contains("<readme>README.md</readme>", StringComparison.Ordinal) || !entries.Contains("README.md"))
            {
                failures.Add($"{id}: missing README.md");
            }

            // The licence is stated three ways and each can drift on its own: the
            // expression the nuspec declares, the acceptance prompt it must not ask
            // for, and the text that travels inside the package.
            if (!nuspec.Contains("""<license type="expression">MIT</license>""", StringComparison.Ordinal))
            {
                failures.Add($"{id}: nuspec does not declare '<license type=\"expression\">MIT</license>'");
            }

            if (!entries.Contains("LICENSE.md"))
            {
                failures.Add($"{id}: LICENSE.md is not inside the package");
            }
            else if (!string.Equals(ReadEntryText(id, "LICENSE.md"), rootLicence, StringComparison.Ordinal))
            {
                failures.Add($"{id}: the packed LICENSE.md differs from the repository's LICENSE.md");
            }

            if (entries.Contains("LICENSE-MIT.md"))
            {
                failures.Add($"{id}: packs the retired licence file LICENSE-MIT.md");
            }

            // Measured: NuGet writes requireLicenseAcceptance only when it is true.
            if (nuspec.Contains("<requireLicenseAcceptance>true</requireLicenseAcceptance>", StringComparison.Ordinal))
            {
                failures.Add($"{id}: an MIT package must not require licence acceptance");
            }

            if (!RepositoryCommit.IsMatch(nuspec))
            {
                failures.Add($"{id}: missing repository/commit metadata");
            }

            var snupkgExists = File.Exists(SnupkgPath(id));

            // Neither the template package nor the meta package compiles anything
            // (IncludeBuildOutput=false), so neither has a .pdb to ship; both set
            // IncludeSymbols=false. For Content an empty symbol package also fails
            // NU5017; for Meta it did NOT fail - the nuspec still lists dependencies -
            // so a 0-PDB symbol package shipped silently until it was measured.
            // Library and Tool carry an assembly, so their symbol package is real.
            if (profile is PackageProfile.Content or PackageProfile.Meta)
            {
                if (snupkgExists)
                {
                    failures.Add($"{id}: unexpected .snupkg for a package with no compiled output");
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

    /// <summary>
    /// A package declares exactly the frameworks its project builds - no more, no
    /// fewer - so NuGet refuses any other framework at restore (<c>NU1202</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The XML doc count above only counts; it cannot tell a <c>lib/net8.0/</c>
    /// folder left by a stale project override from the <c>lib/net10.0/</c> one it
    /// should be. Dependency groups are checked for every package that has them.
    /// </para>
    /// <para>
    /// Two layouts make NuGet treat a package as compatible with EVERY framework,
    /// and both were measured after <c>net8.0</c> and <c>net9.0</c> were dropped: a
    /// <c>.props</c>/<c>.targets</c> at the <c>build/</c> or <c>buildTransitive/</c>
    /// root (<c>Tracon.Core</c> and <c>Tracon.AspNetCore</c>), and a package with
    /// dependency groups but no <c>lib/</c> folder (the meta package). A
    /// <c>net8.0</c> project restored all three without an error, received no
    /// Tracon assembly, and failed only at compile time. The meta package
    /// therefore carries an empty <c>lib/&lt;tfm&gt;/_._</c> marker.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryPackageShipsOnlyTheFrameworksItsProjectBuilds()
    {
        var failures = new List<string>();
        var groupsSeen = 0;

        foreach (var id in PackableProjects.Ids())
        {
            var profile = PackableProjects.ProfileOf(id);
            var expected = PackableProjects.TargetFrameworksOf(id).ToHashSet(StringComparer.OrdinalIgnoreCase);

            using var package = ZipFile.OpenRead(NupkgPath(id));
            var paths = package.Entries.Select(entry => entry.FullName.Split('/')).ToList();

            foreach (var parts in paths.Where(IsFrameworkNeutralBuildAsset))
            {
                failures.Add($"{id}: '{string.Join('/', parts)}' sits at the {parts[0]}/ root, which makes the package compatible with every framework");
            }

            if (profile is PackageProfile.Library or PackageProfile.Meta)
            {
                var libFrameworks = paths
                    .Where(parts => parts.Length > 2 && string.Equals(parts[0], "lib", StringComparison.Ordinal))
                    .Select(parts => parts[1])
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (!libFrameworks.SetEquals(expected))
                {
                    failures.Add($"{id}: lib/ carries [{string.Join(", ", libFrameworks.Order(StringComparer.Ordinal))}], the project builds [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]");
                }

                var metadata = XDocument.Parse(ReadNuspec(id)).Root?.Elements().SingleOrDefault(element => IsNamed(element, "metadata"))
                    ?? throw new InvalidOperationException($"'{id}.nuspec' has no <metadata> element.");
                var groups = Dependencies(metadata).Select(entry => entry.Group).ToHashSet(StringComparer.OrdinalIgnoreCase);
                groupsSeen += groups.Count;

                foreach (var group in groups.Where(group => !expected.Contains(group)))
                {
                    failures.Add($"{id}: dependency group '{group}' is not a framework the project builds [{string.Join(", ", expected.Order(StringComparer.Ordinal))}]");
                }
            }
        }

        groupsSeen.ShouldBeGreaterThan(0, "No dependency group was found in any nuspec; the parser no longer matches the nuspec shape.");
        failures.ShouldBeEmpty(string.Join("\n", failures));
    }

    // NuGet imports only <id>.props and <id>.targets from build/ and buildTransitive/;
    // a document beside them (the capability map, the OpenAPI file) is not an asset.
    private static bool IsFrameworkNeutralBuildAsset(string[] parts)
        => parts.Length == 2
            && (string.Equals(parts[0], "build", StringComparison.Ordinal) || string.Equals(parts[0], "buildTransitive", StringComparison.Ordinal))
            && (parts[1].EndsWith(".props", StringComparison.OrdinalIgnoreCase) || parts[1].EndsWith(".targets", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// EVERY framework of Tracon.UI carries the UI, not just the one the
    /// browser tests happen to run on.
    /// </summary>
    /// <remarks>
    /// The Vite output is produced once per build, but embedding it is work each
    /// inner build does for its OWN assembly. An inner build that skipped that
    /// step still produces a perfectly loadable Tracon.UI.dll - with no UI
    /// inside it, and no other fact in this suite would notice: the E2E tests
    /// exercise a single framework. Measured (2026-09-09): the stamp that gated
    /// the collection was shared by all three inner builds, so whichever
    /// finished first could silence the other two.
    /// </remarks>
    [Fact]
    public void EveryFrameworkOfTheUiPackageEmbedsTheUi()
    {
        const string Id = "Tracon.UI";

        using var package = ZipFile.OpenRead(NupkgPath(Id));

        var assemblies = package.Entries
            .Where(entry => entry.FullName.StartsWith("lib/", StringComparison.Ordinal)
                && entry.FullName.EndsWith($"/{Id}.dll", StringComparison.Ordinal))
            .ToList();

        assemblies.Count.ShouldBe(
            PackableProjects.TargetFrameworksOf(Id).Count,
            $"Expected one {Id}.dll per target framework, found: [{string.Join(", ", assemblies.Select(entry => entry.FullName))}]");

        var withoutUi = assemblies.Where(entry => !EmbedsUi(entry)).Select(entry => entry.FullName).ToList();

        withoutUi.ShouldBeEmpty(
            $"These frameworks ship {Id}.dll without a single 'Tracon.UI.wwwroot/' resource, "
            + $"so MapTracon binds no UI on them: [{string.Join(", ", withoutUi)}]");
    }

    private static bool EmbedsUi(ZipArchiveEntry entry)
    {
        using var buffer = new MemoryStream();

        using (var stream = entry.Open())
        {
            stream.CopyTo(buffer);
        }

        buffer.Position = 0;

        using var portableExecutable = new PEReader(buffer);
        var metadata = portableExecutable.GetMetadataReader();

        return metadata.ManifestResources
            .Select(handle => metadata.GetString(metadata.GetManifestResource(handle).Name))
            .Any(name => name.StartsWith("Tracon.UI.wwwroot/", StringComparison.Ordinal));
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

    private static string ReadEntryText(string id, string entryName)
    {
        using var archive = ZipFile.OpenRead(NupkgPath(id));
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"'{entryName}' is missing from '{id}'.");
        return ReadText(entry);
    }

    private static HashSet<string> ZipEntryNames(string nupkgPath)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        return archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
    }
}
