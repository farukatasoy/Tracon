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
    public void Scan_does_not_count_a_locator_named_inside_a_comment()
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
                        // GetByLabel("Theme") never matches exactly here, so the
                        // wrapper is scoped instead.
                        await page.Locator("label").ClickAsync();
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

    /// <summary>
    /// Phase 184: a <c>//</c> inside a string literal was read as a comment,
    /// and the scan then stopped counting that locator kind for the rest of
    /// the file.
    /// </summary>
    [Fact]
    public void Scan_is_not_stopped_by_a_url_inside_a_string()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-locator-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """"
                public sealed class SampleTest
                {
                    public async Task RunAsync(IPage page)
                    {
                        await page.GetByPlaceholder("https://mcp.example.com/mcp").FillAsync("https://a.test/");
                        await page.GetByPlaceholder($"{Url}/mcp", new() { Exact = true }).FillAsync(@"C:\dir\");
                        await page.GetByText("""a "quoted" // not a comment""").ClickAsync();
                        await page.GetByPlaceholder("later").FillAsync("x");
                    }
                }
                """");

            Scan(directory.FullName).ShouldContainKeyAndValue("Sample.cs", 3);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Scan_does_not_count_a_locator_named_inside_a_block_comment()
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
                        /* GetByText("old") was replaced
                           by GetByTestId below. */
                        await page.GetByTestId("new").ClickAsync();
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

            var text = WithoutComments(File.ReadAllText(file));
            var risky = CountRiskyCalls(text);

            if (risky > 0)
            {
                counts[relative] = risky;
            }
        }

        return counts;
    }

    /// <summary>
    /// Blanks out comments, keeping the line count and every other character
    /// position intact. String and character literals are read, never blanked.
    /// </summary>
    /// <remarks>
    /// A comment that MENTIONS a locator is not a locator call. Counting one
    /// inflates the file's debt by a number nobody can find in the code, and —
    /// worse — it lets a real risky call be added for free whenever a comment
    /// explaining a locator is deleted in the same change. Measured in phase
    /// 164: <c>UiTests.cs</c> carried two such comments.
    /// <para>
    /// 🚨 Phase 184: the first version cut every line at its first <c>//</c>,
    /// inside a string literal too. <c>GetByPlaceholder("https://mcp.example.com/mcp")</c>
    /// lost its closing parenthesis, the parenthesis walk below ran off the end
    /// of the file, and the scan stopped counting <c>GetByPlaceholder</c> for
    /// the rest of <c>UiTests.cs</c>: eight calls, seven of them risky, never
    /// reached the ratchet. The file's split into one file per screen is what
    /// showed it - the same calls counted 119 in one file and 126 in many.
    /// </para>
    /// </remarks>
    private static string WithoutComments(string text)
    {
        var output = text.ToCharArray();
        var index = 0;

        ReadCode(text, output, ref index, insideHole: false);

        return new string(output);
    }

    /// <summary>
    /// Reads code, blanking its comments, until the text ends - or, inside an
    /// interpolation hole, until the <c>}</c> that closes the hole.
    /// </summary>
    private static void ReadCode(string text, char[] output, ref int index, bool insideHole)
    {
        var braces = 0;

        while (index < text.Length)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                while (index < text.Length && text[index] != '\n')
                {
                    output[index++] = ' ';
                }
            }
            else if (current == '/' && next == '*')
            {
                var close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                var stop = close < 0 ? text.Length : close + 2;

                for (; index < stop; index++)
                {
                    output[index] = text[index] == '\n' ? '\n' : ' ';
                }
            }
            else if (current == '\'')
            {
                ReadCharacter(text, ref index);
            }
            else if (StartsString(text, index))
            {
                ReadString(text, output, ref index);
            }
            else
            {
                if (insideHole && current == '{')
                {
                    braces++;
                }
                else if (insideHole && current == '}')
                {
                    if (braces == 0)
                    {
                        return;
                    }

                    braces--;
                }

                index++;
            }
        }
    }

    /// <summary>Whether a string literal (with any <c>$</c>/<c>@</c> prefix) starts at <paramref name="index"/>.</summary>
    private static bool StartsString(string text, int index)
    {
        var at = index;

        while (at < text.Length && text[at] is '$' or '@')
        {
            at++;
        }

        return at < text.Length && text[at] == '"';
    }

    private static void ReadCharacter(string text, ref int index)
    {
        index++;

        while (index < text.Length && text[index] != '\'')
        {
            index += text[index] == '\\' ? 2 : 1;
        }

        index++;
    }

    private static void ReadString(string text, char[] output, ref int index)
    {
        var dollars = 0;
        var verbatim = false;

        while (text[index] is '$' or '@')
        {
            dollars += text[index] == '$' ? 1 : 0;
            verbatim |= text[index] == '@';
            index++;
        }

        var quotes = 0;

        while (index + quotes < text.Length && text[index + quotes] == '"')
        {
            quotes++;
        }

        if (quotes == 2)
        {
            // An empty string: "" (or @"", $"").
            index += 2;
            return;
        }

        var raw = quotes >= 3;
        index += raw ? quotes : 1;

        while (index < text.Length)
        {
            var current = text[index];

            if (raw && current == '"' && Run(text, index, '"') >= quotes)
            {
                index += Run(text, index, '"');
                return;
            }

            if (!raw && current == '"')
            {
                if (verbatim && index + 1 < text.Length && text[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                index++;
                return;
            }

            if (!raw && !verbatim && current == '\\')
            {
                index += 2;
                continue;
            }

            if (dollars > 0 && current == '{')
            {
                var run = Run(text, index, '{');
                var opens = raw ? run >= dollars : run % 2 == 1;

                index += raw ? dollars : run;

                if (opens)
                {
                    ReadCode(text, output, ref index, insideHole: true);
                    index += raw ? dollars : 1;
                }

                continue;
            }

            index++;
        }
    }

    /// <summary>How many times <paramref name="character"/> repeats from <paramref name="index"/>.</summary>
    private static int Run(string text, int index, char character)
    {
        var run = 0;

        while (index + run < text.Length && text[index + run] == character)
        {
            run++;
        }

        return run;
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
                    // Never true for compiling source. Counted rather than
                    // skipped: stopping here would hide every later call of
                    // this kind in the file (Phase 184).
                    risky++;
                    index = argumentsStart;
                    continue;
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
