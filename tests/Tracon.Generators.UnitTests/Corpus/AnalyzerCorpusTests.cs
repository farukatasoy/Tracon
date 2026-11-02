using System.Globalization;
using System.Text;
using Tracon.Generators.UnitTests.Examples;

namespace Tracon.Generators.UnitTests.Corpus;

/// <summary>
/// Runs the package's analyzer and source generator over real code: this repository's own trees, read as text.
/// </summary>
/// <remarks>
/// <para>
/// <c>UsageAnalyzerTests</c> feeds the analyzer the small programs its author imagined. Code written by someone else
/// reaches shapes nobody imagined, and an analyzer that throws on one of them is reported by the compiler as
/// <c>AD0001</c> in the CONSUMER's build, where the consumer cannot fix it and a warnings-as-errors build stops. This
/// class is the first place such a throw would show: every unit is analysed with the references this assembly has,
/// which leaves much of the code only partly bound, and that is the point.
/// </para>
/// <para>
/// Nothing in the corpus is built, restored or run (see <see cref="CorpusDiscovery"/>). To measure other source
/// trees on a machine, set <c>TRACON_ANALYZER_CORPUS_DIRS</c> (a path-separator list of directories; one unit per
/// project directory) or <c>TRACON_ANALYZER_CORPUS_PER_FILE</c> (the same, one compilation per file) and run
/// <see cref="Report"/>; <c>TRACON_ANALYZER_CORPUS_REPORT</c> names the file it writes.
/// <c>TRACON_ANALYZER_CORPUS_EXAMPLES</c> adds the package's shipped <c>&lt;example&gt;</c> blocks, and
/// <c>TRACON_ANALYZER_CORPUS_SKIP_REPOSITORY</c> leaves the repository's own trees out.
/// </para>
/// </remarks>
public sealed class AnalyzerCorpusTests
{
    private const string DirectoriesVariable = "TRACON_ANALYZER_CORPUS_DIRS";

    private const string PerFileVariable = "TRACON_ANALYZER_CORPUS_PER_FILE";

    private const string SkipRepositoryVariable = "TRACON_ANALYZER_CORPUS_SKIP_REPOSITORY";

    private const string ExamplesVariable = "TRACON_ANALYZER_CORPUS_EXAMPLES";

    private const string ReportVariable = "TRACON_ANALYZER_CORPUS_REPORT";

    private static readonly string RepositoryRoot = ExampleExtractor.FindRepositoryRoot();

    private static readonly string[] GuardedTrees = ["src", "samples"];

    private static readonly string[] AllTrees = ["src", "tests", "samples", "bench"];

    private const double PerFileCallbackCeilingMilliseconds = 40;

    private static readonly TimeSpan UnitWallCeiling = TimeSpan.FromSeconds(60);

    /// <summary>The guarded trees analysed once; the throw test and the cost test read the same run.</summary>
    private static readonly Lazy<Task<List<UnitReport>>> GuardedReports = new(
        () => AnalyzeAsync(CorpusDiscovery.FromRepository(RepositoryRoot, GuardedTrees), countSemanticErrors: false));

    [Fact]
    public void The_corpus_is_the_repository_code_and_not_an_empty_directory()
    {
        var units = CorpusDiscovery.FromRepository(RepositoryRoot, GuardedTrees);

        units.Count.ShouldBeGreaterThan(20, "Fewer project directories than the repository has packages; discovery regressed.");
        units.Sum(static unit => unit.Files.Length).ShouldBeGreaterThan(1_000);
        units.ShouldContain(static unit => unit.Name == "Tracon.Core");
        units.ShouldNotContain(static unit => unit.Files.Any(static file => file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_shipped_and_sample_code_makes_neither_the_analyzer_nor_the_generator_throw()
    {
        var reports = await GuardedReports.Value;

        reports.SelectMany(static report => report.Failures).ShouldBeEmpty();

        // A run that registered nothing, or never executed a callback, would be green and prove nothing.
        reports.ShouldAllBe(static report => report.ActionsRun > 0, "The analyzer executed no callback on a unit.");
    }

    /// <summary>
    /// A ceiling, not a measurement: it exists to catch an analyzer that has become quadratic in the size of the code it
    /// reads, which a consumer meets as a build that stops. The figures measured on a loaded 10-core development machine
    /// are 2-4 ms of analyzer callbacks per file; the ceilings leave an order of magnitude to absorb a slow runner.
    /// </summary>
    [Fact]
    public async Task The_analyzer_cost_stays_far_below_a_per_file_ceiling()
    {
        var reports = await GuardedReports.Value;
        var files = reports.Sum(static report => report.Unit.Files.Length);
        var callbacks = reports.Sum(static report => report.AnalyzerActions.TotalMilliseconds);
        var slowestUnit = reports.MaxBy(static report => report.Analyzer)!;

        (callbacks / files).ShouldBeLessThan(
            PerFileCallbackCeilingMilliseconds,
            $"The analyzer's callbacks took {callbacks:F0} ms over {files} files.");
        slowestUnit.Analyzer.ShouldBeLessThan(
            UnitWallCeiling,
            $"{slowestUnit.Unit.Name} ({slowestUnit.Unit.Files.Length} files, {slowestUnit.Lines} lines) took {slowestUnit.Analyzer.TotalSeconds:F1} s to analyse.");
    }

    [Fact(Explicit = true)]
    public async Task Report()
    {
        var units = new List<(CorpusUnit Unit, bool Shipped)>();

        var skipRepository = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SkipRepositoryVariable));

        foreach (var unit in skipRepository ? [] : CorpusDiscovery.FromRepository(RepositoryRoot, AllTrees))
        {
            units.Add((unit, unit.Directory.StartsWith(Path.Combine(RepositoryRoot, "src") + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
        }

        foreach (var directory in (Environment.GetEnvironmentVariable(DirectoriesVariable) ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            units.AddRange(CorpusDiscovery.FromRoot(directory).Select(static unit => (unit, false)));
        }

        // A root named here is analysed one file per compilation, which is what a per-file cost needs.
        foreach (var directory in (Environment.GetEnvironmentVariable(PerFileVariable) ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            units.AddRange(CorpusDiscovery.FromRoot(directory)
                .SelectMany(static unit => unit.Files.Select(file => new CorpusUnit(Path.GetFileName(file), unit.Directory, [file])))
                .Select(static unit => (unit, false)));
        }

        // The shipped <example> blocks are consumer-shaped code written by the package's authors, one block per unit.
        var exampleDirectory = string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ExamplesVariable))
            ? null
            : Directory.CreateTempSubdirectory("tracon-example-corpus-");

        try
        {
            if (exampleDirectory is not null)
            {
                var number = 0;

                foreach (var block in ExampleExtractor.ExtractAll(RepositoryRoot).Where(static block => !string.Equals(block.Language, "json", StringComparison.Ordinal)))
                {
                    var path = Path.Combine(exampleDirectory.FullName, string.Create(CultureInfo.InvariantCulture, $"example-{number++:D3}.cs"));

                    await File.WriteAllTextAsync(path, "// " + block.Origin + Environment.NewLine + ExamplePrelude.Wrap(block.Code), TestContext.Current.CancellationToken);
                    units.Add((new CorpusUnit(Path.GetFileName(path), exampleDirectory.FullName, [path]), false));
                }
            }

            var reports = new List<UnitReport>();

            foreach (var (unit, shipped) in units)
            {
                reports.Add(await CorpusRunner.AnalyzeAsync(unit, shipped, AdditionalFiles(), countSemanticErrors: true, TestContext.Current.CancellationToken));
            }

            await WriteReportAsync(reports);
        }
        finally
        {
            exampleDirectory?.Delete(recursive: true);
        }
    }

    private static async Task WriteReportAsync(List<UnitReport> reports)
    {
        var report = new StringBuilder();
        report.AppendLine("unit\tfiles\tlines\tparse_ms\tgenerator_ms\tanalyzer_ms\tanalyzer_actions_ms\tactions\tfindings\tfailures\tsemantic_errors");

        foreach (var item in reports)
        {
            report.AppendLine(string.Join('\t',
                item.Unit.Name,
                item.Unit.Files.Length.ToString(CultureInfo.InvariantCulture),
                item.Lines.ToString(CultureInfo.InvariantCulture),
                item.Parse.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                item.Generator.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                item.Analyzer.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                item.AnalyzerActions.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
                item.ActionsRun.ToString(CultureInfo.InvariantCulture),
                item.Findings.Length.ToString(CultureInfo.InvariantCulture),
                item.Failures.Length.ToString(CultureInfo.InvariantCulture),
                item.SemanticErrors.ToString(CultureInfo.InvariantCulture)));
        }

        report.AppendLine();

        foreach (var finding in reports.SelectMany(static item => item.Findings).OrderBy(static f => f.Id, StringComparer.Ordinal).ThenBy(static f => f.File, StringComparer.Ordinal))
        {
            report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{finding.Id}\t{finding.File}:{finding.Line}\t{finding.Message}\t{SourceLine(finding)}"));
        }

        foreach (var failure in reports.SelectMany(static item => item.Failures))
        {
            report.AppendLine("FAILURE\t" + failure);
        }

        var path = Environment.GetEnvironmentVariable(ReportVariable);

        if (string.IsNullOrWhiteSpace(path))
        {
            Assert.Fail("Set " + ReportVariable + " to the file the report is written to.");
        }

        await File.WriteAllTextAsync(path, report.ToString(), TestContext.Current.CancellationToken);
    }

    private static string SourceLine(CorpusFinding finding)
    {
        if (!File.Exists(finding.File))
        {
            return string.Empty;
        }

        var lines = File.ReadAllLines(finding.File);

        return finding.Line >= 1 && finding.Line <= lines.Length ? lines[finding.Line - 1].Trim() : string.Empty;
    }

    private static async Task<List<UnitReport>> AnalyzeAsync(IReadOnlyList<CorpusUnit> units, bool countSemanticErrors)
    {
        var additionalFiles = AdditionalFiles();
        var reports = new List<UnitReport>();

        foreach (var unit in units)
        {
            var shipped = unit.Directory.StartsWith(Path.Combine(RepositoryRoot, "src") + Path.DirectorySeparatorChar, StringComparison.Ordinal);

            reports.Add(await CorpusRunner.AnalyzeAsync(unit, shipped, additionalFiles, countSemanticErrors, TestContext.Current.CancellationToken));
        }

        return reports;
    }

    /// <summary>
    /// The two files the package's build target hands the analyzer, so that the diagnostics which read them run on real
    /// content as well.
    /// </summary>
    private static List<(string Path, string Text)> AdditionalFiles()
    {
        var files = new List<(string Path, string Text)>();

        foreach (var path in new[]
        {
            Path.Combine(RepositoryRoot, "AGENTS.md"),
            Path.Combine(RepositoryRoot, "src", "Tracon.Core", "buildTransitive", "Tracon.AgentMap.md"),
        })
        {
            if (File.Exists(path))
            {
                files.Add((path, File.ReadAllText(path)));
            }
        }

        return files;
    }
}
