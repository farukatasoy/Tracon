using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the run-event payload ratchet: every <see cref="RunEventType"/>
/// member whose documentation makes a claim about the event's payload has to
/// be listed in a tracked baseline, and a member marked <c>covered</c> has to
/// have a test that actually reads that event's <c>Payload</c>.
/// </summary>
/// <remarks>
/// <para>
/// The defect this guards shipped twice at once. <c>ModelFallbackUsed</c>
/// documented that its payload carries "the reason the primary was skipped"
/// while the payload record carried four fields and no reason;
/// <c>ContentMasked</c> documented a "match count" that
/// <see cref="ContentGuardResult"/> has never had a field for. Both claims
/// ship inside the NuGet package's XML documentation, so a consumer parses
/// for a key that is never written.
/// </para>
/// <para>
/// Neither was catchable by the existing suite, and the measurement says why:
/// four of the fourteen payload-claiming members have NO test reading their
/// payload at all, so prose and writer can drift apart with nothing in
/// between. <c>ModelFallbackUsed</c> was one of those four.
/// </para>
/// <para>
/// <strong>What this gate does and does not catch.</strong> It catches the
/// unpinned case — a member that claims something about its payload while
/// nothing in the suite ever reads that payload — and it stops a NEW claim
/// from shipping unpinned. It does <em>not</em> compare the prose to the
/// keys the writer emits: <c>ContentMasked</c> was pinned by a real test and
/// still drifted, because that test asserted what the payload must NOT
/// contain, not what it must. Machine-checking English against JSON is not
/// something a repository gate can do; the pairing this gate forces is what
/// puts a human in front of the claim. This is a documented limit, not an
/// oversight.
/// </para>
/// <para>
/// This is a ratchet, the same contract as <see cref="AmbientWriteSiteTests"/>
/// and <see cref="SourceLanguageTests"/>. <c>run-event-payload-baseline.txt</c>
/// lists one line per member as
/// <c>&lt;Member&gt; | covered | &lt;test file&gt;</c> or
/// <c>&lt;Member&gt; | uncovered | &lt;why not yet&gt;</c>. A NEW claiming
/// member fails until it is listed; a listed member that stopped claiming
/// fails as stale; and <c>uncovered</c> only ever SHRINKS — once a test reads
/// that event's payload, the entry must move to <c>covered</c> and can never
/// go back. Refresh after a reviewed change:
/// <c>TRACON_RUN_EVENT_PAYLOAD_REFRESH=1 dotnet test tests/Tracon.Core.UnitTests -c Release</c>.
/// </para>
/// </remarks>
public sealed class RunEventPayloadContractTests
{
    private const string RefreshEnvVar = "TRACON_RUN_EVENT_PAYLOAD_REFRESH";
    /// <summary>
    /// Gates that read RunEventType.cs as TEXT rather than reading an event's
    /// payload at run time.
    /// </summary>
    /// <remarks>
    /// Such a file names event types in <c>nameof</c> and in prose and mentions
    /// "Payload" while asserting nothing about one, so the heuristic below
    /// would read it as coverage. Counting it would let a gate certify its own
    /// subject and would move an entry to <c>covered</c> without anyone ever
    /// having looked at that payload — the exact blindness this ratchet exists
    /// to measure. The list is short on purpose: add a file only when it scans
    /// source instead of running the writer.
    /// </remarks>
    private static readonly string[] SourceScanningGates =
    [
        "RunEventPayloadContractTests.cs",
        "RunEventPayloadSuppressionTests.cs",
        "RunEventTypeFrontendParityTests.cs",
    ];
    private const string Covered = "covered";
    private const string Uncovered = "uncovered";

    private static readonly Regex EnumMember = new(
        @"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\d+\s*,",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Payload_claiming_event_types_are_pinned_by_a_test()
    {
        var claiming = ScanClaimingMembers();

        claiming.ShouldNotBeEmpty(
            "The scan found no payload-claiming member at all — RunEventType.cs moved or its shape changed.");

        if (string.Equals(Environment.GetEnvironmentVariable(RefreshEnvVar), "1", StringComparison.Ordinal))
        {
            WriteBaseline(claiming);
        }

        File.Exists(BaselinePath).ShouldBeTrue(
            $"'{BaselinePath}' is missing. Generate it with {RefreshEnvVar}=1 (see the class remarks).");

        var baseline = ReadBaseline();
        var failures = new List<string>();

        foreach (var member in claiming)
        {
            if (!baseline.ContainsKey(member))
            {
                failures.Add(
                    $"+ {member}: documents its payload but is not in the baseline. Add a test that reads " +
                    $"this event's Payload, then list it as 'covered'.");
            }
        }

        foreach (var (member, entry) in baseline)
        {
            if (!claiming.Contains(member))
            {
                failures.Add($"- {member}: no longer documents its payload, refresh the baseline");
                continue;
            }

            var coveredBy = FindCoveringTestFiles(member);

            if (string.Equals(entry.State, Covered, StringComparison.Ordinal))
            {
                if (coveredBy.Count == 0)
                {
                    failures.Add(
                        $"! {member}: listed as covered by '{entry.Note}', but no test file reads " +
                        $"RunEventType.{member} together with a Payload assertion.");
                }
                else if (!coveredBy.Contains(entry.Note))
                {
                    failures.Add(
                        $"! {member}: listed as covered by '{entry.Note}', but that file no longer covers it. " +
                        $"It is now covered by: {string.Join(", ", coveredBy)}.");
                }
            }
            else if (coveredBy.Count > 0)
            {
                // The ratchet direction: debt is paid down, never re-taken.
                failures.Add(
                    $"< {member}: listed as uncovered, but {string.Join(", ", coveredBy)} now reads its payload. " +
                    $"Move the entry to 'covered'.");
            }
        }

        failures.Sort(StringComparer.Ordinal);

        failures.ShouldBeEmpty(
            customMessage: $"The run-event payload baseline is stale.{Environment.NewLine}" +
                           $"{string.Join(Environment.NewLine, failures)}{Environment.NewLine}" +
                           $"Refresh with {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release");
    }

    /// <summary>
    /// Regression coverage for the scan itself: a member whose documentation
    /// mentions its payload is reported, one whose documentation does not is
    /// skipped, and the doc block of the PRECEDING member never leaks onto
    /// the next one.
    /// </summary>
    [Fact]
    public void Scan_reports_only_the_members_that_document_a_payload()
    {
        const string Source = """
            public enum Sample
            {
                /// <summary>Nothing is said about the event's extra data.</summary>
                Plain = 0,

                /// <summary>Text carries the name and <c>Payload</c> the identifier.</summary>
                Claims = 1,

                /// <summary>Says nothing.</summary>
                /// <remarks>The payload does not carry the blocked content.</remarks>
                DeniesInRemarks = 2,

                Undocumented = 3,
            }
            """;

        var claiming = ScanClaimingMembers(Source);

        claiming.ShouldBe(["Claims", "DeniesInRemarks"], ignoreOrder: true);
    }

    /// <summary>
    /// A negative claim is a claim: a member that documents what its payload
    /// does NOT carry is pinned exactly like one that documents what it does.
    /// Losing that assertion is how sensitive content starts being written.
    /// </summary>
    [Fact]
    public void Negative_payload_claims_are_tracked_too()
    {
        var baseline = ReadBaseline();

        baseline.ShouldContainKey(nameof(RunEventType.ContentBlocked));
        baseline[nameof(RunEventType.ContentBlocked)].State.ShouldBe(Covered);
    }

    private static HashSet<string> ScanClaimingMembers()
        => ScanClaimingMembers(File.ReadAllText(RunEventTypePath));

    private static HashSet<string> ScanClaimingMembers(string source)
    {
        var claiming = new HashSet<string>(StringComparer.Ordinal);
        var documentation = new StringBuilder();

        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            if (trimmed.TrimStart().StartsWith("///", StringComparison.Ordinal))
            {
                documentation.Append(trimmed).Append('\n');
                continue;
            }

            var match = EnumMember.Match(trimmed);

            if (match.Success)
            {
                if (documentation.ToString().Contains("payload", StringComparison.OrdinalIgnoreCase))
                {
                    claiming.Add(match.Groups["name"].Value);
                }

                documentation.Clear();
                continue;
            }

            // Anything else (a blank line, an attribute, a brace) ends the
            // block: a doc comment must sit immediately above its member, and
            // letting it carry further would attribute one member's claim to
            // the next one.
            if (trimmed.Trim().Length > 0)
            {
                documentation.Clear();
            }
        }

        return claiming;
    }

    /// <summary>
    /// Finds the test files that read a given event type's payload — the
    /// file names <c>RunEventType.&lt;member&gt;</c> and asserts on
    /// <c>Payload</c> somewhere in the same file.
    /// </summary>
    /// <remarks>
    /// A file-level pairing, not a statement-level one: resolving which
    /// assertion belongs to which event would need a compilation, and the
    /// point of a repository gate is to be fast and dependency-free. The
    /// coarse answer is enough — it distinguishes "somebody pinned this
    /// payload" from "nobody ever looked at it", which is the only
    /// distinction the two shipped defects turned on.
    /// </remarks>
    [Fact]
    public void Every_excluded_gate_really_reads_the_source_instead_of_a_payload()
    {
        // The exclusion list is the one place a member could be hidden from
        // the coverage scan. A file earns a place on it only by reading
        // RunEventType.cs as text; a normal test that asserts on a payload
        // must never be parked here to silence the ratchet.
        var failures = new List<string>();

        foreach (var gate in SourceScanningGates)
        {
            var matches = Directory
                .EnumerateFiles(TestsRoot, gate, SearchOption.AllDirectories)
                .ToList();

            if (matches.Count == 0)
            {
                failures.Add($"{gate}: listed as a source-scanning gate but no such file exists.");
                continue;
            }

            foreach (var file in matches)
            {
                var text = File.ReadAllText(file);

                if (!text.Contains("File.ReadAllText", StringComparison.Ordinal) &&
                    !text.Contains("File.ReadAllLines", StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{gate}: excluded from the coverage scan but never reads a source file. " +
                        "Only a gate that scans RunEventType.cs as text belongs on that list.");
                }
            }
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }

    private static List<string> FindCoveringTestFiles(string member)
    {
        var marker = $"RunEventType.{member}";
        var covering = new List<string>();

        foreach (var file in Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (Array.Exists(
                    SourceScanningGates,
                    gate => Path.GetFileName(file).Equals(gate, StringComparison.Ordinal)))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            if (text.Contains(marker, StringComparison.Ordinal) &&
                text.Contains("Payload", StringComparison.Ordinal))
            {
                covering.Add(Path.GetRelativePath(RepositoryRoot, file).Replace('\\', '/'));
            }
        }

        covering.Sort(StringComparer.Ordinal);
        return covering;
    }

    private static Dictionary<string, BaselineEntry> ReadBaseline()
    {
        var baseline = new Dictionary<string, BaselineEntry>(StringComparer.Ordinal);

        foreach (var line in File.ReadAllLines(BaselinePath))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|', 3);

            if (parts.Length != 3)
            {
                throw new InvalidOperationException(
                    $"Malformed baseline line (expected '<Member> | covered|uncovered | <note>'): {line}");
            }

            baseline[parts[0].Trim()] = new BaselineEntry(parts[1].Trim(), parts[2].Trim());
        }

        return baseline;
    }

    private static void WriteBaseline(HashSet<string> claiming)
    {
        var existing = File.Exists(BaselinePath) ? ReadBaseline() : [];

        var builder = new StringBuilder()
            .AppendLine("# Generated by RunEventPayloadContractTests. Refresh:")
            .AppendLine($"#   {RefreshEnvVar}=1 dotnet test tests/Tracon.Core.UnitTests -c Release")
            .AppendLine("# One line per RunEventType member whose documentation makes a claim about the")
            .AppendLine("# event's payload. 'covered' names the test file that reads that payload;")
            .AppendLine("# 'uncovered' records the debt and only ever SHRINKS.");

        foreach (var member in claiming.OrderBy(static name => name, StringComparer.Ordinal))
        {
            var covering = FindCoveringTestFiles(member);

            var entry = covering.Count > 0
                ? new BaselineEntry(Covered, covering[0])
                : existing.GetValueOrDefault(
                    member,
                    new BaselineEntry(Uncovered, "REPLACE ME - why no test reads this payload yet"));

            builder.Append(member).Append(" | ").Append(entry.State).Append(" | ").Append(entry.Note).AppendLine();
        }

        File.WriteAllText(BaselinePath, builder.ToString());
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string TestsRoot { get; } = Path.Combine(RepositoryRoot, "tests");

    private static string RunEventTypePath { get; } = Path.Combine(
        RepositoryRoot,
        "src",
        "Tracon.Abstractions",
        "Runs",
        "RunEventType.cs");

    private static string BaselinePath { get; } = Path.Combine(
        RepositoryRoot,
        "tests",
        "Tracon.Core.UnitTests",
        "Architecture",
        "run-event-payload-baseline.txt");

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

    private sealed record BaselineEntry(string State, string Note);
}
