namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// The package identities every release artifact must account for -
/// derived from <c>src/*/*.csproj</c>, never hand-written, so a new
/// packable project enters this list on its own.
/// </summary>
internal static class PackableProjects
{
    private static readonly System.Text.RegularExpressions.Regex TargetFrameworksElement = new(
        "<TargetFrameworks>(?<value>[^<]+)</TargetFrameworks>",
        System.Text.RegularExpressions.RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    public static IReadOnlyList<string> Ids()
        => Directory.EnumerateDirectories(Path.Combine(RepoPaths.Root, "src"))
            .Select(directory => new DirectoryInfo(directory).Name)
            .Where(name => name.StartsWith("Tracon", StringComparison.Ordinal))
            .Where(IsPackable)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    /// <summary>The metadata contract a package must satisfy, by shape.</summary>
    public static PackageProfile ProfileOf(string projectId)
    {
        var text = File.ReadAllText(CsprojPath(projectId));

        if (text.Contains("<PackAsTool>true</PackAsTool>", StringComparison.Ordinal))
        {
            return PackageProfile.Tool;
        }

        if (text.Contains("<IncludeBuildOutput>false</IncludeBuildOutput>", StringComparison.Ordinal))
        {
            return text.Contains("<PackageType>Template</PackageType>", StringComparison.Ordinal)
                ? PackageProfile.Content
                : PackageProfile.Meta;
        }

        return PackageProfile.Library;
    }

    /// <summary>
    /// The target frameworks this project compiles for. Most packages inherit
    /// <c>net8.0;net9.0;net10.0</c> from <c>src/Directory.Build.props</c>; a
    /// project that pins a single framework (e.g. <c>Tracon.Cli</c>, which is
    /// packed as a tool and so cannot cross-target) overrides
    /// <c>TargetFrameworks</c> (plural) explicitly.
    /// </summary>
    public static IReadOnlyList<string> TargetFrameworksOf(string projectId)
    {
        var text = File.ReadAllText(CsprojPath(projectId));
        var match = TargetFrameworksElement.Match(text);

        return match.Success && match.Groups["value"].Value.Trim().Length > 0
            ? match.Groups["value"].Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : ["net8.0", "net9.0", "net10.0"];
    }

    private static readonly System.Text.RegularExpressions.Regex MitLicensedProject = new(
        "'\\$\\(MSBuildProjectName\\)'\\s*==\\s*'(?<id>Tracon[^']*)'",
        System.Text.RegularExpressions.RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly Lazy<HashSet<string>> MitLicensedIds = new(() =>
    {
        // Read the matrix out of src/Directory.Build.props rather than restate it.
        // The build is what decides which licence a package declares and packs, so
        // a copy here could only ever be a second opinion about the same fact.
        var props = File.ReadAllText(Path.Combine(RepoPaths.Root, "src", "Directory.Build.props"));
        var start = props.IndexOf("<TraconMitLicensed", StringComparison.Ordinal);
        var end = start < 0 ? -1 : props.IndexOf("</TraconMitLicensed>", start, StringComparison.Ordinal);

        if (start < 0 || end < 0)
        {
            throw new InvalidOperationException(
                "TraconMitLicensed was not found in src/Directory.Build.props; the licence matrix moved.");
        }

        return [.. MitLicensedProject.Matches(props[start..end]).Select(match => match.Groups["id"].Value)];
    });

    /// <summary>
    /// The licence file this package declares and packs. Three packages are MIT so
    /// that a third party can write and test an extension, and own what
    /// <c>dotnet new</c> generates, without a commercial licence; everything that
    /// runs an agent is PolyForm Small Business 1.0.0.
    /// </summary>
    public static string LicenceFileOf(string projectId)
        => MitLicensedIds.Value.Contains(projectId) ? "LICENSE-MIT.md" : "LICENSE.md";

    private static bool IsPackable(string projectName)
    {
        var csproj = CsprojPath(projectName);
        return File.Exists(csproj) && !File.ReadAllText(csproj).Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal);
    }

    private static string CsprojPath(string projectId) => Path.Combine(RepoPaths.Root, "src", projectId, $"{projectId}.csproj");
}

internal enum PackageProfile
{
    /// <summary>Multi- or single-TFM assembly under <c>lib/</c>: XML docs per TFM, a <c>.snupkg</c>.</summary>
    Library,

    /// <summary><c>PackAsTool</c>: assembly under <c>tools/&lt;tfm&gt;/any/</c>, one XML doc, a <c>.snupkg</c>.</summary>
    Tool,

    /// <summary>
    /// <c>IncludeBuildOutput=false</c> with dependencies only (the meta package):
    /// no <c>lib/</c>, no XML doc, and no <c>.snupkg</c>.
    /// <para>
    /// 🚨 Measured 2026-09-15 (BL-005): a symbol package WAS produced and it carried
    /// 4 metadata entries and 0 PDB - there is no assembly to have symbols for. It
    /// did not fail like <see cref="Content"/> does (the nuspec still lists
    /// dependencies, so NU5017 never fired), so an empty symbol package shipped
    /// silently. The project now sets <c>IncludeSymbols=false</c>.
    /// </para>
    /// </summary>
    Meta,

    /// <summary><c>dotnet new</c> content package: no <c>lib/</c>, no XML doc, no <c>.snupkg</c> (an empty symbol package fails <c>NU5017</c>).</summary>
    Content,
}
