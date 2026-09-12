using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Enforces that every store holding GOVERNANCE state resolves to an
/// audit-trail decorator, and that the stores deliberately left out stay out
/// for a written reason (F-170).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The defect this closes was a PARTIAL trail, which is worse than a
/// missing one because it reads as complete: <c>ISkillScriptGrantStore</c>
/// was audited while <c>IAgentSkillStore</c> was not, so the trail recorded
/// who PERMITTED a script to run and not who WROTE it — and a skill carries
/// both the model's instructions and the scripts themselves. Phase 121's
/// independent audit found it; nothing mechanical would have.
/// </para>
/// <para>
/// This resolves from a real container rather than scanning source: the
/// decorator only matters if it is REGISTERED, and phase 9's own pattern is
/// that every persistence provider must re-apply it (a provider's
/// <c>services.Replace</c> silently drops one otherwise). <c>IAuditDecorated</c>
/// is the marker each decorator already implements for
/// <c>StorePersistence</c>, so no new surface is introduced to test this.
/// </para>
/// <para>
/// 🚨 The exclusion list below is the judgment half and is NOT mechanical: a
/// new store lands in neither list and fails <see
/// cref="Every_store_is_either_audited_or_excluded_for_a_written_reason"/>,
/// which forces the phase adding it to decide, in writing, which it is. That
/// is the gate — the audited list alone would have stayed green through
/// exactly the miss that created this test.
/// </para>
/// </remarks>
public sealed class AuditCoverageTests
{
    /// <summary>Stores whose writes are a governance action and must reach the trail.</summary>
    private static readonly Type[] AuditedStores =
    [
        typeof(IAgentDefinitionStore),
        typeof(IAgentSkillStore),
        typeof(ISkillScriptGrantStore),
        typeof(IWorkflowDefinitionStore),
        typeof(IMcpServerStore),
        typeof(IToolApprovalRuleStore),
        typeof(ISessionStore),
        typeof(IExperimentStore),
        typeof(ITenantStore),
    ];

    /// <summary>
    /// Stores deliberately NOT decorated, each with the reason it is not a
    /// governance action.
    /// </summary>
    /// <remarks>
    /// Two distinct reasons, not one. MACHINE-WRITTEN: the row is produced by
    /// the runtime itself, so there is no actor to record and the row IS the
    /// record (a run, a job, a trace, a lease). ENDPOINT-AUDITED: the write is
    /// a governance action and DOES reach the trail, but through an explicit
    /// <c>AuditRecorder</c> call at the endpoint rather than a decorator,
    /// because the audited unit is the HTTP operation and not the store write
    /// (measured 2026-09-06: <c>ApiKeyEndpoints</c>, <c>GovernanceEndpoints</c>,
    /// <c>RetentionEndpoints</c>, <c>TenantProviderEndpoints</c>,
    /// <c>TriggerEndpoints</c>, <c>WebhookEndpoints</c>, <c>QuotaEndpoints</c>,
    /// <c>EvalEndpoints</c> all call it).
    /// </remarks>
    private static readonly Dictionary<Type, string> ExcludedStores = new()
    {
        [typeof(IRunStore)] = "machine-written: a run row IS the record of the run",
        [typeof(IRunInputStore)] = "machine-written: the run's own input payload",
        [typeof(IRunScoreStore)] = "machine-written: judge and feedback output",
        [typeof(ITraceStore)] = "machine-written: observability spans",
        [typeof(IJobStore)] = "machine-written: queue state",
        [typeof(IJobScheduleStore)] = "machine-written: scheduler bookkeeping",
        [typeof(IIdempotencyStore)] = "machine-written: request de-duplication keys",
        [typeof(ISingletonLeaseStore)] = "machine-written: coordination lease",
        [typeof(IQuotaStore)] = "machine-written: usage counters",
        [typeof(IRetentionStore)] = "machine-written: retention sweep bookkeeping",
        [typeof(IWorkflowCheckpointStore)] = "machine-written: workflow execution state",
        [typeof(IPendingApprovalStore)] = "machine-written: the approval inbox; the DECISION is audited at the endpoint",
        [typeof(IVectorSearchStore)] = "machine-written: embedding index",
        [typeof(IAttachmentStore)] = "machine-written: uploaded blob content",
        [typeof(IEvalStore)] = "endpoint-audited: EvalEndpoints calls AuditRecorder",
        [typeof(IApiKeyStore)] = "endpoint-audited: ApiKeyEndpoints calls AuditRecorder",
        [typeof(IRetentionPolicyStore)] = "endpoint-audited: RetentionEndpoints calls AuditRecorder",
        [typeof(ITenantEgressPolicyStore)] = "endpoint-audited: GovernanceEndpoints calls AuditRecorder",
        [typeof(ITenantProviderBindingStore)] = "endpoint-audited: TenantProviderEndpoints calls AuditRecorder",
        [typeof(IInboundTriggerStore)] = "endpoint-audited: TriggerEndpoints calls AuditRecorder",
        [typeof(IWebhookStore)] = "endpoint-audited: WebhookEndpoints calls AuditRecorder",
        [typeof(IDataSubjectStore)] = "endpoint-audited: the erasure operation is audited, not each row",
        [typeof(IConversationBranchStore)] = "endpoint-audited: SessionEndpoints writes 'session.branch'",
        [typeof(IVoiceSessionStore)] = "machine-written: live voice session bookkeeping",
    };

    [Fact]
    public void Every_governance_store_resolves_to_an_audit_decorator()
    {
        using var provider = BuildProvider();

        var undecorated = AuditedStores
            .Where(store => provider.GetService(store) is not IAuditDecorated)
            .Select(store => store.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        undecorated.ShouldBeEmpty(
            customMessage: "These stores hold governance state but do not resolve to an " +
                           $"IAuditDecorated decorator, so their writes never reach the audit trail: {string.Join(", ", undecorated)}");
    }

    /// <summary>
    /// A store excluded from the trail must actually be undecorated — an
    /// entry that drifts into the audited set silently makes its reason a lie.
    /// </summary>
    [Fact]
    public void An_excluded_store_is_really_undecorated()
    {
        using var provider = BuildProvider();

        var unexpectedlyDecorated = ExcludedStores.Keys
            .Where(store => provider.GetService(store) is IAuditDecorated)
            .Select(store => store.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        unexpectedlyDecorated.ShouldBeEmpty(
            customMessage: "These stores are listed as deliberately unaudited but now resolve to a " +
                           $"decorator; move them to AuditedStores: {string.Join(", ", unexpectedlyDecorated)}");
    }

    /// <summary>
    /// The gate itself: a store interface that appears in NEITHER list fails
    /// here, so a new one cannot be added without deciding whether its writes
    /// are a governance action.
    /// </summary>
    [Fact]
    public void Every_store_is_either_audited_or_excluded_for_a_written_reason()
    {
        var classified = AuditedStores.Concat(ExcludedStores.Keys).ToHashSet();

        var unclassified = typeof(IAgentDefinitionStore).Assembly
            .GetExportedTypes()
            .Where(static type => type.IsInterface && type.Name.EndsWith("Store", StringComparison.Ordinal))
            .Where(type => !classified.Contains(type))
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        unclassified.ShouldBeEmpty(
            customMessage: "These store interfaces are in neither AuditCoverageTests list. Decide whether " +
                           "each one's writes are a governance action: add it to AuditedStores AND give it " +
                           "an Auditing* decorator (in Tracon.Core AND in every persistence provider's " +
                           "own registration), or add it to ExcludedStores with the reason it is not. " +
                           $"Unclassified: {string.Join(", ", unclassified)}");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        return services.BuildServiceProvider();
    }
}
