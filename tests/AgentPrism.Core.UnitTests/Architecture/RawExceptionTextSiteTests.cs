using System.Text;
using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the raw-exception-text ratchet: every place <c>src/</c> writes a
/// caught, non-<see cref="AgentPrismException"/> exception's own text toward a
/// persistent field or an external response has to be named, in a tracked
/// baseline, with a reason for why the site is safe.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards was closed once before in exactly one place
/// (<c>HATA-S3-006</c>, the <c>IRunInputStore</c> path) and reopened in 21
/// others (Phase 119, BL-027/BL-037): a foreign exception's message can carry
/// a request detail, an internal URL, a <c>host:port</c>, or a partial
/// credential the exception's own producer put there, and nothing short of a
/// human reviewing every new write site catches the next one.
/// </para>
/// <para>
/// This is a ratchet, not a snapshot - the same contract as
/// <see cref="AmbientWriteSiteTests"/>: <c>raw-exception-text-baseline.txt</c>
/// lists every <c>&lt;path&gt;:&lt;method&gt;</c> the scan finds today, one
/// entry per method. A NEW entry fails the test; a LOST entry fails it too, so
/// the baseline cannot go stale in the other direction. Refresh after a
/// reviewed change:
/// <c>AGENTPRISM_RAW_EXCEPTION_TEXT_REFRESH=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release</c>.
/// </para>
/// <para>
/// The scan is line-based, not symbol resolution, and deliberately narrow: it
/// flags a <c>.Message</c> reference textually inside a <c>catch (Exception</c>
/// block (with or without a <c>when</c> filter) - the shape every one of the 21
/// vakas had. A <c>catch</c> naming a specific, non-<see cref="AgentPrismException"/>
/// type (for example <c>catch (HttpRequestException ex)</c>) is NOT the shape
/// this scan targets; those sites were reviewed individually during Phase 119
/// and are expected to route through <c>SafeErrorText</c> or a narrower,
/// already-reviewed exemption rather than appear here. The block's end is
/// found by indentation (this repo's Allman brace style keeps a block's
/// closing brace at the same indent as the <c>catch</c> line that opens it),
/// not by full brace-balance tracking, so a `$"...{ }..."` interpolation
/// inside the block cannot confuse it the way brace counting would.
/// </para>
/// </remarks>
public sealed class RawExceptionTextSiteTests
{
    private const string RefreshEnvVar = "AGENTPRISM_RAW_EXCEPTION_TEXT_REFRESH";

    private static readonly Regex CatchException = new(
        @"^\s*catch\s*\(\s*Exception\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AccessModifierLine = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:public|private|protected|internal)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex MethodNameBeforeParen = new(
        @"(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>(]*>)?\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Raw_exception_text_sites_match_the_baseline()
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
                failures.Add($"+ {site}: new raw-exception-text site, not in the baseline");
            }
        }

        foreach (var site in baseline.Keys)
        {
            if (!actual.Contains(site))
            {
                failures.Add($"- {site}: no longer writes raw exception text, refresh the baseline");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The raw-exception-text baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"A site that is genuinely safe (a caught exception whose OWN message is ours, or one " +
                           $"that never reaches a persistent field or external response) is refreshed into the " +
                           $"baseline with a reason of at least 40 characters. A new leak is closed with " +
                           $"SafeErrorText.ForPersistence instead: " +
                           $"{RefreshEnvVar}=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release");
    }

    /// <summary>
    /// Regression coverage for the scan itself, isolated from the real
    /// repository tree: a <c>catch (Exception</c> block that references
    /// <c>.Message</c> has to be reported as one site, named by the method
    /// that encloses the <c>catch</c>.
    /// </summary>
    [Fact]
    public void Scan_reports_a_method_whose_catch_block_references_Message()
    {
        var directory = Directory.CreateTempSubdirectory("agentprism-raw-exception-text-test");

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
                        try
                        {
                            Explode();
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            Store(exception.Message);
                        }
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

    /// <summary>
    /// A <c>catch</c> naming a specific, non-<c>Exception</c> type is not the
    /// shape this scan targets, even when it references <c>.Message</c>.
    /// </summary>
    [Fact]
    public void Scan_ignores_a_catch_block_naming_a_specific_type()
    {
        var directory = Directory.CreateTempSubdirectory("agentprism-raw-exception-text-test");

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
                        try
                        {
                            Explode();
                        }
                        catch (AgentPrismException exception)
                        {
                            Store(exception.Message);
                        }
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
                if (!CatchException.IsMatch(lines[i]))
                {
                    continue;
                }

                var blockEnd = FindBlockEnd(lines, i, IndentOf(lines[i]));
                var referencesMessage = false;

                for (var j = i; j <= blockEnd && j < lines.Length; j++)
                {
                    var trimmed = lines[j].TrimStart();

                    if (trimmed.StartsWith("///", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (lines[j].Contains(".Message", StringComparison.Ordinal))
                    {
                        referencesMessage = true;
                        break;
                    }
                }

                if (!referencesMessage)
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

    private static int IndentOf(string line)
    {
        var count = 0;

        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Finds the line index of the <c>catch</c> block's closing brace. This
    /// repo's Allman brace style keeps a block's opening AND closing brace at
    /// the same indent as the statement that opens it, so the search does not
    /// need full brace-balance tracking - it only has to find the first line
    /// below the <c>catch</c> whose trimmed content is <c>}</c> at the same
    /// indent. If the shape is not found (a formatting style this scan does
    /// not expect), it falls back to the end of the file, which only makes
    /// the scan MORE inclusive, never less.
    /// </summary>
    private static int FindBlockEnd(string[] lines, int catchLineIndex, int catchIndent)
    {
        // A `catch` clause's `when (...)` filter can span several lines (a multi-condition
        // guard, one clause per line); the opening brace is whichever line after those is
        // just "{". The search is capped so a catch this scan does not expect (no filter,
        // no brace at all) falls through to the inclusive-fallback below instead of running away.
        var i = catchLineIndex + 1;
        var scanned = 0;

        while (i < lines.Length && !string.Equals(lines[i].Trim(), "{", StringComparison.Ordinal) && scanned < 20)
        {
            i++;
            scanned++;
        }

        if (i >= lines.Length || !string.Equals(lines[i].Trim(), "{", StringComparison.Ordinal))
        {
            return lines.Length - 1;
        }

        i++;

        while (i < lines.Length)
        {
            if (string.Equals(lines[i].Trim(), "}", StringComparison.Ordinal) && IndentOf(lines[i]) == catchIndent)
            {
                return i;
            }

            i++;
        }

        return lines.Length - 1;
    }

    /// <summary>
    /// Walks upward from a catch site to the nearest signature line. Line-based
    /// on purpose - a symbol-resolving scan would need a full compilation, and
    /// this gate has to stay fast and dependency-free.
    /// </summary>
    private static string? FindEnclosingMethod(string[] lines, int catchLineIndex)
    {
        for (var i = catchLineIndex; i >= 0; i--)
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

        builder.AppendLine("# Generated by RawExceptionTextSiteTests. Refresh:");
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/AgentPrism.Core.UnitTests -c Release");
        builder.AppendLine("# One line per <path>:<method>. The reason is at least 40 characters and explains");
        builder.AppendLine("# why the site is safe (a caught exception whose OWN message is ours, or one that");
        builder.AppendLine("# never reaches a persistent field or external response). New entries this refresh");
        builder.AppendLine("# added carry a placeholder reason that MUST be replaced by hand.");

        foreach (var site in actual)
        {
            var reason = existing.GetValueOrDefault(site, "REPLACE ME - explain why this site is safe, in 40+ characters");
            builder.Append(site).Append(" | ").Append(reason).AppendLine();
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "AgentPrism.Core.UnitTests",
        "Architecture",
        "raw-exception-text-baseline.txt");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for AgentPrism.slnx.");
    }
}
