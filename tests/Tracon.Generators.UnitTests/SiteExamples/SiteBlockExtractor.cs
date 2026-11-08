using System.Text.RegularExpressions;

namespace Tracon.Generators.UnitTests.SiteExamples;

/// <summary>
/// Reads every fenced C# block of the hand-written pages of the documentation site.
/// </summary>
/// <remarks>
/// <para>
/// The reference pages (<c>api/</c>) are generated from the XML documentation, whose <c>&lt;example&gt;</c> blocks are
/// compiled by <c>ExampleCompilationTests</c>, and the HTTP pages (<c>http-api/</c>) are generated from the OpenAPI
/// document; neither is read here. What is left is prose a person wrote, and a snippet in prose is the only code a
/// consumer copies without a compiler between the page and their editor.
/// </para>
/// <para>
/// The changelog page and the page of each release are generated from <c>CHANGELOG.md</c> by the build and are not
/// tracked, so they are not read either. A page that exists only after a build step has run would make the set of
/// blocks, and with it the list of exceptions, depend on which job runs the test and on which pages an earlier
/// build left in the working tree.
/// </para>
/// </remarks>
internal static class SiteBlockExtractor
{
    private static readonly string[] GeneratedRoots = ["api/", "http-api/", "reference/changelog.md", "reference/releases/"];

    private static readonly Regex FenceOpening = new(
        @"^(?<indent>\s*)```(?:csharp|cs)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5));

    /// <summary>Every block of every hand-written page, in page and line order.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <returns>The blocks.</returns>
    public static IReadOnlyList<SiteBlock> ExtractAll(string repositoryRoot)
    {
        var blocks = new List<SiteBlock>();

        foreach (var (file, page) in Pages(repositoryRoot))
        {
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                if (FenceOpening.Match(lines[index]) is not { Success: true } opening)
                {
                    continue;
                }

                var indent = opening.Groups["indent"].Value.Length;
                var body = new List<string>();
                var closed = false;

                for (index++; index < lines.Length; index++)
                {
                    if (lines[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        closed = true;

                        break;
                    }

                    body.Add(lines[index].Length >= indent ? lines[index][indent..] : lines[index].TrimStart());
                }

                if (!closed)
                {
                    throw new InvalidOperationException($"{page}: the C# fence that opens on line {index - body.Count} is never closed.");
                }

                blocks.Add(new SiteBlock(page, index - body.Count, string.Join('\n', body)));
            }
        }

        return blocks;
    }

    /// <summary>
    /// Independent count of the opening fences, read without the line scanner above, so a scanner that skips a block
    /// (an indented fence, a fence after a closing one that was missed) cannot make the gate smaller unseen.
    /// </summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <returns>The number of opening C# fences.</returns>
    public static int CountRawFences(string repositoryRoot)
        => Pages(repositoryRoot).Sum(page => Regex.Count(
            File.ReadAllText(page.File),
            @"^\s*```(?:csharp|cs)\b",
            RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5)));

    private static IEnumerable<(string File, string Page)> Pages(string repositoryRoot)
    {
        var root = Path.Combine(repositoryRoot, "docs-site", "src", "content", "docs");

        return Directory.EnumerateFiles(root, "*.md*", SearchOption.AllDirectories)
            .Select(file => (File: file, Page: Path.GetRelativePath(root, file).Replace('\\', '/')))
            .Where(page => !GeneratedRoots.Any(generated => page.Page.StartsWith(generated, StringComparison.Ordinal)))
            .OrderBy(page => page.Page, StringComparer.Ordinal);
    }
}
