using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Keeps the audit trail's two write guarantees in ONE place: no shipped source file
/// may call <c>IAuditLog.WriteAsync</c> itself, because doing so re-decides — silently,
/// per call site — whether a failed write stops the operation.
/// </summary>
/// <remarks>
/// <para>
/// Phase 171 exists because that decision HAD drifted: four hand-written copies of the
/// fail-closed write and two direct calls, one of which did not even log the failure.
/// Removing the copies is not enough on its own — the sixth copy would arrive with the
/// next phase that needs one. This test is the ratchet that makes it arrive as a
/// failure instead.
/// </para>
/// <para>
/// The sibling <see cref="AuditContentPolicyTests"/> asks a different question about
/// the same call sites: WHAT may be written. This one asks what HAPPENS when the write
/// fails.
/// </para>
/// <para>
/// 🚨 A source scan that finds nothing passes for the wrong reason. Both tests below
/// therefore assert a count first: if the scan stops seeing the files it is supposed
/// to read, it fails rather than going quietly green — the same guard
/// <c>check-console-screens.mjs</c> carries.
/// </para>
/// </remarks>
public sealed class AuditWritePolicyTests
{
    /// <summary>The only file allowed to call <c>IAuditLog.WriteAsync</c> directly.</summary>
    private const string PolicyFile = "src/Tracon.Core/Audit/AuditRecorder.cs";

    /// <summary>
    /// The fail-closed operations: the file each one lives in, and every action name it
    /// writes. This list IS the published contract of K-776 — six operations refuse to
    /// apply when their audit entry cannot be written, and every other audit write is
    /// best-effort.
    /// </summary>
    /// <remarks>
    /// Adding or removing an entry changes a published security guarantee, so it travels
    /// with the three consumer-facing places K-776 names: the governance page's table,
    /// the security policy page's scope, and the capability list.
    /// </remarks>
    private static readonly (string Operation, string File, string[] Actions)[] FailClosedOperations =
    [
        (
            "An approval decision (out of band)",
            "src/Tracon.AspNetCore/Endpoints/ApprovalEndpoints.cs",
            ["approval.decision"]),
        (
            // The SAME operation reached through the run request body instead of the
            // approval inbox. It is listed separately because it is a separate call
            // site that has to be checked separately — it was best-effort until phase
            // 171, silently contradicting the published guarantee.
            "An approval decision (in band)",
            "src/Tracon.AspNetCore/Internal/ToolApprovalResolver.cs",
            ["approval.decision"]),
        (
            "Granting or revoking a skill script",
            "src/Tracon.Core/Audit/AuditingSkillScriptGrantStore.cs",
            ["script.grant", "script.revoke"]),
        (
            "Running a skill script",
            "src/Tracon.Core/Skills/Scripts/SandboxedSkillScriptRunner.cs",
            ["script.run"]),
        (
            "Saving or deleting an inbound trigger",
            "src/Tracon.AspNetCore/Endpoints/TriggerEndpoints.cs",
            ["trigger.create", "trigger.update", "trigger.delete"]),
        (
            "An automatic canary rollback",
            "src/Tracon.Core/Experiments/CanaryEvaluationService.cs",
            ["experiment.auto_rollback"]),
        (
            "A data subject erasure",
            "src/Tracon.AspNetCore/Endpoints/DataSubjectEndpoints.cs",
            ["data_subject.erase"]),
    ];

    /// <summary>
    /// The expected number of <c>WriteOrThrowAsync</c> call sites across the whole tree.
    /// </summary>
    /// <remarks>
    /// NINE, not six: grant and revoke are two call sites of one operation, so are the
    /// trigger upsert and the trigger delete, and an approval decision can arrive on two
    /// surfaces (the approval inbox and the run request body). Counting operations and
    /// counting call sites are different questions, and this test asks both.
    /// </remarks>
    private const int ExpectedFailClosedCallSites = 9;

    /// <remarks>
    /// No <c>\b</c> before <c>auditLog</c> — a word boundary does not exist between
    /// <c>_</c> and a letter, so a field named <c>_auditLog</c> would slip through.
    /// The same trap <see cref="AuditContentPolicyTests"/> documents.
    /// </remarks>
    private static readonly Regex DirectWritePattern = new(
        @"[Aa]udit[Ll]og\.WriteAsync\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex FailClosedPattern = new(
        @"AuditRecorder\.WriteOrThrowAsync\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Only_the_recorder_calls_the_audit_log_directly()
    {
        var repoRoot = FindRepoRoot();
        var scanned = 0;
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            scanned++;

            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            if (string.Equals(relative, PolicyFile, StringComparison.Ordinal) || !DirectWritePattern.IsMatch(File.ReadAllText(file)))
            {
                continue;
            }

            offenders.Add(relative);
        }

        scanned.ShouldBeGreaterThan(
            500,
            "The scan read far fewer source files than this repository has; it is looking in "
            + "the wrong place and would pass without checking anything.");

        offenders.ShouldBeEmpty(
            $"Only {PolicyFile} may call IAuditLog.WriteAsync. A direct call decides, at that "
            + "call site alone, whether a failed audit write stops the operation — which is "
            + "exactly the drift phase 171 removed. Use AuditRecorder.WriteAsync for a "
            + "best-effort write or AuditRecorder.WriteOrThrowAsync for a fail-closed one.");
    }

    [Fact]
    public void The_fail_closed_set_is_exactly_the_published_one()
    {
        var repoRoot = FindRepoRoot();
        var found = 0;

        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            found += FailClosedPattern.Matches(File.ReadAllText(file)).Count;
        }

        found.ShouldBe(
            ExpectedFailClosedCallSites,
            "The number of fail-closed audit writes changed. That set is a PUBLISHED security "
            + "guarantee (K-776): adding or removing one also changes concepts/governance.md, "
            + "reference/security-policy.md and capabilities.md. Update all four together, or "
            + "use the best-effort path instead.");
    }

    [Fact]
    public void Every_fail_closed_operation_is_written_by_its_own_file()
    {
        var repoRoot = FindRepoRoot();
        var missing = new List<string>();

        // A qualifier in brackets marks a second SURFACE of the same operation, not a
        // seventh operation: "An approval decision (in band)" and "(out of band)" are one
        // published guarantee reached two ways.
        FailClosedOperations
            .Select(static entry => BaseOperation(entry.Operation))
            .Distinct(StringComparer.Ordinal)
            .Count()
            .ShouldBe(
                6,
                "The published guarantee names six fail-closed operations. Changing that number "
                + "changes what consumers were promised, not just this test.");

        foreach (var (operation, file, actions) in FailClosedOperations)
        {
            var path = Path.Combine(repoRoot, file);

            if (!File.Exists(path))
            {
                missing.Add($"{operation} — {file} is gone");
                continue;
            }

            var source = File.ReadAllText(path);

            if (!FailClosedPattern.IsMatch(source))
            {
                missing.Add($"{operation} — {file} no longer takes the fail-closed path");
                continue;
            }

            foreach (var action in actions)
            {
                if (!source.Contains($"\"{action}\"", StringComparison.Ordinal))
                {
                    missing.Add($"{operation} — {file} no longer writes '{action}'");
                }
            }
        }

        missing.ShouldBeEmpty(
            "A fail-closed operation no longer takes the fail-closed path, or moved file. Each "
            + "of these six operations refuses to apply when its audit entry cannot be written; "
            + "losing that is invisible until the audit store actually breaks in production.");
    }

    private static string BaseOperation(string operation)
    {
        var qualifier = operation.IndexOf(" (", StringComparison.Ordinal);

        return qualifier < 0 ? operation : operation[..qualifier];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root (Tracon.slnx) from the test output directory.");
    }
}
