using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every <c>Task.Delay(</c> under <c>tests/</c> says why it is not a wait for
/// a state (Phase 184).
/// </summary>
/// <remarks>
/// <para>
/// A fixed delay before a POSITIVE assertion is a claim about the machine:
/// under a loaded full-solution run the state arrives later and the test fails
/// although the product is right. Six such failures were measured before
/// Phase 184 converted every delay that waited for a state to
/// <c>WaitUntil</c> (<c>tests/Shared/Waiting/WaitUntil.cs</c>). The delays
/// that remain are not waits, and each line carries a <c>// delay: &lt;class&gt;</c>
/// tag that says which kind it is:
/// </para>
/// <list type="bullet">
/// <item><c>poll</c> - the pause between two probes; only <c>WaitUntil</c> itself may poll.</item>
/// <item><c>simulated</c> - a fake stands in for slow or hanging work (a tool body, a model, a sink).</item>
/// <item><c>product</c> - the elapsed time IS the claim under test (a lease, a tool's own timeout).</item>
/// <item><c>negative</c> - the window before "this did not happen", where no positive signal exists.</item>
/// <item><c>bound</c> - a failure bound raced against a task.</item>
/// <item><c>retry</c> - the pause before retrying an infrastructure call.</item>
/// <item><c>fixture</c> - source text a test compiles or analyzes; the test never runs it.</item>
/// </list>
/// <para>
/// An untagged delay fails here. A new wait for a state belongs in
/// <c>WaitUntil</c>, not behind a tag.
/// </para>
/// </remarks>
public sealed class TestDelayClassificationTests
{
    private static readonly string[] Classes = ["poll", "simulated", "product", "negative", "bound", "retry", "fixture"];

    /// <summary>The one file allowed to poll: the shared condition wait.</summary>
    private const string PollingFile = "tests/Shared/Waiting/WaitUntil.cs";

    /// <summary>This class's own source names the pattern in its samples.</summary>
    private static readonly string[] SkippedFiles =
        ["tests/Tracon.Core.UnitTests/Architecture/TestDelayClassificationTests.cs"];

    private static readonly Regex Tag = new(
        @"//\s*delay:\s*(?<class>[a-z]+)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Every_Task_Delay_under_tests_carries_a_known_class()
    {
        var violations = Scan(RepositoryRoot);

        violations.ShouldBeEmpty(
            customMessage: $"A Task.Delay under tests/ says nothing about why it is not a wait:{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, violations)}{Environment.NewLine}" +
                           "Wait for the state with WaitUntil; if it is not a wait, add \"// delay: <class>\" " +
                           $"with one of: {string.Join(", ", Classes)} (see the class remarks).");
    }

    [Fact]
    public void Scan_reports_an_untagged_delay()
    {
        var violations = ScanSample("await Task.Delay(200);");

        violations.ShouldHaveSingleItem().ShouldContain("Sample.cs:5");
    }

    [Fact]
    public void Scan_reports_an_unknown_class()
    {
        var violations = ScanSample("await Task.Delay(200); // delay: settle");

        violations.ShouldHaveSingleItem().ShouldContain("'settle'");
    }

    [Fact]
    public void Scan_accepts_a_known_class()
        => ScanSample("await Task.Delay(Timeout.InfiniteTimeSpan, token); // delay: simulated").ShouldBeEmpty();

    [Fact]
    public void Scan_reports_a_poll_outside_the_shared_condition_wait()
    {
        var violations = ScanSample("await Task.Delay(20); // delay: poll");

        violations.ShouldHaveSingleItem().ShouldContain("only WaitUntil polls");
    }

    [Fact]
    public void Scan_ignores_a_delay_named_inside_a_comment()
        => ScanSample("// a fixed await Task.Delay(200) used to stand here").ShouldBeEmpty();

    private static List<string> ScanSample(string line)
    {
        var directory = Directory.CreateTempSubdirectory("tracon-delay-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                $$"""
                public sealed class SampleTest
                {
                    public async Task RunAsync(CancellationToken token)
                    {
                        {{line}}
                    }
                }
                """);

            return Scan(directory.FullName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static List<string> Scan(string root)
    {
        var violations = new List<string>();
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

            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith('*') ||
                    !line.Contains("Task.Delay(", StringComparison.Ordinal))
                {
                    continue;
                }

                var where = $"{relative}:{index + 1}";
                var tag = Tag.Match(line);

                if (!tag.Success)
                {
                    violations.Add($"{where}: no \"// delay: <class>\" tag");
                }
                else if (!Classes.Contains(tag.Groups["class"].Value, StringComparer.Ordinal))
                {
                    violations.Add($"{where}: unknown class '{tag.Groups["class"].Value}'");
                }
                else if (string.Equals(tag.Groups["class"].Value, "poll", StringComparison.Ordinal) &&
                         !string.Equals(relative, PollingFile, StringComparison.Ordinal))
                {
                    violations.Add($"{where}: only WaitUntil polls - use it instead of a private loop");
                }
            }
        }

        return violations;
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
