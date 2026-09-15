using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the phase 64 rule that <c>audit_log</c> never receives conversation
/// content: it is reviewed, closed allowlist of the source files that are allowed
/// to write an audit entry at all.
/// </summary>
/// <remarks>
/// <para>
/// This is a boundary test, not a content-sniffing one: proving that a given
/// string does not "look like" conversation content is not reliable, so instead
/// every call SITE that writes to the audit trail (either <c>AuditRecorder</c>
/// path, or a direct <c>IAuditLog.WriteAsync</c>) must be a file already reviewed
/// and named below. BOTH recorder paths are watched: the fail-closed one writes
/// the same entry shape as the best-effort one, so an unreviewed file could put
/// conversation content in <c>audit_log</c> through it just as easily. A new call site fails this test
/// until a human adds it to the list — which is the point: the review happens
/// once, deliberately, not by guessing from a diff later. See phase 64's
/// evidence table (<c>docs/arsiv/fazlar/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md</c>) for
/// the review that produced this list; every entry is a management/governance
/// action (API key, catalog, quota, retention, session/agent/tool-rule/tenant
/// administration, approval decision, external-call audit), never conversation
/// history.
/// </para>
/// <para>
/// Reads project sources from disk — the same pattern as
/// <see cref="SourceLanguageTests"/>.
/// </para>
/// </remarks>
public sealed class AuditContentPolicyTests
{
    /// <summary>
    /// Every file allowed to call <c>AuditRecorder.WriteAsync</c>,
    /// <c>AuditRecorder.WriteOrThrowAsync</c> or a direct <c>IAuditLog.WriteAsync</c>,
    /// relative to the repository root.
    /// </summary>
    private static readonly string[] AllowedCallSites =
    [
        // AspNetCore endpoints — administrator actions.
        "src/Tracon.AspNetCore/Endpoints/ApiKeyEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/ApprovalEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/CatalogEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/DataSubjectEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/EvalEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/GovernanceEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/QuotaEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/RetentionEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/SessionEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/TenantProviderEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/TriggerEndpoints.cs",
        "src/Tracon.AspNetCore/Endpoints/WebhookEndpoints.cs",

        // AspNetCore internals — tool approval/result resolution, external call audit.
        "src/Tracon.AspNetCore/Internal/ClientToolResultResolver.cs",
        "src/Tracon.AspNetCore/Internal/ToolApprovalResolver.cs",
        "src/Tracon.AspNetCore/Security/ExternalCallAudit.cs",

        // Core — the write helper itself, and store decorators (all administrator
        // actions: agent/experiment/mcp-server/session/skill/skill-script-grant/tenant/
        // tool-approval-rule/workflow-definition writes).
        "src/Tracon.Core/Audit/AuditRecorder.cs",
        "src/Tracon.Core/Audit/AuditingAgentDefinitionStore.cs",
        // Reviewed: writes the skill DEFINITION (name, description, instructions,
        // scripts, resources) — agent configuration an administrator authored, the
        // same category as an agent definition. It carries no conversation content:
        // a skill is never a user message or a model reply.
        "src/Tracon.Core/Audit/AuditingAgentSkillStore.cs",
        "src/Tracon.Core/Audit/AuditingExperimentStore.cs",
        "src/Tracon.Core/Audit/AuditingMcpServerStore.cs",
        "src/Tracon.Core/Audit/AuditingSessionStore.cs",
        "src/Tracon.Core/Audit/AuditingSkillScriptGrantStore.cs",
        "src/Tracon.Core/Audit/AuditingTenantStore.cs",
        "src/Tracon.Core/Audit/AuditingToolApprovalRuleStore.cs",
        "src/Tracon.Core/Audit/AuditingWorkflowDefinitionStore.cs",
        "src/Tracon.Core/Experiments/CanaryEvaluationService.cs",

        // Core — a content guard's BLOCK DECISION (rule name only, never the
        // blocked text — K-325) and a skill script run record (K-089).
        "src/Tracon.Core/Guards/ContentGuardPipeline.cs",
        "src/Tracon.Core/Skills/Scripts/SandboxedSkillScriptRunner.cs",
    ];

    /// <remarks>
    /// 🚨 No <c>\b</c> before <c>auditLog</c>: a word boundary does NOT exist
    /// between <c>_</c> and a letter (both are "word" characters), so
    /// <c>\bauditLog\.WriteAsync</c> silently missed a field named
    /// <c>_auditLog</c> — an independent phase 64 review caught this. Matching
    /// without a boundary at all is deliberate: a false positive
    /// here only means one extra file needs a human look, which is the safe
    /// direction for a closed allowlist; a false negative is the actual risk.
    /// </remarks>
    private static readonly Regex CallSitePattern = new(
        @"\bAuditRecorder\.Write(?:OrThrow)?Async\s*\(|[Aa]udit[Ll]og\.WriteAsync\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    [Fact]
    public void Every_audit_write_call_site_is_a_reviewed_file()
    {
        var repoRoot = FindRepoRoot();
        var allowed = new HashSet<string>(AllowedCallSites, StringComparer.Ordinal);
        var unexpected = new List<string>();

        foreach (var file in Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');

            if (!CallSitePattern.IsMatch(File.ReadAllText(file)))
            {
                continue;
            }

            if (!allowed.Contains(relative))
            {
                unexpected.Add(relative);
            }
        }

        unexpected.ShouldBeEmpty(
            "A new call site writes to the audit trail but is not in AuditContentPolicyTests's " +
            "reviewed allowlist. Confirm it writes a governance/administration record — never " +
            "conversation content — then add it to AllowedCallSites.");
    }

    [Fact]
    public void Every_allowed_file_still_exists_and_still_calls_the_audit_log()
    {
        var repoRoot = FindRepoRoot();
        var stale = new List<string>();

        foreach (var relative in AllowedCallSites)
        {
            var path = Path.Combine(repoRoot, relative);

            if (!File.Exists(path) || !CallSitePattern.IsMatch(File.ReadAllText(path)))
            {
                stale.Add(relative);
            }
        }

        stale.ShouldBeEmpty(
            "An entry in AllowedCallSites no longer calls the audit log (or no longer exists); " +
            "remove it to keep the allowlist accurate.");
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
