using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// The measured durations that stay: each one says in words why the duration itself is what the test is about, and the
/// list below is the review record of those reasons.
/// </summary>
/// <remarks>
/// <para>
/// Most measured durations in tests are a proxy: "the stream did not wait for the connection" was measured in milliseconds
/// and can be counted in clock steps. A few are not. A lease that ends on a database server's clock, a child process that
/// has to die, a lock the SQLite engine times out on with its own clock and a vendor library that sleeps without a hook are
/// real time by construction, and a test that measured them in anything else would test a stand-in. Those sites carry a
/// trailing <c>// real time: &lt;reason&gt;</c> and are not counted by the ratchet.
/// </para>
/// <para>
/// The marker is not a way around the ratchet. It excuses the <c>stopwatch</c> and <c>elapsed-assert</c> kinds on its own
/// line and nothing else (a sleep, a delay and a timed token are counted as before). It must be at least
/// <see cref="MinimumReasonLength"/> characters, it must sit on a line that measures a duration, and the file, the reason and
/// the number of lines must be listed in <see cref="RealTimeSites"/>. A new site therefore needs an edit in this file that a
/// reviewer reads, as a new timeout site needs one in <c>TimeoutSiteTests</c>.
/// </para>
/// </remarks>
public sealed partial class TestTimeRatchetTests
{
    /// <summary>The fewest characters a reason may have; a word or two is not a reason.</summary>
    private const int MinimumReasonLength = 30;

    /// <summary>One reviewed place that measures real time on purpose.</summary>
    /// <param name="File">The file, from the repository root.</param>
    /// <param name="Count">How many lines of the file carry this reason.</param>
    /// <param name="Reason">The words after <c>// real time:</c>, exactly.</param>
    private sealed record RealTimeSite(string File, int Count, string Reason);

    // Every entry is a duration that cannot be a counter or a clock step because the thing under test is not on the test's
    // clock: it is the database server's, the operating system's, an engine's or a vendor library's.
    private static readonly RealTimeSite[] RealTimeSites =
    [
        new("src/Tracon.Testing.Contracts.Xunit/Contracts/Scripts/SkillScriptExecutorContract.cs", 4, "a real child process that the executor has to stop"),
        new("tests/Shared/Infrastructure/DeltaStreamOverLatency.cs", 4, "a stream paced in real time by a model, over a real connection"),
        new("tests/Shared/Infrastructure/SqlLeaseUnderClockSkewContract.cs", 5, "a lease ends on the database server's clock, which a test cannot move"),
        new("tests/Tracon.AspNetCore.FunctionalTests/ProviderRetryAfterTests.cs", 2, "the Anthropic SDK sleeps on the system clock and offers no hook for another"),
        new("tests/Tracon.Generators.UnitTests/Corpus/CorpusRunner.cs", 1, "the analyzer's own cost is what the corpus run reports and bounds"),
        new("tests/Tracon.PostgreSql.IntegrationTests/DeltaStreamLatencyTests.cs", 1, "a stream paced in real time by a model, over a real connection"),
        new("tests/Tracon.PostgreSql.IntegrationTests/Load/BoundedSqlLoadTests.cs", 3, "an opt-in load report whose figures are wall-clock time"),
        new("tests/Tracon.SqlServer.IntegrationTests/DeltaStreamLatencyTests.cs", 1, "a stream paced in real time by a model, over a real connection"),
        new("tests/Tracon.Sqlite.IntegrationTests/SqliteWriteContentionTests.cs", 4, "SQLite's busy handler waits on the operating system's clock inside the engine"),
    ];

    [Theory]
    [InlineData("var watch = Stopwatch.StartNew(); // real time: the database server's clock cannot be moved by a test")]
    [InlineData("elapsed.ShouldBeLessThan(limit); // real time: the child process has to be stopped by the executor")]
    [InlineData("clock.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(1.5)); //real time:   a vendor library sleeps on the system clock  ")]
    [InlineData("var started = Stopwatch.GetTimestamp(); // real time: the analyzer's own cost is what the run reports")]
    public void A_measured_duration_that_says_why_it_is_real_time_is_not_counted(string line)
        => Kinds(line).ShouldBeEmpty();

    [Theory]
    [InlineData("var watch = Stopwatch.StartNew(); // real time: slow", "stopwatch")]
    [InlineData("var watch = Stopwatch.StartNew(); // real time:", "stopwatch")]
    [InlineData("var watch = Stopwatch.StartNew(); // realtime: the database server's clock cannot be moved by a test", "stopwatch")]
    [InlineData("elapsed.ShouldBeLessThan(limit); // real time: because", "elapsed-assert")]
    public void A_real_time_marker_without_a_reason_is_still_debt(string line, string kind)
        => Kinds(line).ShouldBe([kind]);

    [Theory]
    [InlineData("Thread.Sleep(50); // real time: the database server's clock cannot be moved by a test", "sleep")]
    [InlineData("await Task.Delay(50); // real time: the database server's clock cannot be moved by a test", "delay:untagged")]
    [InlineData("source.CancelAfter(span); // real time: the database server's clock cannot be moved by a test", TimedCancelKind)]
    [InlineData("var clock = ManualClock.WithSystemTimers(start); // real time: the database server's clock is not ours", RealTimersKind)]
    public void A_real_time_marker_excuses_a_measured_duration_and_nothing_else(string line, string kind)
        => Kinds(line).ShouldBe([kind]);

    [Fact]
    public void A_marker_excuses_every_measurement_on_its_line()
        => Kinds("var a = Stopwatch.StartNew(); var b = Stopwatch.StartNew(); // real time: two clocks of one database round trip")
            .ShouldBeEmpty();

    [Theory]
    [InlineData("var count = rows.Count; // real time: the database server's clock cannot be moved by a test")]
    [InlineData("await Task.Delay(50); // real time: the database server's clock cannot be moved by a test")]
    [InlineData("var clock = new ManualClock(); // real time: the database server's clock cannot be moved by a test")]
    [InlineData("var watch = Stopwatch.StartNew(); // real time: x\nvar count = 1; // real time: the database server's clock is not ours")]
    public void A_real_time_marker_on_a_line_that_measures_nothing_is_stale(string source)
        => source.Split('\n').Sum(StaleRealTimeMarkers).ShouldBe(1);

    [Theory]
    [InlineData("var watch = Stopwatch.StartNew(); // real time: the database server's clock cannot be moved by a test")]
    [InlineData("elapsed.ShouldBeLessThan(limit); // real time: the child process has to be stopped by the executor")]
    [InlineData("var watch = Stopwatch.StartNew(); // real time: x")]
    [InlineData("var watch = Stopwatch.StartNew();")]
    [InlineData("// real time: the database server's clock cannot be moved by a test")]
    [InlineData("/// <c>// real time: the database server's clock cannot be moved by a test</c>")]
    public void A_real_time_marker_on_a_measured_duration_is_not_stale(string line)
        => StaleRealTimeMarkers(line).ShouldBe(0);

    [Fact]
    public void The_real_time_markers_in_the_tree_sit_on_measured_durations()
        => MarkedRealTime(RepositoryRoot).Stale.ShouldBeEmpty(
            customMessage: "A '// real time:' marker sits on a line that measures no duration. The site was converted: " +
                           "remove the marker and its entry in RealTimeSites.");

    [Fact]
    public void The_real_time_markers_in_the_tree_carry_a_reason()
        => MarkedRealTime(RepositoryRoot).Short.ShouldBeEmpty(
            customMessage: $"A '// real time:' marker has fewer than {MinimumReasonLength} characters of reason. " +
                           "Say what makes the duration the contract of the test.");

    [Fact]
    public void Every_real_time_site_in_the_tree_is_listed_with_its_reason()
    {
        var actual = MarkedRealTime(RepositoryRoot).Sites;
        var listed = RealTimeSites.OrderBy(static site => site.File, StringComparer.Ordinal).ThenBy(static site => site.Reason, StringComparer.Ordinal).ToList();

        var failures = CompareRealTimeSites(actual, listed);

        failures.ShouldBeEmpty(
            customMessage: $"The measured durations that stay changed.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           "+ is a marker nobody reviewed: convert the measurement to a counter, a signal or a ManualClock step, " +
                           "or list the file, the count and the reason in RealTimeSites. " +
                           "- is a listed site that no longer exists or whose words changed: update the list.");
    }

    [Fact]
    public void The_list_of_real_time_sites_has_no_duplicate_and_no_short_reason()
    {
        RealTimeSites.GroupBy(static site => (site.File, site.Reason)).Where(static group => group.Count() > 1)
            .Select(static group => $"{group.Key.File}: {group.Key.Reason}")
            .ShouldBeEmpty("One reason in one file is one entry with its count.");

        RealTimeSites.Where(static site => site.Reason.Length < MinimumReasonLength || site.Count < 1)
            .Select(static site => site.File)
            .ShouldBeEmpty("A listed site has a count of at least one and a reason a marker may carry.");
    }

    [Fact]
    public void A_new_real_time_marker_is_red_until_it_is_listed()
    {
        var actual = new List<RealTimeSite> { new("tests/A.cs", 1, "a database clock that no test moves") };

        CompareRealTimeSites(actual, []).ShouldHaveSingleItem().ShouldStartWith("+ tests/A.cs");
    }

    [Fact]
    public void A_higher_count_a_changed_reason_and_a_gone_site_are_red()
    {
        var listed = new List<RealTimeSite> { new("tests/A.cs", 1, "a database clock that no test moves") };

        CompareRealTimeSites([new("tests/A.cs", 2, "a database clock that no test moves")], listed)
            .ShouldBe(["+ tests/A.cs: 2 x a database clock that no test moves", "- tests/A.cs: 1 x a database clock that no test moves"]);
        CompareRealTimeSites([new("tests/A.cs", 1, "a socket latency report")], listed)
            .ShouldBe(["+ tests/A.cs: 1 x a socket latency report", "- tests/A.cs: 1 x a database clock that no test moves"]);
        CompareRealTimeSites([], listed).ShouldHaveSingleItem().ShouldStartWith("- tests/A.cs");
        CompareRealTimeSites(listed, listed).ShouldBeEmpty();
    }

    [Fact]
    public void The_marked_sites_of_a_tree_are_counted_per_file_and_reason()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-time-ratchet");

        try
        {
            var file = Path.Combine(directory.FullName, "tests", "Marked.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(
                file,
                "var a = Stopwatch.StartNew(); // real time: a database clock that no test moves\n" +
                "a.Elapsed.ShouldBeLessThan(limit); // real time: a database clock that no test moves\n" +
                "var b = Stopwatch.StartNew(); // real time: a socket latency the test cannot give another clock\n" +
                "var c = Stopwatch.StartNew(); // real time: short\n" +
                "var d = 1; // real time: a marker that outlived its measurement\n");

            var marked = MarkedRealTime(directory.FullName);

            marked.Sites.Select(static site => (site.File, site.Count, site.Reason)).ShouldBe(
            [
                ("tests/Marked.cs", 2, "a database clock that no test moves"),
                ("tests/Marked.cs", 1, "a socket latency the test cannot give another clock"),
            ]);
            marked.Short.ShouldBe(["tests/Marked.cs:4"]);
            marked.Stale.ShouldBe(["tests/Marked.cs:5"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>The reason after a trailing marker if it is long enough to excuse the line; otherwise <see langword="null"/>.</summary>
    private static string? RealTimeReason(string line)
        => MarkerReason(line) is { Length: >= MinimumReasonLength } reason ? reason : null;

    /// <summary>The words after a trailing marker whatever their length; <see langword="null"/> when the line has no marker.</summary>
    private static string? MarkerReason(string line)
    {
        if (IsComment(line))
        {
            return null;
        }

        var marker = RealTimeMarker().Match(line);

        return marker.Success ? marker.Groups["reason"].Value : null;
    }

    private static int StaleRealTimeMarkers(string line)
        => MarkerReason(line) is not null && ElapsedAssertion().Count(line) + StopwatchUse().Count(line) == 0 ? 1 : 0;

    /// <summary>What the markers of the tree say: the places by file and reason, the ones with too short a reason and the stale ones.</summary>
    private static (List<RealTimeSite> Sites, List<string> Short, List<string> Stale) MarkedRealTime(string root)
    {
        var counts = new Dictionary<(string File, string Reason), int>();
        var shortReason = new List<string>();
        var stale = new List<string>();

        foreach (var (relative, file) in SourceFiles(root))
        {
            var number = 0;

            foreach (var line in File.ReadLines(file))
            {
                number++;

                if (MarkerReason(line) is not { } reason)
                {
                    continue;
                }

                if (StaleRealTimeMarkers(line) > 0)
                {
                    stale.Add($"{relative}:{number}");
                }
                else if (reason.Length < MinimumReasonLength)
                {
                    shortReason.Add($"{relative}:{number}");
                }
                else
                {
                    counts[(relative, reason)] = counts.GetValueOrDefault((relative, reason)) + 1;
                }
            }
        }

        var sites = counts
            .Select(static entry => new RealTimeSite(entry.Key.File, entry.Value, entry.Key.Reason))
            .OrderBy(static site => site.File, StringComparer.Ordinal)
            .ThenBy(static site => site.Reason, StringComparer.Ordinal)
            .ToList();

        return (sites, shortReason, stale);
    }

    private static List<string> CompareRealTimeSites(IReadOnlyCollection<RealTimeSite> actual, IReadOnlyCollection<RealTimeSite> listed)
    {
        var failures = new List<string>();

        foreach (var site in actual.Where(site => !listed.Contains(site)))
        {
            failures.Add($"+ {site.File}: {site.Count} x {site.Reason}");
        }

        foreach (var site in listed.Where(site => !actual.Contains(site)))
        {
            failures.Add($"- {site.File}: {site.Count} x {site.Reason}");
        }

        failures.Sort(StringComparer.Ordinal);

        return failures;
    }

    // The patterns run over this repository's own sources, one line at a time, so they carry no match timeout.
    [GeneratedRegex(@"//\s*real time:\s*(?<reason>\S.*?)?\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex RealTimeMarker();
}
