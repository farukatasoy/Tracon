using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the second half of the documentation boundary: everything that ships
/// to a consumer is self-contained and written in the consumer's voice.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SourceLanguageTests"/> settled the language question — a shipped
/// document is English. This one settles the audience question. A shipped
/// document may only point at things the consumer holds: the package's own types,
/// members, configuration keys, HTTP paths, MSBuild properties, and the
/// documentation site. It may not point at the development record, which the
/// consumer never receives: phase numbers, decision ids, candidate ids, and the
/// Turkish journal under <c>docs/</c>.
/// </para>
/// <para>
/// The second pattern covers voice rather than reference. The documentation-site
/// generators already strip alarm emoji, <c>Rationale:</c> openers, and
/// <c>Measured (…)</c> blocks before publishing, which is the project's own
/// judgement that they do not belong on a consumer surface. The package ships the
/// unstripped text, so the same judgement has to be enforced at the source.
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
/// <para>
/// Scope is exactly what reaches a consumer. Implementation comments (<c>//</c>)
/// are out of scope on purpose: a maintainer reading the source benefits from
/// "K-320 measured this position", and that line never leaves the repository.
/// The same sentence inside <c>&lt;summary&gt;</c> does leave, because
/// <c>GenerateDocumentationFile</c> ships it as XML.
/// </para>
/// </remarks>
public sealed class ShippedDocumentationSelfContainmentTests
{
    private const string RefreshEnvVar = "TRACON_SHIPPED_DOCS_REFRESH";

    /// <remarks>Declared first: a static initialiser below reads it.</remarks>
    private static string RepositoryRoot { get; } = FindRepositoryRoot();


    /// <summary>
    /// References into the development record. The consumer holds none of these, so a
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
    /// The repository README is read inside the repository, where <c>docs/</c>
    /// resolves. Its links there are correct and stay; only the record references
    /// are wrong for a first-time reader.
    /// </summary>
    private static readonly Regex DocumentationPathPattern = new(
        @"\bdocs/[^\s`<),""]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// This file names every forbidden pattern in order to test for it.
    /// </summary>
    private static readonly string[] SkippedFiles =
    [
        "tests/Tracon.Core.UnitTests/Architecture/ShippedDocumentationSelfContainmentTests.cs",
    ];

    private static readonly string[] SkippedDirectorySegments =
        ["obj", "bin", "node_modules", "artifacts", "dist", "wwwroot"];

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

    private static SortedDictionary<string, int> Scan()
    {
        var results = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var (relative, absolute, kind) in EnumerateShippedFiles())
        {
            var count = CountOffendingLines(absolute, kind);

            if (count > 0)
            {
                results[relative] = count;
            }
        }

        return results;
    }

    /// <summary>Every file that a consumer receives, with the rule that applies to it.</summary>
    private static IEnumerable<(string Relative, string Absolute, ScanKind Kind)> EnumerateShippedFiles()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "src");

        if (Directory.Exists(sourceRoot))
        {
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/');

                if (IsSkipped(relative))
                {
                    continue;
                }

                // The project template's content becomes the consumer's OWN project,
                // and buildTransitive/ is imported into every consumer build: both
                // are read by someone who has no access to the development record.
                var shippedWhole =
                    relative.Contains("/content/", StringComparison.Ordinal) ||
                    relative.Contains("/buildTransitive/", StringComparison.Ordinal);

                // A package README is PackageReadmeFile: nuget.org renders it.
                if (relative.EndsWith("/README.md", StringComparison.Ordinal) &&
                    relative.Count(character => character == '/') == 2)
                {
                    yield return (relative, file, ScanKind.EveryLine);
                }
                else if (shippedWhole && !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    yield return (relative, file, ScanKind.EveryLine);
                }
                else if (Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    yield return (relative, file, ScanKind.DocumentationComments);
                }
            }
        }

        // The repository landing page. Its docs/ links resolve for a reader who is
        // already in the repository, so only record references count against it.
        var readme = Path.Combine(RepositoryRoot, "README.md");

        if (File.Exists(readme))
        {
            yield return ("README.md", readme, ScanKind.EveryLineKeepingDocumentationLinks);
        }

        // PackageLicenseFile packs one of these into every package (Phase 160), so
        // they reach a consumer exactly the way a package README does.
        foreach (var licence in new[] { "LICENSE.md", "LICENSE-MIT.md" })
        {
            var path = Path.Combine(RepositoryRoot, licence);

            if (File.Exists(path))
            {
                yield return (licence, path, ScanKind.EveryLine);
            }
        }

        // Packaged into Tracon.AspNetCore under buildTransitive/.
        var openApi = Path.Combine(RepositoryRoot, "docs", "openapi", "tracon.json");

        if (File.Exists(openApi))
        {
            yield return ("docs/openapi/tracon.json", openApi, ScanKind.EveryLine);
        }
    }

    private static bool IsSkipped(string relativePath)
    {
        if (SkippedFiles.Contains(relativePath, StringComparer.Ordinal))
        {
            return true;
        }

        foreach (var segment in SkippedDirectorySegments)
        {
            if (relativePath.Contains($"/{segment}/", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// A documentation comment is inspected as a block rather than line by line: an
    /// XML comment wraps at the source width, so "(phase 65)" is routinely split
    /// across two lines and a per-line scan sees neither half. Measured: three such
    /// references survived a per-line pass and were only caught when the generator
    /// joined the block.
    /// </remarks>
    private static int CountOffendingLines(string file, ScanKind kind)
    {
        var count = 0;

        if (kind == ScanKind.DocumentationComments)
        {
            var block = new List<string>();

            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.AsSpan().TrimStart();

                if (trimmed.StartsWith("///"))
                {
                    block.Add(trimmed[3..].ToString().Trim());
                    continue;
                }

                count += CountOffendingBlockLines(block);
                block.Clear();
            }

            count += CountOffendingBlockLines(block);

            return count;
        }

        foreach (var line in File.ReadLines(file))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var inspected = kind == ScanKind.EveryLineKeepingDocumentationLinks
                ? DocumentationPathPattern.Replace(line, string.Empty)
                : line;

            if (InternalReferencePattern.IsMatch(inspected) || InternalVoicePattern.IsMatch(inspected))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Counts the lines of one comment block that carry a reference. The block is
    /// matched as joined text, and a match is then attributed to the lines it spans
    /// so the baseline still counts lines rather than blocks.
    /// </summary>
    private static int CountOffendingBlockLines(List<string> block)
    {
        if (block.Count == 0)
        {
            return 0;
        }

        var joined = string.Join(' ', block);

        if (!InternalReferencePattern.IsMatch(joined) && !InternalVoicePattern.IsMatch(joined))
        {
            return 0;
        }

        var offending = new HashSet<int>();

        foreach (var match in InternalReferencePattern.Matches(joined).Concat(InternalVoicePattern.Matches(joined)))
        {
            var cursor = 0;

            for (var index = 0; index < block.Count; index++)
            {
                var end = cursor + block[index].Length;

                if (match.Index < end && match.Index + match.Length > cursor)
                {
                    offending.Add(index);
                }

                cursor = end + 1;
            }
        }

        return offending.Count;
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

    /// <summary>Which lines of a shipped file the rule applies to.</summary>
    private enum ScanKind
    {
        /// <summary>Only <c>///</c> lines: the rest of a source file never ships.</summary>
        DocumentationComments,

        /// <summary>Every line, both patterns.</summary>
        EveryLine,

        /// <summary>Every line, but <c>docs/</c> links are allowed and removed before inspection.</summary>
        EveryLineKeepingDocumentationLinks,
    }
}
