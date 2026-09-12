using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the seam-contract-documentation ratchet: every public extension interface in
/// <c>src/</c> has to state its DI lifetime, its tenant mode, and its delivery guarantee —
/// in its OWN XML documentation, not in a registration extension, an implementation, or a
/// site page — and the interfaces that still owe one of these are named in a tracked,
/// shrinking baseline.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards (Phase 121, the "35x 🟡 systemic lane"): Tracon has 78
/// public extension interfaces, and no single one of them was required to answer the same
/// four questions a third-party implementer actually needs — DI lifetime, tenant mode,
/// delivery guarantee, and the limit of whatever guarantee it does make. Measured
/// 2026-08-27: only 12 of 76 <c>Tracon.Abstractions</c> interfaces mentioned
/// <c>singleton</c>/<c>thread-safe</c> at all, and only 5 carried a tenant-mode table.
/// Two interfaces (<c>IRunStore</c>, <c>IJobHandler</c>) already got this right; this gate
/// generalizes their pattern instead of re-inventing it.
/// </para>
/// <para>
/// <strong>Three dimensions, one mechanism each — not a single generic scan.</strong> DI
/// lifetime, tenant mode, and delivery guarantee each have a FIXED, small vocabulary (the
/// standard's own answer set: <c>singleton</c>/<c>scoped</c>/<c>transient</c>;
/// <c>ambient tenant</c>/<c>expected tenant</c>/<c>tenant-independent</c>;
/// <c>at-least-once</c>/<c>exactly-once</c>/<c>best-effort</c>/<c>no delivery guarantee
/// applies</c>) so a literal, case-insensitive phrase scan over each interface's OWN XML
/// doc block is a faithful gate: <see cref="Seam_contract_baseline_matches_the_tracked_debt_ledger"/>.
/// The FOURTH dimension — the limit of the guarantee actually made ("cooperative-only
/// cancellation", "not strictly-exclusive", "relies on a backup mechanism") — has no fixed
/// vocabulary; it is inherently interface-specific prose, the same shape
/// <c>OrderingContractDocumentationTests</c> already gates for numeric-ordering direction.
/// It is therefore covered by <see cref="A_guarantee_limit_contract_states_what_is_NOT_guaranteed"/>
/// below, one exact-phrase row per interface this phase touches (BL-026, BL-028, BL-042) —
/// NOT by the generic baseline scan. A future phase adding a new guarantee-limit interface
/// adds its own row there, the same way <c>OrderingContractDocumentationTests</c> grows.
/// </para>
/// <para>
/// <strong>This is a ratchet, not a snapshot</strong> — the same contract as
/// <c>RawExceptionTextSiteTests</c>: <c>seam-contract-baseline.txt</c> lists every
/// <c>&lt;interface&gt;:&lt;dimension&gt;</c> the scan finds UNDOCUMENTED today, one entry
/// per missing dimension (decided over "one line per interface, dimensions listed beside
/// it" specifically so that documenting HALF of an interface's missing dimensions still
/// shrinks the file — Phase 121, Open Question 1). A NEW entry fails the test (an
/// undocumented interface nobody reviewed); a LOST entry fails it too (a now-documented
/// interface whose stale baseline row would hide that the work happened). Unlike
/// <c>RawExceptionTextSiteTests</c>, an entry here carries no freeform "why is this safe"
/// reason — this ledger does not vouch for anything, it only counts remaining debt.
/// Refresh after a reviewed documentation change:
/// <c>TRACON_SEAM_CONTRACT_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// <para>
/// <strong>🚨 K-642's tuzak: a split phrase is not a gate.</strong> An early draft of
/// <c>OrderingContractDocumentationTests</c> searched for two phrase FRAGMENTS
/// independently; a deliberate break of one fragment still passed because a LATER,
/// unrelated occurrence of the other fragment satisfied the check. This gate searches
/// each dimension's vocabulary as WHOLE phrases (never split into independently-searched
/// words) for exactly that reason, and the DoD requires each dimension to be verified red
/// under an isolated, dedicated break — not just once for the mechanism as a whole.
/// </para>
/// </remarks>
public sealed class SeamContractDocumentationTests
{
    private const string RefreshEnvVar = "TRACON_SEAM_CONTRACT_REFRESH";

    private static readonly Regex InterfaceDeclaration = new(
        @"^public interface (?<name>I[A-Za-z0-9_]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex DocTag = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// One phrase set per dimension, checked as WHOLE phrases (see the class remarks'
    /// K-642 warning) against an interface's own XML doc block, case-insensitively.
    /// </summary>
    private static readonly (string Dimension, string[] Phrases)[] Dimensions =
    [
        ("lifetime", ["singleton", "scoped", "transient"]),
        ("tenant", ["ambient tenant", "expected tenant", "tenant-independent"]),
        ("delivery", ["at-least-once", "exactly-once", "best-effort", "no delivery guarantee applies"]),
    ];

    [Fact]
    public void Seam_contract_baseline_matches_the_tracked_debt_ledger()
    {
        var actual = ScanMissingDimensions(RepositoryRoot);

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            WriteBaseline(actual);
        }

        File.Exists(BaselinePath).ShouldBeTrue(
            $"'{BaselinePath}' is missing. Generate it with {RefreshEnvVar}=1 (see the class remarks).");

        var baseline = ReadBaseline();
        var failures = new List<string>();

        foreach (var entry in actual)
        {
            if (!baseline.Contains(entry))
            {
                failures.Add($"+ {entry}: undocumented dimension, not in the baseline (a new seam, or a regression)");
            }
        }

        foreach (var entry in baseline)
        {
            if (!actual.Contains(entry))
            {
                failures.Add($"- {entry}: now documented, refresh the baseline so the debt ledger shrinks");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The seam-contract baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"Document the missing dimension directly on the interface's own XML doc (see " +
                           $"IRunStore/IJobHandler for the reference pattern), then refresh: " +
                           $"{RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    /// <summary>
    /// Regression coverage for the scan itself: an undocumented interface must be
    /// reported as missing ALL THREE dimensions, and a fully documented one must be
    /// reported as missing none — isolated from the real repository tree.
    /// </summary>
    [Fact]
    public void Scan_reports_every_missing_dimension_and_none_of_the_answered_ones()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-seam-contract-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "Undocumented.cs"),
                """
                namespace Sample;

                public interface IUndocumented
                {
                    void DoWork();
                }
                """);

            File.WriteAllText(
                Path.Combine(directory.FullName, "Documented.cs"),
                """
                namespace Sample;

                /// <summary>A fully documented seam.</summary>
                /// <remarks>
                /// Registered as a SINGLETON. Tenant mode: AMBIENT tenant, read from
                /// ITenantContext. Delivery guarantee: AT-LEAST-ONCE, the caller may retry.
                /// </remarks>
                public interface IDocumented
                {
                    void DoWork();
                }
                """);

            var actual = ScanMissingDimensions(directory.FullName);

            actual.ShouldContain(entry => string.Equals(entry, "IUndocumented:lifetime", StringComparison.Ordinal));
            actual.ShouldContain(entry => string.Equals(entry, "IUndocumented:tenant", StringComparison.Ordinal));
            actual.ShouldContain(entry => string.Equals(entry, "IUndocumented:delivery", StringComparison.Ordinal));
            actual.ShouldNotContain(entry => entry.StartsWith("IDocumented:", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Regression coverage for the exact shape <c>IJobHandler</c> (the delivery-guarantee
    /// reference example, K-641) uses: the interface's HEADER doc says nothing about
    /// delivery, but a MEMBER's own <c>&lt;remarks&gt;</c> states "at-least-once". This
    /// must be recognized as answered — the scan reads the whole interface's doc
    /// surface, not just the header (this was a real defect this class shipped with
    /// once and was caught by hand, not by a test, before this regression test existed).
    /// </summary>
    [Fact]
    public void A_dimension_answered_on_a_MEMBERs_doc_counts_as_answered()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-seam-contract-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "MemberLevel.cs"),
                """
                namespace Sample;

                /// <summary>An interface whose header says nothing about delivery.</summary>
                public interface IMemberLevel
                {
                    /// <summary>Executes the job.</summary>
                    /// <remarks>
                    /// Execution is at-least-once, not exactly-once. Registered as a
                    /// SINGLETON. Tenant mode is AMBIENT tenant.
                    /// </remarks>
                    void ExecuteAsync();
                }
                """);

            var actual = ScanMissingDimensions(directory.FullName);

            actual.ShouldNotContain(entry => entry.StartsWith("IMemberLevel:", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Regression coverage for the K-642 tuzak this class's remarks describe: a phrase
    /// must be matched WHOLE. A doc block carrying only the first HALF of a two-word
    /// phrase must still be reported as missing that dimension.
    /// </summary>
    [Fact]
    public void A_split_phrase_does_not_satisfy_the_scan()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-seam-contract-test");

        try
        {
            File.WriteAllText(
                Path.Combine(directory.FullName, "HalfPhrase.cs"),
                """
                namespace Sample;

                /// <summary>
                /// The ambient world is complex; this store is expected to behave, and its
                /// tenant is whatever it is. Best effort, we hope.
                /// </summary>
                public interface IHalfPhrase
                {
                    void DoWork();
                }
                """);

            var actual = ScanMissingDimensions(directory.FullName);

            // "ambient" and "expected" and "tenant" all appear, scattered, but never as the
            // WHOLE phrase "ambient tenant" or "expected tenant" - this must still be
            // reported as missing. Likewise "best effort" (no hyphen) is not "best-effort".
            actual.ShouldContain(entry => string.Equals(entry, "IHalfPhrase:tenant", StringComparison.Ordinal));
            actual.ShouldContain(entry => string.Equals(entry, "IHalfPhrase:delivery", StringComparison.Ordinal));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static SortedSet<string> ScanMissingDimensions(string root)
    {
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var srcRoot = Path.Combine(root, "src");
        var scanRoot = Directory.Exists(srcRoot) ? srcRoot : root;

        foreach (var file in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var source = File.ReadAllText(file);

            foreach (Match match in InterfaceDeclaration.Matches(source))
            {
                var name = match.Groups["name"].Value;
                var docText = InterfaceDocSurface(source, match);

                foreach (var (dimension, phrases) in Dimensions)
                {
                    var answered = phrases.Any(phrase => docText.Contains(phrase, StringComparison.OrdinalIgnoreCase));

                    if (!answered)
                    {
                        missing.Add($"{name}:{dimension}");
                    }
                }
            }
        }

        return missing;
    }

    /// <summary>
    /// The FULL doc surface a reader gets for one interface: its own header doc block
    /// (<see cref="DocBlockAbove"/>) PLUS every <c>///</c> line inside its body — a
    /// member's own <c>&lt;remarks&gt;</c> is part of "the interface's own XML
    /// documentation" the standard asks for, not just the header. This is deliberate:
    /// <c>IJobHandler</c> (the delivery-guarantee reference example, K-641) states
    /// "at-least-once" on <c>ExecuteAsync</c>'s <c>&lt;remarks&gt;</c>, not the
    /// interface header, and a scan that only read the header would wrongly flag it
    /// as undocumented.
    /// </summary>
    private static string InterfaceDocSurface(string source, Match declarationMatch)
    {
        var header = DocBlockAbove(source, declarationMatch.Index);

        var bodyStart = source.IndexOf('{', declarationMatch.Index + declarationMatch.Length);

        if (bodyStart < 0)
        {
            return header;
        }

        var depth = 0;
        var bodyEnd = -1;

        for (var i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    bodyEnd = i;
                    break;
                }
            }
        }

        if (bodyEnd < 0)
        {
            return header;
        }

        var body = source[bodyStart..(bodyEnd + 1)];
        var memberDocLines = body.Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("///", StringComparison.Ordinal))
            .Select(static line => line.TrimStart('/').Trim());

        var memberDocs = DocTag.Replace(string.Join(' ', memberDocLines), " ");

        return $"{header} {memberDocs}";
    }

    /// <summary>
    /// Collects the contiguous <c>///</c> lines directly above a declaration (skipping
    /// blank lines and attribute lines), strips XML tags, and returns the plain text.
    /// An interface with no doc comment immediately above it returns an empty string,
    /// which correctly fails every dimension's check.
    /// </summary>
    private static string DocBlockAbove(string source, int declarationIndex)
    {
        var before = source[..declarationIndex];
        var lines = before.Split('\n');
        var collected = new List<string>();

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var trimmed = lines[i].TrimEnd('\r').Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('[') || trimmed.EndsWith(']'))
            {
                continue;
            }

            if (trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                collected.Add(trimmed);
                continue;
            }

            break;
        }

        collected.Reverse();

        var joined = string.Join(' ', collected.Select(static line => line.TrimStart('/').Trim()));

        return DocTag.Replace(joined, " ");
    }

    private static SortedSet<string> ReadBaseline()
    {
        var baseline = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var line in File.ReadLines(BaselinePath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            baseline.Add(line.Trim());
        }

        return baseline;
    }

    private static void WriteBaseline(SortedSet<string> actual)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Generated by SeamContractDocumentationTests. Refresh:");
        builder.AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
        builder.AppendLine("# One line per <interface>:<dimension> still UNDOCUMENTED. Dimensions: lifetime, tenant,");
        builder.AppendLine("# delivery (the fourth dimension, guarantee limits, is gated separately by");
        builder.AppendLine("# A_guarantee_limit_contract_states_what_is_NOT_guaranteed in this same file). This is a");
        builder.AppendLine("# debt ledger, not a safety allowlist - an entry needs no reason, only removal by");
        builder.AppendLine("# documenting the interface (see IRunStore / IJobHandler for the reference pattern).");

        foreach (var entry in actual)
        {
            builder.AppendLine(entry);
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "seam-contract-baseline.txt");

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

    // --- Dimension 4: guarantee limits (BL-026, BL-028, BL-042) ---
    // Open-ended prose, not a fixed vocabulary - the same shape
    // OrderingContractDocumentationTests already gates for numeric-ordering direction.
    // Each row is verified to fail red under an isolated, dedicated break (DoD).

    public static TheoryData<string, string, string[]> GuaranteeLimitContracts() => new()
    {
        {
            // BL-028: cancellation is cooperative-only - TryCancel signals the token,
            // it does not force the run's body to stop reading it.
            Path.Combine("Tracon.Abstractions", "Runs", "IRunCancellationRegistry.cs"),
            "bool TryCancel(Guid runId, string? tenantId);",
            ["cooperative", "does not force"]
        },
        {
            // BL-042: the lease is eventually-correct, not strictly-exclusive - a
            // frozen owner (GC pause, thread starvation, network partition) can leave
            // a narrow split-brain window before the lease expires.
            Path.Combine("Tracon.Abstractions", "Coordination", "ISingletonLeaseStore.cs"),
            "public interface ISingletonLeaseStore",
            ["not strictly exclusive", "split-brain"]
        },
        {
            // BL-026: IsDraining relies on the HTTP host's own request draining and the
            // job worker's own WaitForRunningJobsAsync to actually stop accepting work
            // in time - it does not itself guarantee that a run in the register/accept
            // window is never started after the flag flips.
            Path.Combine("Tracon.Abstractions", "Runs", "ITraconDrainState.cs"),
            "public interface ITraconDrainState",
            ["does not itself", "backup mechanism"]
        },
    };

    [Theory]
    [MemberData(nameof(GuaranteeLimitContracts))]
    public void A_guarantee_limit_contract_states_what_is_NOT_guaranteed(
        string relativePath,
        string memberDeclaration,
        string[] requiredPhrases)
    {
        var summary = SummaryAbove(relativePath, memberDeclaration);

        foreach (var phrase in requiredPhrases)
        {
            summary.ShouldContain(
                phrase,
                Case.Insensitive,
                $"'{relativePath}' documents a guarantee-limit contract, so its summary has to say, in words a " +
                $"consumer can act on, what is NOT guaranteed. Verify the phrase against the actual implementation " +
                $"before changing it here.");
        }
    }

    private static string SummaryAbove(string relativePath, string memberDeclaration)
    {
        var path = Path.Combine(RepositoryRoot, "src", relativePath);
        var source = File.ReadAllText(path);

        var member = source.IndexOf(memberDeclaration, StringComparison.Ordinal);

        member.ShouldBeGreaterThan(
            -1,
            $"'{path}' no longer declares '{memberDeclaration}'; this gate has gone stale.");

        return DocTag.Replace(DocBlockAbove(source, member), " ");
    }
}
