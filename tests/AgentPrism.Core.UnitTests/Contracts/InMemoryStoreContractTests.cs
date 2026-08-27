using AgentPrism.Testing.Contracts.Storage;
namespace AgentPrism.Core.UnitTests.Contracts;

/// <summary>
/// Runs the contract tests against the in-memory implementation.
/// </summary>
/// <remarks>
/// This class tests the implementation in <c>AgentPrism.Core</c>, but its role does not
/// stop there: verifying the contract from the <em>same</em> source for both
/// implementations prevents a divergence between them from going unnoticed. It
/// requires no database and no <c>[assembly: AssemblyFixture]</c> — that is
/// why it lives here rather than in an integration-test project that starts
/// a real container for the whole assembly.
/// </remarks>
public sealed class InMemoryAgentDefinitionStoreContractTests : AgentDefinitionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IAgentDefinitionStore> CreateStoreAsync()
        => ValueTask.FromResult<IAgentDefinitionStore>(new InMemoryAgentDefinitionStore(AmbientTenant));
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryRunStoreContractTests : RunStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunStore>(new InMemoryRunStore(tenantContext: AmbientTenant));
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemorySessionStoreContractTests : SessionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ISessionStore> CreateStoreAsync()
        => ValueTask.FromResult<ISessionStore>(new InMemorySessionStore(AmbientTenant));
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryToolInvocationContractTests : ToolInvocationContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunStore>(new InMemoryRunStore(tenantContext: AmbientTenant));
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryTraceStoreContractTests : TraceStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ITraceStore> CreateStoreAsync()
        => ValueTask.FromResult<ITraceStore>(new InMemoryTraceStore(AmbientTenant));

    /// <summary>
    /// The in-memory span store does not look for a run record; there is no
    /// foreign key constraint, so no seeding is needed.
    /// </summary>
    protected override ValueTask SeedRunAsync(Guid runId) => default;
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryAuditLogContractTests : AuditLogContract
{
    /// <inheritdoc />
    protected override ValueTask<IAuditLog> CreateStoreAsync()
        => ValueTask.FromResult<IAuditLog>(new InMemoryAuditLog(AmbientTenant));
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemorySkillScriptGrantContractTests : SkillScriptGrantContract
{
    /// <inheritdoc />
    protected override ValueTask<ISkillScriptGrantStore> CreateStoreAsync()
        => ValueTask.FromResult<ISkillScriptGrantStore>(new InMemorySkillScriptGrantStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryAttachmentStoreContractTests : AttachmentStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IAttachmentStore> CreateStoreAsync()
        => ValueTask.FromResult<IAttachmentStore>(new InMemoryAttachmentStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryWorkflowDefinitionStoreContractTests : WorkflowDefinitionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
        => ValueTask.FromResult<IWorkflowDefinitionStore>(new InMemoryWorkflowDefinitionStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryWorkflowCheckpointStoreContractTests : WorkflowCheckpointStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IWorkflowCheckpointStore> CreateStoreAsync()
        => ValueTask.FromResult<IWorkflowCheckpointStore>(new InMemoryWorkflowCheckpointStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryJobStoreContractTests : JobStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IJobStore> CreateStoreAsync()
        => ValueTask.FromResult<IJobStore>(new InMemoryJobStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryJobScheduleStoreContractTests : JobScheduleStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IJobScheduleStore> CreateStoreAsync()
        => ValueTask.FromResult<IJobScheduleStore>(new InMemoryJobScheduleStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryEvalStoreContractTests : EvalStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IEvalStore> CreateStoreAsync()
        => ValueTask.FromResult<IEvalStore>(new InMemoryEvalStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryExperimentStoreContractTests : ExperimentStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IExperimentStore> CreateStoreAsync()
        => ValueTask.FromResult<IExperimentStore>(new InMemoryExperimentStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryQuotaStoreContractTests : QuotaStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IQuotaStore> CreateStoreAsync()
        => ValueTask.FromResult<IQuotaStore>(new InMemoryQuotaStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryWebhookStoreContractTests : WebhookStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IWebhookStore> CreateStoreAsync()
        => ValueTask.FromResult<IWebhookStore>(new InMemoryWebhookStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryApiKeyStoreContractTests : ApiKeyStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IApiKeyStore> CreateStoreAsync()
        => ValueTask.FromResult<IApiKeyStore>(new InMemoryApiKeyStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryTenantProviderBindingStoreContractTests : TenantProviderBindingStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ITenantProviderBindingStore> CreateStoreAsync()
        => ValueTask.FromResult<ITenantProviderBindingStore>(new InMemoryTenantProviderBindingStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryTenantEgressPolicyStoreContractTests : TenantEgressPolicyStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ITenantEgressPolicyStore> CreateStoreAsync()
        => ValueTask.FromResult<ITenantEgressPolicyStore>(new InMemoryTenantEgressPolicyStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryInboundTriggerStoreContractTests : InboundTriggerStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IInboundTriggerStore> CreateStoreAsync()
        => ValueTask.FromResult<IInboundTriggerStore>(new InMemoryInboundTriggerStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryRetentionPolicyStoreContractTests : RetentionPolicyStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IRetentionPolicyStore> CreateStoreAsync()
        => ValueTask.FromResult<IRetentionPolicyStore>(new InMemoryRetentionPolicyStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryVoiceSessionStoreContractTests : VoiceSessionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IVoiceSessionStore> CreateStoreAsync()
        => ValueTask.FromResult<IVoiceSessionStore>(new InMemoryVoiceSessionStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryRunScoreStoreContractTests : RunScoreStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunScoreStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunScoreStore>(new InMemoryRunScoreStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemorySingletonLeaseStoreContractTests : SingletonLeaseStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ISingletonLeaseStore> CreateStoreAsync()
        => ValueTask.FromResult<ISingletonLeaseStore>(new InMemorySingletonLeaseStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryIdempotencyStoreContractTests : IdempotencyStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IIdempotencyStore> CreateStoreAsync()
        => ValueTask.FromResult<IIdempotencyStore>(new InMemoryIdempotencyStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryAgentSkillStoreContractTests : AgentSkillStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IAgentSkillStore> CreateStoreAsync()
        => ValueTask.FromResult<IAgentSkillStore>(new InMemoryAgentSkillStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryToolApprovalRuleStoreContractTests : ToolApprovalRuleStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IToolApprovalRuleStore> CreateStoreAsync()
        => ValueTask.FromResult<IToolApprovalRuleStore>(new InMemoryToolApprovalRuleStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryMcpServerStoreContractTests : McpServerStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IMcpServerStore> CreateStoreAsync()
        => ValueTask.FromResult<IMcpServerStore>(new InMemoryMcpServerStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryRunInputStoreContractTests : RunInputStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunInputStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunInputStore>(new InMemoryRunInputStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryPendingApprovalStoreContractTests : PendingApprovalStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IPendingApprovalStore> CreateStoreAsync()
        => ValueTask.FromResult<IPendingApprovalStore>(new InMemoryPendingApprovalStore(AmbientTenant));
}
