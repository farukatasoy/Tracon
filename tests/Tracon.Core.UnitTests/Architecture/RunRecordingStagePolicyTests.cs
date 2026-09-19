using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// <c>tracon.recording.stage</c> is a published, CLOSED set of six values, and the
/// counter's cardinality is bounded by nothing else. These tests scan source so that
/// a seventh value cannot arrive as a hand-written string.
/// </summary>
/// <remarks>
/// <para>
/// A unit test that compares the constant list with itself proves nothing about the
/// call sites. The same gap existed on the audit side and was closed the same way
/// (<c>AuditWritePolicyTests</c>): the risk is not the constants, it is the next
/// caller that writes its own reason inline, which is exactly what this phase had
/// to undo — <c>Disable</c> used to take a free-text string and four call sites
/// carried four different ones.
/// </para>
/// <para>
/// Source scanning is deliberately cheap and deliberately brittle: it knows nothing
/// about the compiler, and it would miss a value that reaches the counter through a
/// variable. It bounds the cheap mistake, not the determined one.
/// </para>
/// </remarks>
public sealed partial class RunRecordingStagePolicyTests
{
    /// <summary>Files allowed to publish a stage value, and how many call sites each has.</summary>
    /// <remarks>
    /// The numbers are asserted so that a new call site has to be reviewed rather than
    /// silently absorbed. <c>RunEventWriter</c>'s seven are: FIVE <c>Disable</c> calls
    /// (one per store stage, plus the late tool-completion write, which reuses the
    /// <c>ToolInvocation</c> stage because it writes to the same row through the same
    /// store), the sink site that records directly, and <c>Disable</c>'s own forward of
    /// the value to the counter. The input stage is recorded where it happens, outside
    /// the writer.
    /// </remarks>
    private static readonly (string File, int CallSites)[] AllowedCallSites =
    [
        ("src/Tracon.Core/Recording/RunEventWriter.cs", 7),
        ("src/Tracon.Core/Recording/RunRecordingAgent.Persistence.cs", 1),
    ];

    /// <summary>
    /// The one parameter name a stage may be forwarded through.
    /// </summary>
    /// <remarks>
    /// <c>RunEventWriter.Disable</c> takes the stage from its own caller and hands it
    /// to the counter. Each of those callers is itself matched by this scan, so the
    /// forward is checked at the point the value is actually chosen.
    /// </remarks>
    private const string ForwardedParameter = "stage";

    [Fact]
    public void Every_stage_value_reaching_the_counter_comes_from_the_closed_set()
    {
        var repoRoot = FindRepoRoot();
        var offenders = new List<string>();
        var found = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file))
            {
                continue;
            }

            foreach (Match match in CallPattern().Matches(File.ReadAllText(file)))
            {
                found++;

                var argument = match.Groups["stage"].Value.Trim();

                if (!argument.StartsWith("RunRecordingStages.", StringComparison.Ordinal)
                    && !string.Equals(argument, ForwardedParameter, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(repoRoot, file)}: {match.Value.Trim()}");
                }
            }
        }

        // A scan that collects nothing cannot fail. Both call shapes must still exist,
        // or the pattern below has drifted away from the code it is guarding.
        found.ShouldBeGreaterThan(
            0,
            "no RecordRunRecordingFailure/Disable call was found; the pattern no longer matches the code.");

        offenders.ShouldBeEmpty(
            "the stage tag is a closed set; a hand-written string here is how cardinality escapes.");
    }

    [Fact]
    public void Only_reviewed_files_publish_a_stage_value()
    {
        var repoRoot = FindRepoRoot();

        var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file))
            {
                continue;
            }

            var count = CallPattern().Count(File.ReadAllText(file));

            if (count > 0)
            {
                actual[Path.GetRelativePath(repoRoot, file).Replace('\\', '/')] = count;
            }
        }

        var expected = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (file, callSites) in AllowedCallSites)
        {
            expected[file] = callSites;
        }

        actual.ShouldBe(
            expected,
            "a new place that records a recording failure is a new place the stage set can drift; " +
            "add it here together with its stage constant and the site's tag documentation.");
    }

    /// <summary>
    /// Matches the second argument of <c>Disable(...)</c> and the second argument of
    /// <c>RecordRunRecordingFailure(...)</c> — the two shapes that put a value on the tag.
    /// </summary>
    [GeneratedRegex(
        @"(?<!void\s)(?:Disable|RecordRunRecordingFailure)\(\s*[\w.?\[\]]+\s*,\s*(?<stage>""[^""]*""|[\w.]+)\s*\)",
        RegexOptions.None,
        matchTimeoutMilliseconds: 5000)]
    private static partial Regex CallPattern();

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root (Tracon.slnx) from the test output directory.");
    }
}
