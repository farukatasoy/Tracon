using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// No code in the repository finds a file from the path the compiler recorded for its own source.
/// </summary>
/// <remarks>
/// <para>
/// The repository builds with <c>ContinuousIntegrationBuild=true</c> whenever <c>CI</c> is set, and the SDK then rewrites
/// every compile-time source path to start with <c>/_</c>. A location derived from that path points at a directory that
/// does not exist on the machine that runs the code, so a test that reads a file next to its source, or a tool that
/// writes beside it, passes in every local run and fails on CI only. It happened twice: a benchmark runner that tried to
/// create a directory at the filesystem root, and four span-shape tests that could not find their golden files.
/// </para>
/// <para>
/// Files are found from the checkout root instead, walked up from the output directory of the running assembly
/// (<c>RepoRoot</c>). The text of comments is not read.
/// </para>
/// </remarks>
public sealed partial class CallerFilePathSiteTests
{
    // Built from two parts so that this file does not match its own scan.
    private const string Attribute = "Caller" + "FilePath";

    private static readonly string[] Roots = ["src", "tests", "bench", "samples"];

    /// <summary>Places that take the compile-time path as a hint and prove it before they use it.</summary>
    private static readonly Dictionary<string, string> ProvenHint = new(StringComparer.Ordinal)
    {
        ["bench/Tracon.Benchmarks/Program.cs"] =
            "hands the path to RepositoryLayout.ResolveRoot, which checks it against the checkout and falls back to a walk from the output directory",
    };

    [Fact]
    public void No_code_derives_a_file_location_from_the_compile_time_source_path()
    {
        var root = RepoRoot.Path;

        var files = Roots
            .Select(directory => Path.Combine(root, directory))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            .Where(static path => !IsBuildOutput(path))
            .ToList();

        files.ShouldNotBeEmpty("the scan found no source file at all, so it would pass on nothing");

        var sites = files
            .Where(path => Mentions(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        sites.Where(site => !ProvenHint.ContainsKey(site)).ToList().ShouldBeEmpty(
            "A compile-time source path is rewritten to /_ on a continuous integration build, so a file found through it is missing on CI " +
            "and present locally. Find the file from RepoRoot.Path (tests/Shared/Infrastructure/RepoRoot.cs), or list the place in ProvenHint " +
            "with the reason the path is checked before it is used.");

        ProvenHint.Keys.Where(site => !sites.Contains(site, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList()
            .ShouldBeEmpty("A listed place no longer uses the compile-time source path; remove it from the list.");
    }

    [Fact]
    public void The_scan_sees_the_attribute_in_code_and_ignores_it_in_comments()
    {
        Mentions($"private static void Verify(string name, [{Attribute}] string sourceFile = \"\")").ShouldBeTrue();
        Mentions($"    // [{Attribute}] would resolve to /_ on CI").ShouldBeFalse();
        Mentions($"    /// <c>[{Attribute}]</c> is a build artifact").ShouldBeFalse();
    }

    // The attribute as a word: a class or a test named after it ("...SiteTests") is not a use of it.
    [GeneratedRegex(@"\b" + Attribute + @"(?:Attribute)?\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AttributeUse();

    private static bool Mentions(string source)
        => source
            .Split('\n')
            .Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Any(static line => AttributeUse().IsMatch(line));

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
}
