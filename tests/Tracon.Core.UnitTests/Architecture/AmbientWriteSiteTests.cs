using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the ambient-write-site ratchet: every place <c>src/</c> writes
/// through an <see cref="AsyncLocal{T}"/> ambient scope has to be named, in a
/// tracked baseline, with a reason for why the write site is safe.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards repeated four times (phase 6, 11, 12, 15): an
/// <see cref="AsyncLocal{T}"/> assignment made inside an <c>async</c> helper
/// method, or before a streaming loop instead of inside it, does not flow to
/// the caller and does not survive a <c>yield return</c> boundary. Nothing
/// short of a human reading every new write site catches the next one; a
/// coding assistant reading <c>MEMORY.md</c> is a hope, not a gate.
/// </para>
/// <para>
/// This is a ratchet, not a snapshot - the same contract as
/// <see cref="SourceLanguageTests"/>: <c>ambient-write-baseline.txt</c> lists
/// every <c>&lt;path&gt;:&lt;method&gt;</c> the scan finds today, one entry
/// per method (not per line - the same method can write more than once, most
/// often the loop-repeats-the-write pattern APG0501 checks at compile time).
/// A NEW entry fails the test; a LOST entry fails it too, so the baseline
/// cannot go stale in the other direction. Refresh after a reviewed change:
/// <c>TRACON_AMBIENT_WRITE_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// <para>
/// The write pattern matched here is deliberately the same plain substring
/// search the phase 93 doc's own verification command uses
/// (<c>grep -rn "SetCurrent(\|AmbientTenantScope.Begin(\|..." src/</c>), not a
/// symbol resolution: this is a fast, dependency-free repository gate, and it
/// has to report the identical count that command reports, or the two drift
/// apart the first time either one changes.
/// </para>
/// </remarks>
public sealed class AmbientWriteSiteTests
{
    private const string RefreshEnvVar = "TRACON_AMBIENT_WRITE_REFRESH";

    private static readonly string[] WriteMarkers =
    [
        "SetCurrent(",
        "AmbientTenantScope.Begin(",
        "AmbientRunAttributionScope.Begin(",
        "StartActivity(",
    ];

    private static readonly Regex AccessModifierLine = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The last identifier on a signature line that is immediately followed
    /// by <c>(</c> - the method name, whether the return type is a simple
    /// name or a generic one (<c>IAsyncEnumerable&lt;T&gt;</c>).
    /// </summary>
    private static readonly Regex MethodNameBeforeParen = new(
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>(]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Ambient_write_sites_match_the_baseline()
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

        foreach (var site in actual)
        {
            if (!baseline.ContainsKey(site))
            {
                failures.Add($"+ {site}: new ambient write site, not in the baseline");
            }
        }

        foreach (var site in baseline.Keys)
        {
            if (!actual.Contains(site))
            {
                failures.Add($"- {site}: no longer writes ambient state, refresh the baseline");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The ambient-write-site baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"A write site that is genuinely safe (see MEMORY.md's AsyncLocal rule) is refreshed " +
                           $"into the baseline with a reason of at least 40 characters: " +
                           $"{RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    /// <summary>
    /// Regression coverage for the scan itself, isolated from the real
    /// repository tree: a method that writes ambient state has to be reported
    /// as one site, named by the method that encloses it.
    /// </summary>
    [Fact]
    public void Scan_reports_a_method_that_writes_ambient_state()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-ambient-write-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """
                namespace Sample;

                public sealed class Widget
                {
                    private void DoWork()
                    {
                        TraconRunContext.SetCurrent(null);
                    }
                }
                """);

            var actual = Scan(directory.FullName);

            actual.ShouldContain(site => string.Equals(site, "Sample.cs:DoWork", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>A doc-comment reference to the API is not a write site.</summary>
    [Fact]
    public void Scan_ignores_a_reference_inside_a_doc_comment()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-ambient-write-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Sample.cs"),
                """
                namespace Sample;

                public sealed class Widget
                {
                    /// <summary>See <see cref="TraconRunContext.SetCurrent(AgentRunScope)"/>.</summary>
                    private void DoWork()
                    {
                    }
                }
                """);

            Scan(directory.FullName).ShouldBeEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SortedSet<string> Scan(string root)
    {
        var sites = new SortedSet<string>(StringComparer.Ordinal);
        var srcRoot = Path.Combine(root, "src");
        var scanRoot = Directory.Exists(srcRoot) ? srcRoot : root;

        foreach (var file in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();

                if (trimmed.StartsWith("///", StringComparison.Ordinal) || !ContainsWriteMarker(lines[i]))
                {
                    continue;
                }

                var method = FindEnclosingMethod(lines, i);

                if (method is not null)
                {
                    sites.Add($"{relative}:{method}");
                }
            }
        }

        return sites;
    }

    private static bool ContainsWriteMarker(string line)
    {
        foreach (var marker in WriteMarkers)
        {
            if (line.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks upward from a write site to the nearest signature line. Line-based
    /// on purpose - a symbol-resolving scan would need a full compilation, and
    /// this gate has to stay fast and dependency-free. A write site inside a
    /// lambda or a local function is attributed to the nearest enclosing member
    /// above it, which is enough to point a reviewer at the right method.
    /// </summary>
    private static string? FindEnclosingMethod(string[] lines, int writeLineIndex)
    {
        for (var i = writeLineIndex; i >= 0; i--)
        {
            var line = lines[i];

            if (!AccessModifierLine.IsMatch(line) || !line.Contains('(', StringComparison.Ordinal))
            {
                continue;
            }

            var match = MethodNameBeforeParen.Match(line);

            if (match.Success)
            {
                return match.Groups["name"].Value;
            }
        }

        return null;
    }

    private static Dictionary<string, string> ReadBaseline()
    {
        var baseline = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(BaselinePath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('|');

            separator.ShouldBeGreaterThan(0, $"Malformed baseline line: '{line}'");

            var site = line[..separator].Trim();
            var reason = line[(separator + 1)..].Trim();

            reason.Length.ShouldBeGreaterThanOrEqualTo(
                40,
                $"'{site}' carries a reason shorter than 40 characters: '{reason}'.");

            baseline[site] = reason;
        }

        return baseline;
    }

    private static void WriteBaseline(SortedSet<string> actual)
    {
        var existing = File.Exists(BaselinePath) ? ReadBaseline() : [];
        var builder = new StringBuilder();

        builder.AppendLine("# Generated by AmbientWriteSiteTests. Refresh:");
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
        builder.AppendLine("# One line per <path>:<method>. The reason is at least 40 characters and explains");
        builder.AppendLine("# why the write site is safe (see MEMORY.md's AsyncLocal rule). New entries this");
        builder.AppendLine("# refresh added carry a placeholder reason that MUST be replaced by hand.");

        foreach (var site in actual)
        {
            var reason = existing.GetValueOrDefault(site, "REPLACE ME - explain why this write site is safe, in 40+ characters");
            builder.Append(site).Append(" | ").Append(reason).AppendLine();
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "ambient-write-baseline.txt");

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
