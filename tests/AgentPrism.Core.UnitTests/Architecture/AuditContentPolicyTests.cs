using System.Text.RegularExpressions;

namespace AgentPrism.Core.UnitTests.Architecture;

/// <summary>
/// Enforces the phase 64 rule that <c>audit_log</c> never receives conversation
/// content: it is reviewed, closed allowlist of the source files that are allowed
/// to write an audit entry at all.
/// </summary>
/// <remarks>
/// <para>
/// This is a boundary test, not a content-sniffing one: proving that a given
/// string does not "look like" conversation content is not reliable, so instead
/// every call SITE that writes to the audit trail
/// (<c>AuditRecorder.WriteAsync</c> or a direct <c>IAuditLog.WriteAsync</c>) must
/// be a file already reviewed and named below. A new call site fails this test
/// until a human adds it to the list — which is the point: the review happens
/// once, deliberately, not by guessing from a diff later. See phase 64's
/// evidence table (<c>docs/64-DENETIM-ZINCIRI-VE-VERI-KONUSU-HAKLARI.md</c>) for
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
    /// Every file allowed to call <c>AuditRecorder.WriteAsync</c> or a direct
    /// <c>IAuditLog.WriteAsync</c>, relative to the repository root.
    /// </summary>
    private static readonly string[] AllowedCallSites =
    [
        // AspNetCore endpoints — administrator actions.
        "src/AgentPrism.AspNetCore/Endpoints/ApiKeyEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/CatalogEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/DataSubjectEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/EvalEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/GovernanceEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/QuotaEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/RetentionEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/SessionEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/TenantProviderEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/TriggerEndpoints.cs",
        "src/AgentPrism.AspNetCore/Endpoints/WebhookEndpoints.cs",

        // AspNetCore internals — tool approval/result resolution, external call audit.
        "src/AgentPrism.AspNetCore/Internal/ClientToolResultResolver.cs",
        "src/AgentPrism.AspNetCore/Internal/ToolApprovalResolver.cs",
        "src/AgentPrism.AspNetCore/Security/ExternalCallAudit.cs",

        // Core — the write helper itself, and store decorators (all administrator
        // actions: agent/experiment/mcp-server/session/skill-script-grant/tenant/
        // tool-approval-rule/workflow-definition writes).
        "src/AgentPrism.Core/Audit/AuditRecorder.cs",
        "src/AgentPrism.Core/Audit/AuditingAgentDefinitionStore.cs",
        "src/AgentPrism.Core/Audit/AuditingExperimentStore.cs",
        "src/AgentPrism.Core/Audit/AuditingMcpServerStore.cs",
        "src/AgentPrism.Core/Audit/AuditingSessionStore.cs",
        "src/AgentPrism.Core/Audit/AuditingSkillScriptGrantStore.cs",
        "src/AgentPrism.Core/Audit/AuditingTenantStore.cs",
        "src/AgentPrism.Core/Audit/AuditingToolApprovalRuleStore.cs",
        "src/AgentPrism.Core/Audit/AuditingWorkflowDefinitionStore.cs",
        "src/AgentPrism.Core/Experiments/CanaryEvaluationService.cs",

        // Core — a content guard's BLOCK DECISION (rule name only, never the
        // blocked text — K-325) and a skill script run record (K-089).
        "src/AgentPrism.Core/Guards/ContentGuardPipeline.cs",
        "src/AgentPrism.Core/Skills/Scripts/SandboxedSkillScriptRunner.cs",
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
        @"\bAuditRecorder\.WriteAsync\s*\(|[Aa]udit[Ll]og\.WriteAsync\s*\(",
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

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root (AgentPrism.slnx) from the test output directory.");
    }
}
