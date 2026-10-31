using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the second half of the documentation boundary: the public tree is
/// self-contained. A reader who holds only the public tree finds no pointer to a
/// development record they were never given.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourceLanguageTests"/> settled the language question — a shipped
/// document is English. This one settles the audience question. Every file of the
/// public tree (<c>scripts/kamu-agaci.txt</c>) may point only at things the reader
/// holds: the package's own types, members, configuration keys, HTTP paths, MSBuild
/// properties, the contract directories <c>docs/sozlesme/</c> and
/// <c>docs/openapi/</c>, and the documentation site. It may not point at the
/// development record: phase numbers, decision, candidate and defect ids, the
/// agent folder, the memory file, or the Turkish journal under <c>docs/</c>.
/// </para>
/// <para>
/// A comment is part of the tree. A <c>//</c> line reaches a public reader the same
/// way a <c>&lt;summary&gt;</c> does, and the reader does not hold the ledger the
/// reference points at. The link between code and ledger runs one way: the ledger
/// names a file and a symbol, the code does not name the ledger.
/// </para>
/// <para>
/// Text that ships to a consumer is held to a second rule about voice. The
/// documentation-site generators already strip alarm emoji, <c>Rationale:</c>
/// openers and <c>Measured (…)</c> blocks before publishing, which is the project's
/// own judgement that they do not belong on a consumer surface. The package ships
/// the unstripped text, so the same judgement is enforced at the source for
/// <c>///</c> blocks, package READMEs, template content, MSBuild imports, the
/// licence files and the packed OpenAPI document.
/// </para>
/// <para>
/// This is a ratchet, not a snapshot, and deliberately mirrors
/// <see cref="SourceLanguageTests"/> line for line:
/// <c>shipped-documentation-baseline.txt</c> records how many offending lines each
/// file may still have, the test fails when a file gains lines or when a file
/// loses them without the baseline being refreshed, so the debt can only shrink.
/// </para>
/// <para>
/// Refresh the baseline after a clean-up pass:
/// <c>TRACON_SHIPPED_DOCS_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class ShippedDocumentationSelfContainmentTests
{
    private const string RefreshEnvVar = "TRACON_SHIPPED_DOCS_REFRESH";

    /// <remarks>Declared first: a static initialiser below reads it.</remarks>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// References into the development record. The reader holds none of these, so a
    /// sentence that leans on one leaves them with a gap.
    /// </summary>
    /// <remarks>
    /// The pattern is read from <c>docs-site/scripts/internal-history.pattern</c>,
    /// which the three site generators read as well. It was copied into each of them
    /// once and the copies drifted: a reference that passed this gate then failed the
    /// API-reference generator. One file, one definition.
    /// </remarks>
    /// <remarks>
    /// Lazy on purpose: a static field initialiser runs in declaration order, and
    /// <c>RepositoryRoot</c> is declared below. Reading the file eagerly here throws
    /// <c>TypeInitializationException</c>.
    /// </remarks>
    private static readonly Lazy<Regex> InternalReferenceRegex = new(() => new Regex(
        File.ReadAllText(Path.Combine(RepositoryRoot, "docs-site", "scripts", "internal-history.pattern")).Trim(),
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(5)));

    private static Regex InternalReferencePattern => InternalReferenceRegex.Value;

    /// <summary>
    /// The memory file of the development repository. It is matched here, case-sensitively,
    /// rather than in the shared pattern: that pattern ignores case, and a file called
    /// <c>memory.md</c> is an ordinary name in the session-workspace tests and the site.
    /// </summary>
    private static readonly Regex MemoryFilePattern = new(
        @"(?<![\w./-])MEMORY\.md\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// The development journal's voice. Alarm emoji, a <c>Rationale:</c> opener and
    /// a dated measurement are notes to the next maintainer, not API documentation.
    /// </summary>
    private static readonly Regex InternalVoicePattern = new(
        // 'Rationale:' and 'Decision:' are the journal's labels and are capitalised;
        // lower-case 'rationale' and 'decision' are ordinary English and stay. Only
        // the dated measurement is matched either way.
        @"🚨|⚠️|\bRationale:|\bDecision:\s|(?i:\bmeasured \(20\d\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// A comment marker at the start of a line. Removed from the second line of a pair
    /// before the pair is joined, so a reference that wraps across two comment lines
    /// reads as one.
    /// </summary>
    private static readonly Regex LeadingCommentMarker = new(
        @"^\s*(?:///?|#+|\*|--|>)\s?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// Everything below here is a private root: it is in the development repository and
    /// never in the public tree. <c>docs/</c> is private except the two directories the
    /// tree list names, which are checked first.
    /// </summary>
    private static readonly string[] PrivateRoots =
    [
        "docs/",
        ".agents/",
        ".claude/",
        "AGENTS.md",
        "CLAUDE.md",
        "MEMORY.md",
        ".mcp.json",
        "skills-lock.json",
    ];

    /// <summary>
    /// Files that name every forbidden pattern in order to define or test it, and a workflow that is
    /// withheld from the public tree: it has to name a private path to run it, and no public reader
    /// ever receives it.
    /// </summary>
    private static readonly string[] SkippedFiles =
    [
        "tests/Tracon.Core.UnitTests/Architecture/ShippedDocumentationSelfContainmentTests.cs",
        "docs-site/scripts/internal-history.pattern",
        ".github/workflows/ayna.yml",
    ];

    /// <summary>
    /// The lower-case forms that look like a reference but are ordinary code: a utility
    /// class such as <c>mt-4</c>, a configuration key such as <c>k1</c> and a file name such as
    /// <c>hata-kodlari.txt</c>. The ids the pattern exists for are upper-case.
    /// </summary>
    private static readonly Regex CodeTokenPattern = new(
        @"^(?:k[1-4]|mt-.*|hata-.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Shipped_documentation_points_only_at_what_the_consumer_holds()
    {
        var actual = Scan();

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
                failures.Add($"+ {path}: {count} offending lines, baseline allows {allowed}");
            }
        }

        foreach (var (path, allowed) in baseline)
        {
            var count = actual.GetValueOrDefault(path, 0);

            if (count < allowed)
            {
                failures.Add($"- {path}: {count} offending lines, baseline still allows {allowed} — refresh it");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The shipped-documentation baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"Lines that only lost debt are fixed by refreshing: {RefreshEnvVar}=1 " +
                           "dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    [Fact]
    public void The_public_tree_list_accounts_for_every_tracked_file()
    {
        var entries = ReadTreeList(TreeListPath);
        var unaccounted = TrackedFiles()
            .Where(path => !IsPublic(path, entries) && !IsPrivate(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        unaccounted.ShouldBeEmpty(
            customMessage: "These tracked files are neither in scripts/kamu-agaci.txt nor under a private root, " +
                           "so no gate scans them and the mirror would drop them. Add each one to the list " +
                           $"(public) or to PrivateRoots (private):{Environment.NewLine}" +
                           string.Join(Environment.NewLine, unaccounted));
    }

    [Fact]
    public void The_scan_reads_every_public_root_and_no_private_one()
    {
        var entries = ReadTreeList(TreeListPath);
        var scanned = EnumeratePublicFiles().Select(file => file.Relative).ToList();
        var tracked = TrackedFiles();

        scanned.ShouldNotContain(path => IsPrivate(path) && !IsPublic(path, entries));

        foreach (var entry in entries.Where(entry => entry.EndsWith('/')))
        {
            if (tracked.Any(path => path.StartsWith(entry, StringComparison.Ordinal)))
            {
                scanned.ShouldContain(
                    path => path.StartsWith(entry, StringComparison.Ordinal),
                    $"The public root '{entry}' has tracked files but the scan read none of them.");
            }
        }
    }

    [Fact]
    public void The_tree_list_skips_blank_and_comment_lines()
    {
        var entries = ParseTreeList(["# comment", "", "src/", "  README.md  ", "   ", "# src/ignored/", "Tracon.*.slnf"]);

        entries.ShouldBe(["src/", "README.md", "Tracon.*.slnf"]);
    }

    [Fact]
    public void A_missing_tree_list_fails_loudly()
    {
        var missing = Path.Combine(RepositoryRoot, "scripts", "no-such-tree-list.txt");

        var error = Should.Throw<InvalidOperationException>(() => ReadTreeList(missing));

        error.Message.ShouldContain("kamu-agaci.txt");
    }

    [Theory]
    [InlineData("src/Tracon.Core/Foo.cs", true)]
    [InlineData("docs/sozlesme/hata-kodlari.txt", true)]
    [InlineData("docs/openapi/tracon.json", true)]
    // The private paths are split: the stale-reference scan of the gate runner would read them as references.
    [InlineData("docs" + "/KARARLAR.md", false)]
    [InlineData("docs" + "/hafiza/00-INDEKS.md", false)]
    [InlineData("README.md", true)]
    [InlineData("Tracon.src.slnf", true)]
    [InlineData("Tracon.slnx", true)]
    [InlineData("AGENTS.md", false)]
    [InlineData("MEMORY.md", false)]
    [InlineData(".agents/skills/x/SKILL.md", false)]
    [InlineData("subdir/README.md", false)]
    public void The_tree_list_decides_what_is_public(string path, bool expected)
    {
        var entries = ParseTreeList(
            ["src/", "docs/sozlesme/", "docs/openapi/", "README.md", "Tracon.slnx", "Tracon.*.slnf"]);

        IsPublic(path, entries).ShouldBe(expected);
    }

    [Theory]
    [InlineData("// the retry loop (K-320 measured this position)")]
    [InlineData("/// Added in phase 65 for the queue.")]
    [InlineData("# see docs" + "/KARARLAR.md for the rationale")]
    [InlineData("description: \"Score given by a judge (Phase 49).\"")]
    [InlineData("// the agent skill lives in .agents/skills/faz-uygulama")]
    [InlineData("// the memory file MEMORY.md names this trap")]
    [InlineData("// recorded as HATA-S3-008 and MT-MCP-023")]
    [InlineData("// see section 53.4 of the plan")]
    [InlineData("// This phase widens the store.")]
    [InlineData("// 144.4: the option object is built by Tracon")]
    [InlineData("# 237.3 / 237.4 - what the report cannot see")]
    [InlineData("/// 21.1). The scope counts in the store")]
    [InlineData("/// <summary>135.1: a generic type is rejected.</summary>")]
    public void A_reference_into_the_development_record_is_flagged(string line)
    {
        CountOffendingLines([line], Rules.Reference).ShouldBe(1);
    }

    [Theory]
    [InlineData("see docs/sozlesme/hata-kodlari.txt for the codes")]
    [InlineData("the snapshot docs/openapi/tracon.json is packed")]
    [InlineData("see docs-site/src/content/docs/guides/x.md")]
    [InlineData("../src/content/docs/reference/y.md")]
    [InlineData("<div class=\"mt-4 mb-2\">")]
    [InlineData("var key = \"k1\";")]
    [InlineData("the snapshot hata-kodlari.txt lists the codes")]
    [InlineData("See https://tracon.dev/docs/guides/setup")]
    [InlineData("the phase of the moon")]
    [InlineData("an ordinary comment about retries")]
    [InlineData("// a contrast of 4.5:1 is the floor")]
    [InlineData("// one millisecond to about 24.8 days)")]
    [InlineData("// the window is 49.8 s long")]
    public void Text_the_public_reader_holds_is_not_flagged(string line)
    {
        CountOffendingLines([line], Rules.Reference).ShouldBe(0);
    }

    [Fact]
    public void A_reference_wrapped_across_two_comment_lines_is_flagged_on_both()
    {
        string[] lines =
        [
            "// the queue was reworked (phase",
            "// 65) to drop the lock",
            "// unrelated line",
        ];

        CountOffendingLines(lines, Rules.Reference).ShouldBe(2);
    }

    [Fact]
    public void The_voice_rule_applies_only_where_the_text_ships()
    {
        string[] lines = ["🚨 a note to the next maintainer", "plain text"];

        CountOffendingLines(lines, Rules.Reference).ShouldBe(0);
        CountOffendingLines(lines, Rules.Reference | Rules.VoiceEveryLine).ShouldBe(1);
    }

    [Fact]
    public void The_voice_rule_on_a_source_file_reads_documentation_comments_only()
    {
        string[] lines =
        [
            "// 🚨 an implementation comment stays",
            "/// <summary>Measured (2026-09-04): fast.</summary>",
        ];

        CountOffendingLines(lines, Rules.Reference | Rules.VoiceDocumentationComments).ShouldBe(1);
    }

    private static SortedDictionary<string, int> Scan()
    {
        var results = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (relative, absolute, rules) in EnumeratePublicFiles())
        {
            if (IsBinary(absolute))
            {
                continue;
            }

            var count = CountOffendingLines(File.ReadAllLines(absolute), rules);

            if (count > 0)
            {
                results[relative] = count;
            }
        }

        return results;
    }

    /// <summary>Every file of the public tree, with the rules that apply to it.</summary>
    private static IEnumerable<(string Relative, string Absolute, Rules Rules)> EnumeratePublicFiles()
    {
        var entries = ReadTreeList(TreeListPath);

        foreach (var relative in TrackedFiles())
        {
            if (!IsPublic(relative, entries) || SkippedFiles.Contains(relative, StringComparer.Ordinal))
            {
                continue;
            }

            var absolute = Path.Combine(RepositoryRoot, relative);

            if (File.Exists(absolute))
            {
                yield return (relative, absolute, RulesFor(relative));
            }
        }
    }

    /// <summary>
    /// The reference rule covers the whole tree. The voice rule covers what reaches a
    /// consumer: the project template's content becomes the consumer's OWN project and
    /// <c>buildTransitive/</c> is imported into every consumer build, both read by
    /// someone with no access to the development record; a package README is
    /// <c>PackageReadmeFile</c> and nuget.org renders it; <c>PackageLicenseFile</c> packs
    /// the licence files; the OpenAPI document is packaged into
    /// <c>Tracon.AspNetCore</c>; and <c>GenerateDocumentationFile</c> ships every
    /// <c>///</c> block as XML.
    /// </summary>
    private static Rules RulesFor(string relative)
    {
        var shippedWhole =
            relative.StartsWith("src/", StringComparison.Ordinal) &&
            (relative.Contains("/content/", StringComparison.Ordinal) ||
             relative.Contains("/buildTransitive/", StringComparison.Ordinal)) &&
            !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        var packageReadme =
            relative.StartsWith("src/", StringComparison.Ordinal) &&
            relative.EndsWith("/README.md", StringComparison.Ordinal) &&
            relative.Count(character => character == '/') == 2;

        var shippedFile = relative is "README.md" or "LICENSE.md" or "LICENSE-MIT.md" or "docs/openapi/tracon.json";

        if (shippedWhole || packageReadme || shippedFile)
        {
            return Rules.Reference | Rules.VoiceEveryLine;
        }

        if (relative.StartsWith("src/", StringComparison.Ordinal) &&
            relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return Rules.Reference | Rules.VoiceDocumentationComments;
        }

        return Rules.Reference;
    }

    /// <summary>
    /// Tracked files plus untracked ones that are not ignored, so a file added in the
    /// working tree is scanned before it is committed. Git decides what exists: a walk
    /// of the disk would read the generated and ignored trees the site build writes.
    /// </summary>
    private static List<string> TrackedFiles()
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        foreach (var argument in new[] { "ls-files", "--cached", "--others", "--exclude-standard", "-z" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start 'git'. The public-tree scan lists files with git.");

        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'git ls-files' failed with exit code {process.ExitCode}: {errors.GetAwaiter().GetResult()}");
        }

        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
    }

    private static string TreeListPath { get; } = Path.Combine(RepositoryRoot, "scripts", "kamu-agaci.txt");

    private static List<string> ReadTreeList(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"The public tree list '{path}' (scripts/kamu-agaci.txt) is missing. " +
                "The gate cannot tell which files are public without it, and it does not guess.");
        }

        return ParseTreeList(File.ReadAllLines(path));
    }

    private static List<string> ParseTreeList(IEnumerable<string> lines) =>
        lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#')
            .ToList();

    private static bool IsPublic(string relative, IReadOnlyList<string> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.EndsWith('/'))
            {
                if (relative.StartsWith(entry, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (!relative.Contains('/', StringComparison.Ordinal) && MatchesFileName(entry, relative))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesFileName(string entry, string name)
    {
        if (!entry.Contains('*', StringComparison.Ordinal))
        {
            return string.Equals(entry, name, StringComparison.Ordinal);
        }

        var expression = "^" + Regex.Escape(entry).Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(name, expression, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5));
    }

    private static bool IsPrivate(string relative) =>
        PrivateRoots.Any(root => root.EndsWith('/')
            ? relative.StartsWith(root, StringComparison.Ordinal)
            : string.Equals(root, relative, StringComparison.Ordinal));

    private static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[8000];
        var read = stream.Read(buffer, 0, buffer.Length);

        return Array.IndexOf(buffer, (byte)0, 0, read) >= 0;
    }

    /// <summary>
    /// Counts the lines that carry a reference or, where the rules ask for it, the
    /// wrong voice. A line is counted once, however many patterns it breaks.
    /// </summary>
    private static int CountOffendingLines(string[] lines, Rules rules)
    {
        var offending = new HashSet<int>();

        for (var index = 0; index < lines.Length; index++)
        {
            if (HasReference(lines[index]))
            {
                offending.Add(index);
            }
        }

        // A comment wraps at the source width, so "(phase 65)" is routinely split across
        // two lines and a line-by-line scan sees neither half. Measured: three such
        // references survived a per-line pass and were only caught when the site
        // generator joined the block.
        for (var index = 0; index + 1 < lines.Length; index++)
        {
            if (offending.Contains(index) && offending.Contains(index + 1))
            {
                continue;
            }

            if (ReferenceSpansBoundary(lines[index], lines[index + 1]))
            {
                offending.Add(index);
                offending.Add(index + 1);
            }
        }

        if (rules.HasFlag(Rules.VoiceEveryLine))
        {
            for (var index = 0; index < lines.Length; index++)
            {
                if (InternalVoicePattern.IsMatch(lines[index]))
                {
                    offending.Add(index);
                }
            }
        }

        if (rules.HasFlag(Rules.VoiceDocumentationComments))
        {
            AddOffendingDocumentationLines(lines, offending);
        }

        return offending.Count;
    }

    private static bool HasReference(string text)
    {
        if (MemoryFilePattern.IsMatch(text))
        {
            return true;
        }

        foreach (Match match in InternalReferencePattern.Matches(text))
        {
            if (!IsAllowed(text, match))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReferenceSpansBoundary(string first, string second)
    {
        var left = first.TrimEnd();
        var right = LeadingCommentMarker.Replace(second, string.Empty).Trim();

        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        var joined = left + " " + right;

        foreach (Match match in InternalReferencePattern.Matches(joined))
        {
            if (match.Index < left.Length && match.Index + match.Length > left.Length && !IsAllowed(joined, match))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A match the reader may hold: a lower-case code token, a path into one of the two
    /// contract directories, or a path inside the documentation site's own content tree
    /// (<c>…/content/docs/…</c>), which is not the repository's <c>docs/</c>.
    /// </summary>
    private static bool IsAllowed(string text, Match match)
    {
        if (CodeTokenPattern.IsMatch(match.Value))
        {
            return true;
        }

        var docsAt = match.Value.IndexOf("docs/", StringComparison.OrdinalIgnoreCase);

        if (docsAt < 0)
        {
            return false;
        }

        var path = match.Value[docsAt..];

        if (path.StartsWith("docs/sozlesme/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("docs/openapi/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return text.AsSpan(0, match.Index + docsAt).EndsWith("content/", StringComparison.Ordinal);
    }

    /// <summary>
    /// A documentation comment is inspected as a block rather than line by line, for
    /// the same reason as the pair check: the voice phrase "Measured (2026" wraps too.
    /// A match is attributed to the lines it spans so the baseline still counts lines.
    /// </summary>
    private static void AddOffendingDocumentationLines(string[] lines, HashSet<int> offending)
    {
        var block = new List<(int Line, string Text)>();

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].AsSpan().TrimStart();

            if (trimmed.StartsWith("///"))
            {
                block.Add((index, trimmed[3..].ToString().Trim()));
                continue;
            }

            AddOffendingBlockLines(block, offending);
            block.Clear();
        }

        AddOffendingBlockLines(block, offending);
    }

    private static void AddOffendingBlockLines(List<(int Line, string Text)> block, HashSet<int> offending)
    {
        if (block.Count == 0)
        {
            return;
        }

        var joined = string.Join(' ', block.Select(entry => entry.Text));

        foreach (var match in InternalVoicePattern.Matches(joined).Cast<Match>())
        {
            var cursor = 0;

            foreach (var (line, text) in block)
            {
                var end = cursor + text.Length;

                if (match.Index < end && match.Index + match.Length > cursor)
                {
                    offending.Add(line);
                }

                cursor = end + 1;
            }
        }
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

        builder.AppendLine("# Generated by ShippedDocumentationSelfContainmentTests. Refresh:");
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
        builder.AppendLine("# One line per file: <path>|<offending line count>. The count may only shrink.");
        builder.AppendLine("# An empty list is the goal; keep the file even when empty.");

        foreach (var (path, count) in actual)
        {
            builder.Append(path).Append('|').Append(count).AppendLine();
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "shipped-documentation-baseline.txt");

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

    /// <summary>Which rules apply to a file of the public tree.</summary>
    [Flags]
    private enum Rules
    {
        /// <summary>Every line is checked for a pointer into the development record.</summary>
        Reference = 1,

        /// <summary>Every line is also checked for the journal's voice.</summary>
        VoiceEveryLine = 2,

        /// <summary>Only <c>///</c> blocks are also checked for the journal's voice.</summary>
        VoiceDocumentationComments = 4,
    }
}
