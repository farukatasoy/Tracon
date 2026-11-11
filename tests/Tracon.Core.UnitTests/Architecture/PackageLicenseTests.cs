namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the licence. Every Tracon package is MIT: the build declares it as an
/// SPDX expression and packs the text as a plain file at the package root.
/// </summary>
/// <remarks>
/// <para>
/// The release gate reads the produced <c>.nupkg</c> back and checks the same three
/// facts on the artifact. These tests catch the source side earlier: the root
/// file, the build properties, the npm twin and every README that states a licence.
/// </para>
/// <para>
/// A project file must not override the licence. One package that declared a
/// different licence would make the claim in every README false for that package.
/// </para>
/// </remarks>
public sealed class PackageLicenseTests
{
    private const string LicenceFile = "LICENSE.md";

    [Fact]
    public void The_root_licence_is_the_MIT_text()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot, LicenceFile)).ReplaceLineEndings("\n");

        text.ShouldStartWith("MIT License\n", customMessage: "LICENSE.md no longer starts with the MIT title.");
        text.ShouldContain(
            "The above copyright notice and this permission notice shall be included in all\ncopies or substantial portions of the Software.",
            customMessage: "LICENSE.md no longer carries the MIT permission notice.");
        text.ShouldNotContain("PolyForm", Case.Insensitive, "LICENSE.md still names the earlier licence.");

        File.Exists(Path.Combine(RepositoryRoot, "LICENSE-MIT.md")).ShouldBeFalse(
            "LICENSE-MIT.md is back. There is one licence and one file; a second file reopens the question of which terms apply.");
    }

    [Fact]
    public void The_build_declares_one_licence_expression()
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot, "src", "Directory.Build.props"));

        CountOf(props, "<PackageLicenseExpression>MIT</PackageLicenseExpression>").ShouldBe(
            1, "src/Directory.Build.props must declare <PackageLicenseExpression>MIT</PackageLicenseExpression> exactly once.");
        props.ShouldNotContain("<PackageLicenseFile", customMessage: "NuGet refuses a licence file next to an expression.");
        props.ShouldNotContain(
            "<PackageRequireLicenseAcceptance>true",
            customMessage: "An MIT package must not ask the consumer to accept a licence.");
        props.ShouldContain(
            """<None Include="$(RepositoryRootPath)LICENSE.md" Pack="true" PackagePath="\" """,
            customMessage: "The MIT text must travel inside every package.");

        var overrides = PackableProjectFiles()
            .Where(path => File.ReadAllText(path).Contains("<PackageLicense", StringComparison.Ordinal)
                || File.ReadAllText(path).Contains("<PackageRequireLicenseAcceptance", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        overrides.ShouldBeEmpty("These project files override the licence that src/Directory.Build.props declares.");
    }

    [Fact]
    public void The_npm_client_ships_the_same_licence_as_its_NuGet_twin()
    {
        // @tracon/client and Tracon.Client are the same client generated from the
        // same OpenAPI document, so they carry the same terms. npm needs its own
        // copy of the text inside the package directory; this pins that copy to
        // the root file rather than letting the two drift.
        var clientDirectory = Path.Combine(RepositoryRoot, "packages", "tracon-client");
        var manifest = File.ReadAllText(Path.Combine(clientDirectory, "package.json"));

        manifest.ShouldContain(
            "\"license\": \"MIT\"",
            customMessage: "packages/tracon-client/package.json must declare 'MIT', the same terms as Tracon.Client.");
        manifest.ShouldContain(
            $"\"{LicenceFile}\"",
            customMessage: "packages/tracon-client/package.json must list LICENSE.md in 'files'.");

        File.ReadAllText(Path.Combine(clientDirectory, LicenceFile)).ShouldBe(
            File.ReadAllText(Path.Combine(RepositoryRoot, LicenceFile)),
            customMessage: "packages/tracon-client/LICENSE.md has drifted from the root LICENSE.md.");
    }

    [Fact]
    public void No_shipped_readme_claims_another_licence()
    {
        // The directory is the source, not a list: a new package README is checked
        // the day it appears.
        var readmes = PackableProjectFiles()
            .Select(path => Path.Combine(Path.GetDirectoryName(path)!, "README.md"))
            .Where(File.Exists)
            .Append(Path.Combine(RepositoryRoot, "README.md"))
            .Append(Path.Combine(RepositoryRoot, "packages", "tracon-client", "README.md"))
            .ToList();

        readmes.Count.ShouldBeGreaterThan(2, "No package README was found; the walk is broken, not the READMEs clean.");

        var stale = readmes
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("PolyForm", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("commercial licen", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToList();

        stale.ShouldBeEmpty("These shipped READMEs still describe the earlier licence terms.");
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;

        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static List<string> PackableProjectFiles()
    {
        // Same shape as the release gate: src/*/*.csproj, ONE level down. A deeper
        // walk also finds template content that `dotnet new` writes into a
        // consumer's project, which is not a package.
        var projects = Directory
            .EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly))
            .Where(path => !File.ReadAllText(path).Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal))
            .ToList();

        projects.ShouldNotBeEmpty("No packable project was found under src/.");

        return projects;
    }

    /// <summary>
    /// Finds the repository root by walking up to the Tracon.slnx file; the
    /// test build output lives under artifacts/, so a fixed relative path
    /// cannot be used. Same approach as <see cref="DependencyDirectionTests"/>.
    /// </summary>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Tracon.slnx was not found while walking up from the test output directory.");

        return directory.FullName;
    }
}
