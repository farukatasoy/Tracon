using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// A test does not wait for real time and does not measure it. Every place that still does is counted per file, and the
/// count can only fall.
/// </summary>
/// <remarks>
/// <para>
/// A real wait makes a test slow on every run, and a measured duration makes its verdict depend on how busy the machine
/// is: a run asserted to take at least 600 ms read 599.5 ms on one CI leg. A test that needs time to pass advances a
/// <c>ManualClock</c>; a test that needs a state waits for it with <c>WaitUntil</c>.
/// </para>
/// <para>
/// This is a ratchet, not a snapshot. <c>test-time-baseline.txt</c> records what each file is still allowed. A new
/// entry and a higher count fail, and so does a lower count until the baseline is lowered with it, so the debt cannot
/// come back unnoticed. Lower it after a clean-up pass:
/// <c>TRACON_TEST_TIME_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>. The refresh lowers counts and
/// removes entries that reached zero; it never raises one and never adds one.
/// </para>
/// <para>
/// A delay ended by cancellation costs nothing and is not counted. Every finite delay is, whatever its class says, so
/// renaming a class does not pay a debt. <c>TestDelayClassificationTests</c> still requires a reason on every delay; this
/// class limits how many delays that take real time may exist.
/// </para>
/// <para>
/// <c>ManualClock</c> itself can pay real time: made with <c>WithSystemTimers</c> or <c>OverSystemClock</c>, or given a
/// <c>SystemTimersWithin</c> or <c>AutoFireWithin</c>, its timers run on the system's clock or fire on their own. Those
/// modes are migration aids, and the clock's own source is exempt, so each use in a test is counted per file as
/// <c>manual-clock-real-timers</c>; <c>ManualClockTests</c> exercises them and is exempt too.
/// </para>
/// <para>
/// A file that moves keeps its debt: change the path in the baseline by hand, in the same change, and leave the count.
/// </para>
/// </remarks>
public sealed partial class TestTimeRatchetTests
{
    private const string RefreshEnvVar = "TRACON_TEST_TIME_REFRESH";

    private const string BaselineFile = "tests/Tracon.Core.UnitTests/Architecture/test-time-baseline.txt";

    /// <summary>The fake every other clock moves to; it is the one subclass that stays.</summary>
    private const string StandardClockFile = "tests/Shared/Time/ManualClock.cs";

    /// <summary>The tests of the standard clock; they use its real-time modes to prove them.</summary>
    private const string StandardClockTestsFile = "tests/Tracon.Core.UnitTests/TestInfrastructure/ManualClockTests.cs";

    private const string TimeProviderSubclassKind = "time-provider-subclass";

    private const string RealTimersKind = "manual-clock-real-timers";

    private static readonly string[] ScanRoots = ["tests", "src/Tracon.Testing.Contracts.Xunit"];

    /// <summary>These sources name the patterns in their samples.</summary>
    private static readonly string[] SkippedFiles =
    [
        "tests/Tracon.Core.UnitTests/Architecture/TestDelayClassificationTests.cs",
        "tests/Tracon.Core.UnitTests/Architecture/TestTimeRatchetTests.cs",
    ];

    /// <summary>The pause between two probes, allowed only inside <c>WaitUntil</c> by the classification test.</summary>
    private const string PollClass = "poll";

    [Fact]
    public void Real_time_in_tests_matches_the_baseline()
    {
        var actual = Scan(RepositoryRoot);
        var path = Path.Combine(RepositoryRoot, BaselineFile);

        // A missing baseline is red even under a refresh: writing one here would accept every debt in the tree.
        File.Exists(path).ShouldBeTrue($"'{BaselineFile}' is missing. Restore it; a refresh only lowers an existing baseline.");

        var baseline = ReadBaseline(path);

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            baseline = Refreshed(actual, baseline);
            WriteBaseline(path, baseline);
        }

        var failures = Compare(actual, baseline);

        failures.ShouldBeEmpty(
            customMessage: $"Real time in tests changed.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           "+ is new debt: advance a ManualClock, or wait for the state with WaitUntil. " +
                           $"- is paid debt: lower the baseline with {RefreshEnvVar}=1.");
    }

    [Theory]
    [InlineData("Thread.Sleep(50);", "sleep")]
    [InlineData("clock.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(1));", "elapsed-assert")]
    [InlineData("elapsedTime.ShouldBeLessThan(limit);", "elapsed-assert")]
    [InlineData("await Should.CompleteIn(work, TimeSpan.FromSeconds(1));", "elapsed-assert")]
    [InlineData("(DateTimeOffset.UtcNow - started).ShouldBeLessThan(TimeSpan.FromSeconds(30));", "elapsed-assert")]
    [InlineData("(clock.GetUtcNow() - started).ShouldBeLessThan(limit);", "elapsed-assert")]
    [InlineData("settled.Duration.Value.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(300));", "elapsed-assert")]
    [InlineData("inLine.StreamTime.ShouldBeGreaterThan(TimeSpan.FromMilliseconds(2));", "elapsed-assert")]
    [InlineData("stopTime.ShouldBeLessThan(StopBound);", "elapsed-assert")]
    [InlineData("var clock = Stopwatch.StartNew();", "stopwatch")]
    [InlineData("var started = Stopwatch.GetTimestamp();", "stopwatch")]
    [InlineData("var clock = new Stopwatch();", "stopwatch")]
    [InlineData("private sealed class FixedTime : TimeProvider", "time-provider-subclass")]
    [InlineData("private sealed class FixedTime(DateTimeOffset now) : TimeProvider", "time-provider-subclass")]
    [InlineData("var clock = ManualClock.WithSystemTimers(start);", "manual-clock-real-timers")]
    [InlineData("var clock = ManualClock.OverSystemClock(TimeSpan.FromHours(1), systemTimestamp: false);", "manual-clock-real-timers")]
    [InlineData("var clock = new ManualClock { SystemTimersWithin = TimeSpan.FromMilliseconds(100) };", "manual-clock-real-timers")]
    [InlineData("    AutoFireWithin = TimeSpan.FromSeconds(1),", "manual-clock-real-timers")]
    [InlineData("await Task.Delay(200); // delay: simulated", "delay:simulated")]
    [InlineData("await Task.Delay(200, token); // delay: product", "delay:product")]
    [InlineData("await Task.Delay(200); // delay: negative", "delay:negative")]
    [InlineData("await Task.Delay(200);", "delay:untagged")]
    [InlineData("await Task.Delay(1000); // delay: retry", "delay:retry")]
    [InlineData("await Task.Delay(1000); // delay: bound", "delay:bound")]
    [InlineData("await Task.Delay(1000); // delay: fixture", "delay:fixture")]
    public void The_scan_counts_real_time(string line, string kind)
        => Kinds(line).ShouldBe([kind]);

    [Theory]
    [InlineData("await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated")]
    [InlineData("await Task.Delay(-1, token); // delay: simulated")]
    [InlineData("await Task.Delay(20); // delay: poll")]
    [InlineData("options.CreatedTime.ShouldBe(expected);")]
    [InlineData("// Thread.Sleep(50) used to stand here")]
    [InlineData("/// <see cref=\"Stopwatch.StartNew\"/>")]
    [InlineData("* Thread.Sleep(50)")]
    [InlineData("var seen = clock.GetUtcNow();")]
    [InlineData("var clock = new ManualClock();")]
    [InlineData("// var clock = ManualClock.WithSystemTimers(start);")]
    [InlineData("/// <see cref=\"ManualClock.OverSystemClock\"/> and <c>SystemTimersWithin = x</c>")]
    [InlineData("if (clock.AutoFireWithin == null) { }")]
    public void The_scan_leaves_the_rest_alone(string line)
        => Kinds(line).ShouldBeEmpty();

    [Fact]
    public void Every_real_time_mode_on_one_line_is_counted()
        => Kinds("var clock = new ManualClock { SystemTimersWithin = poll, AutoFireWithin = poll };")
            .ShouldBe([RealTimersKind, RealTimersKind]);

    [Fact]
    public void A_new_sleep_is_red()
    {
        var failures = Compare(Counts(("sleep|tests/A.cs", 1)), Counts());

        failures.ShouldHaveSingleItem().ShouldStartWith("+ sleep|tests/A.cs");
    }

    [Fact]
    public void A_higher_count_is_red()
    {
        var failures = Compare(Counts(("sleep|tests/A.cs", 3)), Counts(("sleep|tests/A.cs", 2)));

        failures.ShouldHaveSingleItem().ShouldStartWith("+ sleep|tests/A.cs");
    }

    [Fact]
    public void A_paid_debt_is_red_until_the_baseline_is_lowered()
    {
        var failures = Compare(Counts(("sleep|tests/A.cs", 1)), Counts(("sleep|tests/A.cs", 2)));

        failures.ShouldHaveSingleItem().ShouldStartWith("- sleep|tests/A.cs");
    }

    [Fact]
    public void An_unchanged_count_is_green()
        => Compare(Counts(("sleep|tests/A.cs", 2)), Counts(("sleep|tests/A.cs", 2))).ShouldBeEmpty();

    [Fact]
    public void A_refresh_lowers_counts_and_never_raises_or_adds_one()
    {
        var refreshed = Refreshed(
            Counts(("sleep|tests/Lower.cs", 1), ("sleep|tests/Higher.cs", 9), ("sleep|tests/New.cs", 4)),
            Counts(("sleep|tests/Lower.cs", 3), ("sleep|tests/Higher.cs", 2), ("sleep|tests/Gone.cs", 5)));

        refreshed.ShouldBe(Counts(("sleep|tests/Lower.cs", 1), ("sleep|tests/Higher.cs", 2)));
    }

    [Fact]
    public void The_standard_clock_and_its_tests_are_not_debt()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-time-ratchet");

        try
        {
            var clock = Path.Combine(directory.FullName, "tests", "Shared", "Time", "ManualClock.cs");
            var clockTests = Path.Combine(directory.FullName, StandardClockTestsFile);
            var other = Path.Combine(directory.FullName, "tests", "Other.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(clock)!);
            Directory.CreateDirectory(Path.GetDirectoryName(clockTests)!);
            File.WriteAllText(
                clock,
                "internal sealed class ManualClock : TimeProvider\n" +
                "{\n" +
                "    public static ManualClock WithSystemTimers(DateTimeOffset? start = null) => new();\n" +
                "    public TimeSpan? SystemTimersWithin { get; init; }\n" +
                "}\n");
            File.WriteAllText(clockTests, "var clock = ManualClock.WithSystemTimers(start);\n");
            File.WriteAllText(
                other,
                "internal sealed class OtherClock : TimeProvider\n" +
                "var clock = ManualClock.WithSystemTimers(start);\n");

            Scan(directory.FullName).ShouldBe(
                Counts(($"{TimeProviderSubclassKind}|tests/Other.cs", 1), ($"{RealTimersKind}|tests/Other.cs", 1)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SortedDictionary<string, int> Counts(params (string Key, int Count)[] entries)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (key, count) in entries)
        {
            counts[key] = count;
        }

        return counts;
    }

    private static List<string> Compare(SortedDictionary<string, int> actual, SortedDictionary<string, int> baseline)
    {
        var failures = new List<string>();

        foreach (var (key, count) in actual)
        {
            var allowed = baseline.GetValueOrDefault(key, 0);

            if (count > allowed)
            {
                failures.Add($"+ {key}: {count}, baseline allows {allowed}");
            }
        }

        foreach (var (key, allowed) in baseline)
        {
            var count = actual.GetValueOrDefault(key, 0);

            if (count < allowed)
            {
                failures.Add($"- {key}: {count}, baseline still allows {allowed}");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        return failures;
    }

    private static SortedDictionary<string, int> Refreshed(
        SortedDictionary<string, int> actual,
        SortedDictionary<string, int> baseline)
    {
        var refreshed = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (key, allowed) in baseline)
        {
            var count = Math.Min(actual.GetValueOrDefault(key, 0), allowed);

            if (count > 0)
            {
                refreshed[key] = count;
            }
        }

        return refreshed;
    }

    private static SortedDictionary<string, int> Scan(string root)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var scanRoot in ScanRoots)
        {
            var absoluteRoot = Path.Combine(root, scanRoot);

            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (relative.Contains("/obj/", StringComparison.Ordinal) ||
                    relative.Contains("/bin/", StringComparison.Ordinal) ||
                    SkippedFiles.Contains(relative, StringComparer.Ordinal))
                {
                    continue;
                }

                foreach (var line in File.ReadLines(file))
                {
                    foreach (var kind in Kinds(line))
                    {
                        if (IsExempt(kind, relative))
                        {
                            continue;
                        }

                        var key = $"{kind}|{relative}";

                        counts[key] = counts.GetValueOrDefault(key, 0) + 1;
                    }
                }
            }
        }

        return counts;
    }

    private static bool IsExempt(string kind, string relative)
        => kind switch
        {
            TimeProviderSubclassKind => string.Equals(relative, StandardClockFile, StringComparison.Ordinal),
            RealTimersKind => string.Equals(relative, StandardClockFile, StringComparison.Ordinal)
                || string.Equals(relative, StandardClockTestsFile, StringComparison.Ordinal),
            _ => false,
        };

    private static List<string> Kinds(string line)
    {
        var kinds = new List<string>();
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
        {
            return kinds;
        }

        AddMatches(kinds, line, Sleep(), "sleep");
        AddMatches(kinds, line, ElapsedAssertion(), "elapsed-assert");
        AddMatches(kinds, line, StopwatchUse(), "stopwatch");
        AddMatches(kinds, line, TimeProviderSubclass(), TimeProviderSubclassKind);
        AddMatches(kinds, line, ManualClockRealTimers(), RealTimersKind);

        if (line.Contains("Task.Delay(", StringComparison.Ordinal) && !Unbounded().IsMatch(line))
        {
            var tag = DelayTag().Match(line);

            if (!tag.Success)
            {
                kinds.Add("delay:untagged");
            }
            else if (!string.Equals(tag.Groups["class"].Value, PollClass, StringComparison.Ordinal))
            {
                kinds.Add($"delay:{tag.Groups["class"].Value}");
            }
        }

        return kinds;
    }

    private static void AddMatches(List<string> kinds, string line, Regex pattern, string kind)
    {
        for (var count = pattern.Count(line); count > 0; count--)
        {
            kinds.Add(kind);
        }
    }

    // The patterns run over this repository's own sources, one line at a time, so they carry no match timeout: a timeout
    // would make this gate depend on how busy the machine is.
    [GeneratedRegex(@"\bThread\.Sleep\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex Sleep();

    [GeneratedRegex(
        @"\b\w*[Ee]lapsed\w*\b[^;]*\.ShouldBe(?:GreaterThan|GreaterThanOrEqualTo|LessThan|LessThanOrEqualTo|InRange)\b" +
        @"|\b\w*(?:Duration|Time)\b(?:\.\w+)*\.ShouldBe(?:GreaterThan|GreaterThanOrEqualTo|LessThan|LessThanOrEqualTo|InRange)\b" +
        @"|\b(?:UtcNow|GetUtcNow\(\))\s*-\s*[^)]*\)\s*\.ShouldBe(?:GreaterThan|GreaterThanOrEqualTo|LessThan|LessThanOrEqualTo|InRange)\b" +
        @"|\bShould\.CompleteIn\(",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex ElapsedAssertion();

    [GeneratedRegex(
        @"\bStopwatch\.(?:StartNew|GetTimestamp)\(|\bnew Stopwatch\(\)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex StopwatchUse();

    [GeneratedRegex(
        @"\bclass\s+\w+(?:\([^)]*\))?\s*:\s*TimeProvider\b",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex TimeProviderSubclass();

    [GeneratedRegex(
        @"\b(?:WithSystemTimers|OverSystemClock)\(|\b(?:SystemTimersWithin|AutoFireWithin)\s*=(?!=)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex ManualClockRealTimers();

    [GeneratedRegex(
        @"Task\.Delay\(\s*(?:Timeout\.Infinite(?:TimeSpan)?\b|-1\b)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex Unbounded();

    [GeneratedRegex(@"//\s*delay:\s*(?<class>[a-z]+)\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex DelayTag();

    private static SortedDictionary<string, int> ReadBaseline(string path)
    {
        var baseline = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.LastIndexOf('|');

            baseline[line[..separator]] = int.Parse(line[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture);
        }

        return baseline;
    }

    private static void WriteBaseline(string path, SortedDictionary<string, int> baseline)
    {
        var content = new StringBuilder()
            .Append("# Generated by TestTimeRatchetTests. Lower it after a clean-up:\n")
            .Append($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release\n")
            .Append("# One line per kind and file: <kind>|<path>|<count>. A count may only fall.\n");

        foreach (var (key, count) in baseline)
        {
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"{key}|{count}\n");
        }

        File.WriteAllText(path, content.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

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
