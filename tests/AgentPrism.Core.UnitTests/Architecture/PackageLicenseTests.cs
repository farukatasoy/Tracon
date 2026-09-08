using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the licence matrix. AgentPrism ships under the PolyForm Small
/// Business License 1.0.0, except for three packages that stay MIT so that a
/// third party can write an extension, run the behaviour contract suites
/// against it, and own the code <c>dotnet new</c> generates, without holding a
/// commercial licence.
/// </summary>
/// <remarks>
/// <para>
/// The matrix is written down twice on purpose — once in
/// <c>src/Directory.Build.props</c>, which decides what a package declares and
/// packs, and once in <c>scripts/kapi.py</c>, which is the release gate that
/// reads the produced <c>.nupkg</c> back. Two independent statements catch a
/// build that silently packs the wrong file; the risk is that they drift apart,
/// so these tests read both files from disk and require them to agree.
/// </para>
/// <para>
/// A MIT package must never depend on a PolyForm one. The MIT label would be
/// worthless if it did: a consumer's licence scanner resolves the transitive
/// graph, reaches the restricted package and stops the build anyway. Reads
/// project files rather than compiled assemblies, the same way
/// <see cref="DependencyDirectionTests"/> does.
/// </para>
/// </remarks>
public sealed class PackageLicenseTests
{
    private const string PolyFormLicenseFile = "LICENSE.md";
    private const string MitLicenseFile = "LICENSE-MIT.md";

    [Fact]
    public void Both_licence_files_exist_at_the_repository_root()
    {
        // Every package packs one of these two, so a rename breaks all 20 at once.
        foreach (var file in new[] { PolyFormLicenseFile, MitLicenseFile })
        {
            File.Exists(Path.Combine(RepositoryRoot, file)).ShouldBeTrue(
                $"'{file}' is missing from the repository root. src/Directory.Build.props packs it " +
                "into every package through PackageLicenseFile.");
        }
    }

    [Fact]
    public void The_PolyForm_file_carries_the_canonical_licence_body()
    {
        // The threshold clause is what makes this licence collectable: a scanner
        // recognises the name, and legal reads this sentence. Editing the
        // canonical body would leave the package claiming a licence it no longer
        // carries, so the two load-bearing sentences are pinned here.
        var text = File.ReadAllText(Path.Combine(RepositoryRoot, PolyFormLicenseFile));

        text.ShouldContain("# PolyForm Small Business License 1.0.0");
        text.ShouldContain(
            "fewer than 100 total\nindividuals working as employees and independent contractors,\nand less than 1,000,000 USD (2019) total revenue in the prior\ntax year.".ReplaceLineEndings("\n"),
            customMessage: "The Small Business clause no longer matches the canonical PolyForm text.");
    }

    [Fact]
    public void The_npm_client_ships_the_same_licence_as_its_NuGet_twin()
    {
        // @agentprism/client and AgentPrism.Client are the same client generated
        // from the same OpenAPI document, so they carry the same terms. npm needs
        // its own copy of the text inside the package directory; this pins that
        // copy to the root file rather than letting the two drift.
        // The expected SPDX id is DERIVED from what AgentPrism.Client carries, not
        // written down again: if that package is ever moved to MIT, this test has to
        // fail rather than leave npm quietly on the restricted licence.
        var twinLicence = ReleaseGateLicences()["AgentPrism.Client"];
        var expectedSpdx = string.Equals(twinLicence, MitLicenseFile, StringComparison.Ordinal)
            ? "MIT"
            : "PolyForm-Small-Business-1.0.0";

        var manifest = File.ReadAllText(
            Path.Combine(RepositoryRoot, "packages", "agentprism-client", "package.json"));

        manifest.ShouldContain(
            $"\"license\": \"{expectedSpdx}\"",
            customMessage: $"packages/agentprism-client/package.json must declare '{expectedSpdx}', " +
                           "the same terms as its NuGet twin AgentPrism.Client.");

        var rootText = File.ReadAllText(Path.Combine(RepositoryRoot, twinLicence));
        var npmText = File.ReadAllText(
            Path.Combine(RepositoryRoot, "packages", "agentprism-client", twinLicence));

        npmText.ShouldBe(
            rootText,
            customMessage: "packages/agentprism-client/LICENSE.md has drifted from the root LICENSE.md. " +
                           "A consumer who reads the npm copy would be reading different terms.");
    }

    [Fact]
    public void Every_packable_project_declares_a_licence_in_the_release_gate()
    {
        // No default: a package that is not in the table is a package whose
        // licence nobody decided. The gate treats that as an error, and so does this.
        var packable = PackableProjectIds();
        var gate = ReleaseGateLicences();

        gate.Keys.Order(StringComparer.Ordinal).ShouldBe(
            packable.Order(StringComparer.Ordinal),
            customMessage: "scripts/kapi.py PACKAGE_LICENSES does not list exactly the packable projects. " +
                           "A new package has to state its licence there before it can ship.");
    }

    [Fact]
    public void The_build_matrix_and_the_release_gate_agree_on_which_packages_are_MIT()
    {
        var fromBuild = MitPackagesFromBuildProps().Order(StringComparer.Ordinal).ToList();
        var fromGate = ReleaseGateLicences()
            .Where(entry => string.Equals(entry.Value, MitLicenseFile, StringComparison.Ordinal))
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        fromGate.ShouldBe(
            fromBuild,
            customMessage: "AgentPrismMitLicensed in src/Directory.Build.props and PACKAGE_LICENSES in " +
                           "scripts/kapi.py disagree. The build decides what ships; the gate decides what " +
                           "passes. If they drift, one of them is lying about the product.");
    }

    [Fact]
    public void A_MIT_package_depends_on_no_PolyForm_package()
    {
        var mit = MitPackagesFromBuildProps();

        foreach (var package in mit)
        {
            foreach (var reference in AgentPrismProjectReferences(package))
            {
                mit.Contains(reference).ShouldBeTrue(
                    $"'{package}' is MIT but references '{reference}', which is not. " +
                    "A consumer's licence scanner resolves the transitive graph, so the MIT label " +
                    "on this package would buy them nothing.");
            }
        }
    }

    private static IReadOnlyList<string> PackableProjectIds()
    {
        // Mirrors _packable_ids in scripts/kapi.py exactly, including its src/*/*.csproj
        // shape: ONE level down. Measured — a deeper walk also finds
        // src/AgentPrism.Templates/.../AgentPrism.Starter.csproj, which is template
        // content that `dotnet new` writes into a consumer's project, not a package.
        return [.. Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Where(path => !File.ReadAllText(path).Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)];
    }

    private static readonly Regex GateEntryPattern = new(
        @"^\s*""(?<id>AgentPrism[^""]*)"":\s*(?<licence>POLYFORM_LICENSE_FILE|MIT_LICENSE_FILE),\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static Dictionary<string, string> ReleaseGateLicences()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot, "scripts", "kapi.py"));
        var start = script.IndexOf("PACKAGE_LICENSES: dict[str, str] = {", StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, "PACKAGE_LICENSES was not found in scripts/kapi.py.");

        var end = script.IndexOf("\n}", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, "PACKAGE_LICENSES in scripts/kapi.py is not terminated.");

        return GateEntryPattern
            .Matches(script[start..end])
            .ToDictionary(
                match => match.Groups["id"].Value,
                match => string.Equals(match.Groups["licence"].Value, "MIT_LICENSE_FILE", StringComparison.Ordinal)
                    ? MitLicenseFile
                    : PolyFormLicenseFile,
                StringComparer.Ordinal);
    }

    private static readonly Regex MitConditionPattern = new(
        @"'\$\(MSBuildProjectName\)'\s*==\s*'(?<id>AgentPrism[^']*)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static HashSet<string> MitPackagesFromBuildProps()
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Directory.Build.props"));
        var start = props.IndexOf("<AgentPrismMitLicensed", StringComparison.Ordinal);

        start.ShouldBeGreaterThanOrEqualTo(0, "AgentPrismMitLicensed was not found in src/Directory.Build.props.");

        var end = props.IndexOf("</AgentPrismMitLicensed>", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, "AgentPrismMitLicensed in src/Directory.Build.props is not terminated.");

        return [.. MitConditionPattern
            .Matches(props[start..end])
            .Select(match => match.Groups["id"].Value)];
    }

    private static IReadOnlyList<string> AgentPrismProjectReferences(string package)
    {
        var projectPath = Path.Combine(RepositoryRoot, "src", package, $"{package}.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Project file not found: {projectPath}");

        return [.. XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .Where(name => name.StartsWith("AgentPrism", StringComparison.Ordinal))];
    }

    /// <summary>
    /// Finds the repository root by walking up to the AgentPrism.slnx file; the
    /// test build output lives under artifacts/, so a fixed relative path
    /// cannot be used. Same approach as <see cref="DependencyDirectionTests"/>.
    /// </summary>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("AgentPrism.slnx was not found while walking up from the test output directory.");

        return directory.FullName;
    }
}
