using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tracon.Generators.UnitTests.Examples;

/// <summary>
/// Reads every <c>&lt;example&gt;</c> block directly out of <c>src/**/*.cs</c>.
/// </summary>
/// <remarks>
/// The built XML documentation is not used, unlike <c>CapabilityExampleTests</c>:
/// that gate proves an entry point HAS an example, which only exists once
/// <c>dotnet build</c> ran. This gate proves an example COMPILES, which is a
/// property of the source text alone and should not depend on a build having
/// happened first.
/// </remarks>
internal static class ExampleExtractor
{
    private static readonly Regex ExampleTagPattern = new(
        @"<example>(?<body>.*?)</example>",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex CodeTagPattern = new(
        """<code(?:\s+language="(?<language>[^"]+)")?>(?<body>.*?)</code>""",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DocCommentLinePattern = new(
        @"^[ \t]*///[ \t]?(?<content>.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>Every block found under <c>src/</c>, in file-then-line order.</summary>
    public static IReadOnlyList<ExampleBlock> ExtractAll(string repositoryRoot)
    {
        var blocks = new List<ExampleBlock>();

        foreach (var file in SourceFiles(repositoryRoot))
        {
            var text = File.ReadAllText(file);

            // Doc-comment markers are stripped file-wide first: the gap between
            // "<example>" and "<code>" is a fresh "///"-prefixed line, not
            // whitespace, so a regex spanning both tags cannot see through it
            // otherwise. Line positions are kept (markers are blanked in place,
            // not removed) so the line number below still matches the file on disk.
            var stripped = DocCommentLinePattern.Replace(text, match => match.Groups["content"].Value);
            var relativePath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');

            foreach (Match example in ExampleTagPattern.Matches(stripped))
            {
                var code = CodeTagPattern.Match(example.Groups["body"].Value);

                if (!code.Success)
                {
                    continue;
                }

                var decoded = XElement.Parse($"<c>{code.Groups["body"].Value}</c>").Value.Trim('\r', '\n');
                var lineNumber = stripped[..example.Index].Count(character => character == '\n') + 1;
                var language = code.Groups["language"] is { Success: true } languageGroup ? languageGroup.Value : "csharp";

                blocks.Add(new ExampleBlock(relativePath, lineNumber, decoded, language));
            }
        }

        return blocks;
    }

    /// <summary>
    /// Independent count of <c>&lt;example&gt;</c> occurrences, read without going
    /// through the block parser above. Used to prove <see cref="ExtractAll"/> did
    /// not silently drop one: a parser that skips a malformed block would
    /// otherwise report a smaller, still non-empty count and look healthy.
    /// </summary>
    public static int CountRawExampleTags(string repositoryRoot)
    {
        var count = 0;

        foreach (var file in SourceFiles(repositoryRoot))
        {
            count += Regex.Count(File.ReadAllText(file), "<example>", RegexOptions.None, TimeSpan.FromSeconds(5));
        }

        return count;
    }

    private static IEnumerable<string> SourceFiles(string repositoryRoot)
        => Directory.EnumerateFiles(Path.Combine(repositoryRoot, "src"), "*.cs", SearchOption.AllDirectories);

    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
