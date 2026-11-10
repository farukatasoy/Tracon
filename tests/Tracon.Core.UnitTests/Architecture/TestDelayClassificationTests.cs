using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every <c>Task.Delay(</c> under <c>tests/</c> and in the contract package
/// <c>src/Tracon.Testing.Contracts.Xunit</c> says why it is not a wait for a state.
/// </summary>
/// <remarks>
/// <para>
/// A fixed delay before a POSITIVE assertion is a claim about the machine:
/// under a loaded full-solution run the state arrives later and the test fails
/// although the product is right. Six such failures were measured before
/// converted every delay that waited for a state to
/// <c>WaitUntil</c> (<c>tests/Shared/Waiting/WaitUntil.cs</c>). The delays
/// that remain are not waits, and each line carries a <c>// delay: &lt;class&gt;</c>
/// tag that says which kind it is:
/// </para>
/// <list type="bullet">
/// <item><c>poll</c> - the pause between two probes; only <c>WaitUntil</c> and the contract package's
/// <c>UntilAnswered</c> may poll, each by its exact file.</item>
/// <item><c>simulated</c> - a fake stands in for slow or hanging work (a tool body, a model, a sink).</item>
/// <item><c>product</c> - the elapsed time IS the claim under test (a lease, a tool's own timeout).</item>
/// <item><c>negative</c> - the window before "this did not happen", where no positive signal exists.</item>
/// <item><c>bound</c> - a failure bound raced against a task.</item>
/// <item><c>retry</c> - the pause before retrying an infrastructure call.</item>
/// <item><c>fixture</c> - source text a test compiles or analyzes; the test never runs it.</item>
/// </list>
/// <para>
/// An untagged delay fails here. A new wait for a state belongs in
/// <c>WaitUntil</c>, not behind a tag. The contract package ships to consumers, so its
/// sources are held to the same rule as the tests: it cannot grow a second polling loop.
/// </para>
/// </remarks>
public sealed class TestDelayClassificationTests
{
    private static readonly string[] Classes = ["poll", "simulated", "product", "negative", "bound", "retry", "fixture"];

    /// <summary>The roots the rule covers: the tests, and the shipped package whose contracts the tests inherit.</summary>
    private static readonly string[] ScanRoots = ["tests", "src/Tracon.Testing.Contracts.Xunit"];

    /// <summary>The only files allowed to poll: the shared condition wait and the contract package's own.</summary>
    private static readonly string[] PollingFiles =
    [
        "tests/Shared/Waiting/WaitUntil.cs",
        "src/Tracon.Testing.Contracts.Xunit/Internal/UntilAnswered.cs",
    ];

    /// <summary>This class's own source names the pattern in its samples.</summary>
    private static readonly string[] SkippedFiles =
    [
        "tests/Tracon.Core.UnitTests/Architecture/TestDelayClassificationTests.cs",
        "tests/Tracon.Core.UnitTests/Architecture/TestTimeRatchetTests.cs",
        "tests/Tracon.Core.UnitTests/Architecture/TestTimeRatchetTests.RealTimeSites.cs",
    ];

    private static readonly Regex Tag = new(
        @"//\s*delay:\s*(?<class>[a-z]+)\s*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void Every_Task_Delay_under_tests_and_the_contract_package_carries_a_known_class()
    {
        var violations = Scan(RepositoryRoot);

        violations.ShouldBeEmpty(
            customMessage: $"A Task.Delay under tests/ or in Tracon.Testing.Contracts.Xunit says nothing about why it is not a wait:{Environment.NewLine}" +
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

    [Theory]
    [InlineData("tests/Sample.cs")]
    [InlineData("tests/Other.cs")]
    [InlineData("tests/Shared/Waiting/Other.cs")]
    [InlineData("tests/Shared/Waiting/WaitUntil.cs.bak.cs")]
    [InlineData("tests/Elsewhere/Shared/Waiting/WaitUntil.cs")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/Contracts/RunStoreContract.cs")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/Internal/Other.cs")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/Internal/UntilAnswered.cs.bak.cs")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/WaitUntil.cs")]
    [InlineData("tests/Internal/UntilAnswered.cs")]
    public void Scan_reports_a_poll_outside_the_two_condition_waits(string relativePath)
    {
        var violations = ScanSample(relativePath, "await Task.Delay(20); // delay: poll");

        violations.ShouldHaveSingleItem().ShouldContain("only the shared condition waits poll");
    }

    [Theory]
    [InlineData("tests/Shared/Waiting/WaitUntil.cs")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/Internal/UntilAnswered.cs")]
    public void Scan_accepts_a_poll_in_a_condition_wait(string relativePath)
        => ScanSample(relativePath, "await Task.Delay(20); // delay: poll").ShouldBeEmpty();

    [Theory]
    [InlineData("tests/Other.cs")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/Internal/Other.cs")]
    [InlineData("src/Tracon.Testing.Contracts.Xunit/Contracts/RunStoreContract.cs")]
    public void Scan_reports_an_untagged_delay_in_both_roots(string relativePath)
        => ScanSample(relativePath, "await Task.Delay(200);").ShouldHaveSingleItem().ShouldContain("no \"// delay: <class>\" tag");

    [Fact]
    public void Scan_reports_an_untagged_delay_inside_a_condition_wait()
        => ScanSample("tests/Shared/Waiting/WaitUntil.cs", "await Task.Delay(200);")
            .ShouldHaveSingleItem().ShouldContain("no \"// delay: <class>\" tag");

    [Fact]
    public void Scan_leaves_the_rest_of_the_package_sources_alone()
        => ScanSample("src/Tracon.Core/Other.cs", "await Task.Delay(200);").ShouldBeEmpty();

    [Fact]
    public void Scan_ignores_a_delay_named_inside_a_comment()
        => ScanSample("// a fixed await Task.Delay(200) used to stand here").ShouldBeEmpty();

    private static List<string> ScanSample(string line) => ScanSample("tests/Sample.cs", line);

    private static List<string> ScanSample(string relativePath, string line)
    {
        var directory = Directory.CreateTempSubdirectory("tracon-delay-test");

        try
        {
            var file = Path.Combine(directory.FullName, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(
                file,
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

        foreach (var scanRoot in ScanRoots)
        {
            var absoluteRoot = Path.Combine(root, scanRoot);

            if (!Directory.Exists(absoluteRoot))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories))
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
                             !PollingFiles.Contains(relative, StringComparer.Ordinal))
                    {
                        violations.Add($"{where}: only the shared condition waits poll - use WaitUntil instead of a private loop");
                    }
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
