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
/// <c>WaitUntil</c> measures its own failure bound with a <c>Stopwatch</c>. The bound only ends a wait that already failed,
/// so that is the one place where real time is the contract and the row could never reach zero: exactly that file is exempt
/// for exactly that kind. Another file's stopwatch, and any other kind in that file, is still counted.
/// </para>
/// <para>
/// A token that expires after a span (<c>CancelAfter</c>, <c>new CancellationTokenSource(span)</c>) is either a failure bound
/// ("give up after 30 s": the expiry is never the expected outcome) or a wait (the test expects the expiry, or sits until
/// it). Syntax cannot tell them apart, so the author says which: a bound carries a trailing <c>// cancel: bound</c> and is
/// not counted, and every other timed token is counted per file as <c>timed-cancel</c>. A wait becomes a signal (cancel
/// when the awaited thing happens) or a token on a <c>ManualClock</c> (<c>new CancellationTokenSource(span, clock)</c>
/// is not counted). A marker on a line that makes no timed token is red, so a converted site cannot keep a stale one.
/// </para>
/// <para>
/// A measured duration (a <c>Stopwatch</c>, an assert on an elapsed value) is debt unless the duration itself is what the
/// test is about: a database server's clock, a real child process, a lock the engine times out on, an analyzer's cost, the
/// latency of a connection, a vendor SDK's backoff that cannot be given another clock. Such a site is not made to look
/// like something else. It carries a trailing <c>// real time: &lt;the reason&gt;</c> of at least thirty characters, is not
/// counted, and is listed with its reason in <c>RealTimeSites</c> (<c>TestTimeRatchetTests.RealTimeSites.cs</c>), so a new
/// one needs a reviewed entry. The marker excuses only the <c>stopwatch</c> and <c>elapsed-assert</c> kinds on its own
/// line, and a marker on a line that has neither is red, so a converted site cannot keep a stale one.
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

    /// <summary>The shared condition wait; its stopwatch only limits how long a failure takes.</summary>
    private const string ConditionWaitFile = "tests/Shared/Waiting/WaitUntil.cs";

    private const string TimeProviderSubclassKind = "time-provider-subclass";

    private const string RealTimersKind = "manual-clock-real-timers";

    private const string StopwatchKind = "stopwatch";

    private const string TimedCancelKind = "timed-cancel";

    private static readonly string[] ScanRoots = ["tests", "src/Tracon.Testing.Contracts.Xunit"];

    /// <summary>These sources name the patterns in their samples.</summary>
    private static readonly string[] SkippedFiles =
    [
        "tests/Tracon.Core.UnitTests/Architecture/TestDelayClassificationTests.cs",
        "tests/Tracon.Core.UnitTests/Architecture/TestTimeRatchetTests.cs",
        "tests/Tracon.Core.UnitTests/Architecture/TestTimeRatchetTests.RealTimeSites.cs",
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
    [InlineData("cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));", "timed-cancel")]
    [InlineData("using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));", "timed-cancel")]
    [InlineData("using var cts = timeout is { } t ? new CancellationTokenSource(t) : new CancellationTokenSource();", "timed-cancel")]
    [InlineData("using var source = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, limit)));", "timed-cancel")]
    [InlineData("bound.CancelAfter(WaitUntil.DefaultTimeout);", "timed-cancel")]
    [InlineData("deadline.CancelAfter(TimeSpan.FromSeconds(30)); // cancel: bounded", "timed-cancel")]
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
    [InlineData("using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock);")]
    [InlineData("using var deadline = new CancellationTokenSource(Math.Max(1, 2) * delay, timeProvider);")]
    [InlineData("using var source = new CancellationTokenSource(Timeout.InfiniteTimeSpan);")]
    [InlineData("using var source = new CancellationTokenSource( Timeout.InfiniteTimeSpan );")]
    [InlineData("using var source = new CancellationTokenSource(-1);")]
    [InlineData("source.CancelAfter(Timeout.InfiniteTimeSpan);")]
    [InlineData("using var source = new CancellationTokenSource();")]
    [InlineData("using var source = new CancellationTokenSource( );")]
    [InlineData("Should.NotThrow(() => new CancellationTokenSource(fits + grace).Dispose());")]
    [InlineData("using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);")]
    [InlineData("deadline.CancelAfter(TimeSpan.FromSeconds(30)); // cancel: bound")]
    [InlineData("using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));  // cancel: bound")]
    [InlineData("// source.CancelAfter(TimeSpan.FromSeconds(1));")]
    [InlineData("/// <see cref=\"CancellationTokenSource.CancelAfter(TimeSpan)\"/> moves a deadline")]
    public void The_scan_leaves_the_rest_alone(string line)
        => Kinds(line).ShouldBeEmpty();

    [Fact]
    public void Every_real_time_mode_on_one_line_is_counted()
        => Kinds("var clock = new ManualClock { SystemTimersWithin = poll, AutoFireWithin = poll };")
            .ShouldBe([RealTimersKind, RealTimersKind]);

    [Fact]
    public void Every_timed_token_on_one_line_is_counted()
        => Kinds("using var a = new CancellationTokenSource(span); a.CancelAfter(span);")
            .ShouldBe([TimedCancelKind, TimedCancelKind]);

    [Theory]
    [InlineData("var source = new CancellationTokenSource(); // cancel: bound")]
    [InlineData("var source = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock); // cancel: bound")]
    [InlineData("var source = CancellationTokenSource.CreateLinkedTokenSource(token); // cancel: bound")]
    public void A_failure_bound_marker_on_a_line_without_a_timed_token_is_stale(string line)
        => StaleMarkers(line).ShouldBe(1);

    [Theory]
    [InlineData("deadline.CancelAfter(TimeSpan.FromSeconds(30)); // cancel: bound")]
    [InlineData("using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // cancel: bound")]
    [InlineData("deadline.CancelAfter(TimeSpan.FromSeconds(30));")]
    [InlineData("var source = new CancellationTokenSource();")]
    public void A_failure_bound_marker_on_a_timed_token_is_not_stale(string line)
        => StaleMarkers(line).ShouldBe(0);

    [Fact]
    public void The_failure_bound_markers_in_the_tree_sit_on_timed_tokens()
        => StaleMarkerSites(RepositoryRoot).ShouldBeEmpty(
            customMessage: "A '// cancel: bound' marker sits on a line that makes no timed token. The site was converted: " +
                           "remove the marker.");

    [Fact]
    public void A_stale_failure_bound_marker_in_a_file_is_reported_with_its_line()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-time-ratchet");

        try
        {
            var file = Path.Combine(directory.FullName, "tests", "Stale.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(
                file,
                "using var ok = new CancellationTokenSource(span); // cancel: bound\n" +
                "using var gone = new CancellationTokenSource(); // cancel: bound\n");

            StaleMarkerSites(directory.FullName).ShouldBe(["tests/Stale.cs:2"]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

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

    [Fact]
    public void The_condition_wait_may_hold_its_own_stopwatch_and_nothing_else()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-time-ratchet");

        try
        {
            var wait = Path.Combine(directory.FullName, ConditionWaitFile);
            var other = Path.Combine(directory.FullName, "tests", "Other.cs");

            Directory.CreateDirectory(Path.GetDirectoryName(wait)!);
            File.WriteAllText(
                wait,
                "var elapsed = Stopwatch.StartNew();\n" +
                "await Task.Delay(PollInterval, token); // delay: poll\n" +
                "await Task.Delay(PollInterval, token);\n" +
                "await Task.Delay(PollInterval, token); // delay: simulated\n" +
                "Thread.Sleep(5);\n" +
                "var again = Stopwatch.GetTimestamp();\n");
            File.WriteAllText(other, "var elapsed = Stopwatch.StartNew();\n");

            // The stopwatch of the wait is exempt (both of its lines); a delay other than the poll, a sleep and another
            // file's stopwatch are not.
            Scan(directory.FullName).ShouldBe(
                Counts(
                    ("delay:simulated|tests/Shared/Waiting/WaitUntil.cs", 1),
                    ("delay:untagged|tests/Shared/Waiting/WaitUntil.cs", 1),
                    ("sleep|tests/Shared/Waiting/WaitUntil.cs", 1),
                    ("stopwatch|tests/Other.cs", 1)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_condition_wait_exemption_is_per_kind_and_per_file()
    {
        IsExempt(StopwatchKind, ConditionWaitFile).ShouldBeTrue();
        IsExempt(StopwatchKind, "tests/Shared/Waiting/Other.cs").ShouldBeFalse();
        IsExempt(StopwatchKind, StandardClockFile).ShouldBeFalse();

        foreach (var kind in (string[])["sleep", "elapsed-assert", "delay:untagged", "delay:simulated", TimedCancelKind, RealTimersKind])
        {
            IsExempt(kind, ConditionWaitFile).ShouldBeFalse(kind);
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

        foreach (var (relative, file) in SourceFiles(root))
        {
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

        return counts;
    }

    private static IEnumerable<(string Relative, string Path)> SourceFiles(string root)
    {
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

                yield return (relative, file);
            }
        }
    }

    /// <summary>The <c>path:line</c> of every failure-bound marker that sits on a line making no timed token.</summary>
    private static List<string> StaleMarkerSites(string root)
    {
        var sites = new List<string>();

        foreach (var (relative, file) in SourceFiles(root))
        {
            var number = 0;

            foreach (var line in File.ReadLines(file))
            {
                number++;

                if (StaleMarkers(line) > 0)
                {
                    sites.Add($"{relative}:{number}");
                }
            }
        }

        return sites;
    }

    private static int StaleMarkers(string line)
        => FailureBound().IsMatch(line) && TimedCancels(line) == 0 ? 1 : 0;

    private static bool IsExempt(string kind, string relative)
        => kind switch
        {
            TimeProviderSubclassKind => string.Equals(relative, StandardClockFile, StringComparison.Ordinal),
            RealTimersKind => string.Equals(relative, StandardClockFile, StringComparison.Ordinal)
                || string.Equals(relative, StandardClockTestsFile, StringComparison.Ordinal),

            // WaitUntil's own bound is the one stopwatch whose verdict does not depend on it: the limit only ends a wait that
            // already failed. Nothing else in that file and no other file's stopwatch is covered.
            StopwatchKind => string.Equals(relative, ConditionWaitFile, StringComparison.Ordinal),
            _ => false,
        };

    /// <summary>How many timed tokens the line makes: <c>CancelAfter</c> and a source built with a span and no time source.</summary>
    private static int TimedCancels(string line)
        => IsComment(line) ? 0 : CancelAfterCall().Count(line) + TimedTokenSource().Count(line);

    private static bool IsComment(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*');
    }

    private static List<string> Kinds(string line)
    {
        var kinds = new List<string>();

        if (IsComment(line))
        {
            return kinds;
        }

        AddMatches(kinds, line, Sleep(), "sleep");

        // A measured duration that is the contract of the test says so in words and is listed; it is not debt.
        if (RealTimeReason(line) is null)
        {
            AddMatches(kinds, line, ElapsedAssertion(), "elapsed-assert");
            AddMatches(kinds, line, StopwatchUse(), "stopwatch");
        }

        AddMatches(kinds, line, TimeProviderSubclass(), TimeProviderSubclassKind);
        AddMatches(kinds, line, ManualClockRealTimers(), RealTimersKind);

        if (!FailureBound().IsMatch(line))
        {
            for (var count = TimedCancels(line); count > 0; count--)
            {
                kinds.Add(TimedCancelKind);
            }
        }

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
        @"\.CancelAfter\((?!\s*(?:Timeout\.Infinite(?:TimeSpan)?|-1)\b)",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex CancelAfterCall();

    // One argument, no more: a second one is the time source, and a source on a ManualClock waits for nothing. An argument may
    // nest two levels of parentheses (a span built from a call that takes a call). A source disposed in the same expression
    // only checks a range and never arms a timer for long.
    [GeneratedRegex(
        @"\bnew\s+CancellationTokenSource\((?!\s*\))(?>\s*)(?!Timeout\.Infinite(?:TimeSpan)?\b|-1\b)" +
        @"(?:[^(),]|\((?:[^()]|\([^()]*\))*\))+\)(?!\s*\.Dispose\(\))",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex TimedTokenSource();

    [GeneratedRegex(@"//\s*cancel:\s*bound\s*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: Timeout.Infinite)]
    private static partial Regex FailureBound();

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
