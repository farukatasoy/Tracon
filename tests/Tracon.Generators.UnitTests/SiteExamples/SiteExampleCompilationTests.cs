using Tracon.Generators.UnitTests.Examples;

namespace Tracon.Generators.UnitTests.SiteExamples;

/// <summary>
/// Compiles the C# blocks of the hand-written documentation pages.
/// </summary>
/// <remarks>
/// <para>
/// <c>ExampleCompilationTests</c> proves that the <c>&lt;example&gt;</c> blocks in the source compile; the pages of the
/// documentation site carry more code than that, written by hand, and nothing compiled it. A snippet that does not
/// build reaches a consumer who copies it, and the first thing they learn about the library is that its guide is wrong.
/// </para>
/// <para>
/// A page is compiled as one unit (see <see cref="SitePageCompiler"/>). A block that is meant as an illustration and
/// cannot compile in isolation, because it names a type only the reader would write, is listed in
/// <c>site-example-exceptions.txt</c> with the first error. The list only shrinks: a block that fails and is not on
/// it fails the gate, and a block that is on it and now compiles fails the gate too, so the entry is removed. A block
/// is named by its page and a digest of its text, which means an edited block has to be looked at again.
/// </para>
/// <para>
/// To write the list: <c>TRACON_SITE_EXAMPLES_REFRESH=1</c> and run <see cref="Refresh"/>.
/// </para>
/// </remarks>
public sealed class SiteExampleCompilationTests
{
    private const string RefreshVariable = "TRACON_SITE_EXAMPLES_REFRESH";

    private static readonly string RepositoryRoot = ExampleExtractor.FindRepositoryRoot();

    private static readonly IReadOnlyList<SiteBlock> Blocks = SiteBlockExtractor.ExtractAll(RepositoryRoot);

    private static string ExceptionFile => Path.Combine(
        RepositoryRoot, "tests", "Tracon.Generators.UnitTests", "SiteExamples", "site-example-exceptions.txt");

    public static TheoryData<string> Pages() => [.. Blocks.Select(static block => block.Page).Distinct(StringComparer.Ordinal)];

    [Fact]
    public void Every_csharp_fence_of_a_hand_written_page_is_read_as_a_block()
    {
        var raw = SiteBlockExtractor.CountRawFences(RepositoryRoot);

        Blocks.ShouldNotBeEmpty("No C# block was found under docs-site; the search path or the reader regressed.");
        Blocks.Count.ShouldBe(raw, $"The reader produced {Blocks.Count} blocks; the pages carry {raw} opening C# fences.");
    }

    [Theory]
    [MemberData(nameof(Pages))]
    public void Every_example_of_the_page_compiles_or_is_a_listed_exception(string page)
    {
        if (RefreshRequested)
        {
            return;
        }

        var pageBlocks = Blocks.Where(block => string.Equals(block.Page, page, StringComparison.Ordinal)).ToList();
        var failing = SitePageCompiler.Compile(pageBlocks, TestContext.Current.CancellationToken);
        var listed = ReadExceptions().Where(entry => entry.Id.StartsWith(page + "#", StringComparison.Ordinal)).Select(static e => e.Id).ToHashSet(StringComparer.Ordinal);

        var unexpected = failing
            .Where(pair => !listed.Contains(pair.Key.Id))
            .Select(pair => $"{pair.Key.Id} (line {pair.Key.Line}):\n    {string.Join("\n    ", pair.Value.Take(4))}\n  Source:\n{Indent(pair.Key.Code)}")
            .ToList();

        var stale = listed
            .Where(id => !failing.Keys.Any(block => string.Equals(block.Id, id, StringComparison.Ordinal)))
            .ToList();

        unexpected.ShouldBeEmpty($"{page}: a block does not compile and is not a listed exception:\n{string.Join("\n", unexpected)}");
        stale.ShouldBeEmpty($"{page}: these blocks compile now, or are gone; remove them from site-example-exceptions.txt: {string.Join(", ", stale)}");
    }

    [Fact]
    public void The_exception_list_names_only_blocks_that_exist()
    {
        if (RefreshRequested)
        {
            return;
        }

        var ids = Blocks.Select(static block => block.Id).ToHashSet(StringComparer.Ordinal);

        ReadExceptions().Select(static entry => entry.Id).Where(id => !ids.Contains(id)).ToList()
            .ShouldBeEmpty("site-example-exceptions.txt names a block no page carries any more; remove the line.");
    }

    /// <summary>Writes <c>site-example-exceptions.txt</c> from what fails today.</summary>
    [Fact]
    public void Refresh()
    {
        if (!RefreshRequested)
        {
            return;
        }

        var lines = new List<string>
        {
            "# Hand-written documentation blocks that cannot compile on their own, one per line:",
            "#   <page>#<digest of the block> | the first error",
            "# The list only shrinks. See SiteExampleCompilationTests.",
        };

        foreach (var page in Blocks.Select(static block => block.Page).Distinct(StringComparer.Ordinal))
        {
            var pageBlocks = Blocks.Where(block => string.Equals(block.Page, page, StringComparison.Ordinal)).ToList();

            foreach (var (block, errors) in SitePageCompiler.Compile(pageBlocks, TestContext.Current.CancellationToken))
            {
                lines.Add($"{block.Id} | {errors[0].ReplaceLineEndings(" ")}");
            }
        }

        File.WriteAllLines(ExceptionFile, lines);
    }

    private static bool RefreshRequested
        => string.Equals(Environment.GetEnvironmentVariable(RefreshVariable), "1", StringComparison.Ordinal);

    private static IEnumerable<(string Id, string Reason)> ReadExceptions()
        => File.Exists(ExceptionFile)
            ? File.ReadAllLines(ExceptionFile)
                .Where(static line => line.Length > 0 && !line.StartsWith('#'))
                .Select(static line => line.Split(" | ", 2))
                .Select(static parts => (parts[0].Trim(), parts.Length > 1 ? parts[1] : string.Empty))
            : [];

    private static string Indent(string code) => string.Join('\n', code.Split('\n').Select(static line => "      " + line));
}
