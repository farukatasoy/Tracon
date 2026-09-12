using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the Playwright-locator ratchet: a text-based locator
/// (<c>GetByText</c>, <c>GetByPlaceholder</c>, <c>GetByLabel</c>,
/// <c>GetByRole</c>, <c>GetByAltText</c>, <c>GetByTitle</c>) that carries
/// neither <c>Exact = true</c> nor a <c>.First</c>/<c>.Nth(...)</c> narrowing
/// call matches a SUBSTRING of the accessible name by default, and can
/// silently resolve to the wrong element - or to more than one, which
/// Playwright's strict mode then fails on nondeterministically depending on
/// DOM order.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards repeated three times (phase 16 twice, phase 19):
/// <c>GetByPlaceholder("github")</c>, <c>GetByText("Awaiting input")</c>, and
/// <c>GetByRole(Heading, Name: "Experiments")</c> all matched more broadly
/// than the author intended.
/// </para>
/// <para>
/// This is a ratchet, not a gate that must reach zero: 125 of today's 181
/// calls are risky, and fixing every one is out of this phase's scope - some
/// of them are legitimately safe (the matched text really is unique on the
/// page). The baseline is <strong>per file, a count, not a line list</strong>
/// - the same contract as <see cref="SourceLanguageTests"/> - because the E2E
/// files this scans are edited often and a line-number list would conflict on
/// nearly every change. It can only shrink: a new risky call fails the test,
/// and a fixed one requires a refresh so the debt is actually recorded as
/// paid down. Refresh:
/// <c>TRACON_PLAYWRIGHT_LOCATOR_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class PlaywrightLocatorTests
{
    private const string RefreshEnvVar = "TRACON_PLAYWRIGHT_LOCATOR_REFRESH";

    private static readonly string[] LocatorMethods =
        ["GetByText", "GetByPlaceholder", "GetByLabel", "GetByRole", "GetByAltText", "GetByTitle"];

    /// <summary>A chained narrowing call immediately after the locator's closing parenthesis.</summary>
    private static readonly Regex NarrowingChainPattern = new(
        @"^\s*\.(?:First\b|Nth\s*\()",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// This class's own source carries example locator calls inside string
    /// literals (the risky/safe shapes its tests assert on); scanning it
    /// would count its own fixtures as real risky calls. Same exemption
    /// pattern as <see cref="SourceLanguageTests"/>'s <c>SkippedFiles</c>.
    /// </summary>
    private static readonly string[] SkippedFiles =
        ["tests/Tracon.Core.UnitTests/Architecture/PlaywrightLocatorTests.cs"];

    [Fact]
    public void Risky_locator_counts_match_the_baseline()
    {
        var actual = Scan(RepositoryRoot);

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            WriteBaseline(actual);
        }

        File.Exists(BaselinePath).ShouldBeTrue(
            $"'{BaselinePath}' is missing. Generate it with {RefreshEnvVar}=1 (see the class remarks).");

        var baseline = ReadBaseline();
        var failures = new List<string>();

        foreach (var (path, count) in actual)
        {
            var allowed = baseline.GetValueOrDefault(path, 0);

            if (count > allowed)
            {
                failures.Add($"+ {path}: {count} risky locator calls, baseline allows {allowed}");
            }
        }

        foreach (var (path, allowed) in baseline)
        {
            var count = actual.GetValueOrDefault(path, 0);

            if (count < allowed)
            {
                failures.Add($"- {path}: {count} risky locator calls, baseline still allows {allowed} — refresh it");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The Playwright locator baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"A call that only just became risky needs Exact = true or .First/.Nth(...); a call " +
                           $"that was fixed needs the baseline refreshed: {RefreshEnvVar}=1 " +
                           "dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    /// <summary>Regression coverage for the scan itself, isolated from the real repository tree.</summary>
    [Fact]
    public void Scan_counts_a_locator_with_neither_Exact_nor_a_narrowing_call_as_risky()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-locator-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """
                public sealed class SampleTest
                {
                    public async Task RunAsync(IPage page)
                    {
                        await page.GetByText("hello").ClickAsync();
                    }
                }
                """);

            var actual = Scan(directory.FullName);

            actual.ShouldContainKeyAndValue("Sample.cs", 1);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_does_not_count_a_locator_guarded_by_First()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-locator-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """
                public sealed class SampleTest
                {
                    public async Task RunAsync(IPage page)
                    {
                        await page.GetByText("hello").First.WaitForAsync();
                    }
                }
                """);

            Scan(directory.FullName).ShouldNotContainKey("Sample.cs");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_does_not_count_a_locator_with_Exact_true()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-locator-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """
                public sealed class SampleTest
                {
                    public async Task RunAsync(IPage page)
                    {
                        await page.GetByRole(AriaRole.Heading, new() { Name = "Experiments", Exact = true }).ClickAsync();
                    }
                }
                """);

            Scan(directory.FullName).ShouldNotContainKey("Sample.cs");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SortedDictionary<string, int> Scan(string root)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var testsRoot = Path.Combine(root, "tests");
        var scanRoot = Directory.Exists(testsRoot) ? testsRoot : root;

        foreach (var file in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (SkippedFiles.Contains(relative, StringComparer.Ordinal))
            {
                continue;
            }

            var text = File.ReadAllText(file);
            var risky = CountRiskyCalls(text);

            if (risky > 0)
            {
                counts[relative] = risky;
            }
        }

        return counts;
    }

    /// <summary>
    /// Walks every <c>GetByXxx(</c> call, balances its parentheses to find
    /// where it actually ends (arguments can nest their own parentheses, as
    /// <c>new() { Name = "X" }</c> does not, but <c>GetByRole(Role, new()
    /// {...})</c>'s outer call does), and checks the call's own argument text
    /// for <c>Exact</c> and the text immediately after it for a chained
    /// <c>.First</c>/<c>.Nth(...)</c>.
    /// </summary>
    private static int CountRiskyCalls(string text)
    {
        var risky = 0;

        foreach (var marker in LocatorMethods)
        {
            var search = marker + "(";
            var index = 0;

            while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
            {
                var argumentsStart = index + search.Length;
                var callEnd = FindMatchingParen(text, argumentsStart);

                if (callEnd < 0)
                {
                    break;
                }

                var arguments = text[argumentsStart..callEnd];
                var tail = text[(callEnd + 1)..Math.Min(text.Length, callEnd + 1 + 64)];

                if (!arguments.Contains("Exact", StringComparison.Ordinal) && !NarrowingChainPattern.IsMatch(tail))
                {
                    risky++;
                }

                index = callEnd + 1;
            }
        }

        return risky;
    }

    /// <summary>
    /// Finds the index of the <c>)</c> that closes the <c>(</c> ONE POSITION
    /// before <paramref name="argumentsStart"/>, tracking nested parentheses.
    /// Returns -1 if the text ends unbalanced (never true for compiling
    /// source, kept only so a malformed scratch file cannot hang the scan).
    /// </summary>
    private static int FindMatchingParen(string text, int argumentsStart)
    {
        var depth = 1;

        for (var i = argumentsStart; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')')
            {
                depth--;

                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static Dictionary<string, int> ReadBaseline()
    {
        var baseline = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(BaselinePath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.LastIndexOf('|');

            separator.ShouldBeGreaterThan(0, $"Malformed baseline line: '{line}'");

            baseline[line[..separator]] = int.Parse(
                line[(separator + 1)..],
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return baseline;
    }

    private static void WriteBaseline(SortedDictionary<string, int> actual)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Generated by PlaywrightLocatorTests. Refresh:");
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
        builder.AppendLine("# One line per <path>|<risky locator call count>. The count may only shrink.");
        builder.AppendLine("# A call is risky when it carries neither Exact = true nor .First/.Nth(...).");

        foreach (var (path, count) in actual)
        {
            builder.Append(path).Append('|').Append(count).AppendLine();
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "playwright-locator-baseline.txt");

    private static string FindRepositoryRoot()
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
