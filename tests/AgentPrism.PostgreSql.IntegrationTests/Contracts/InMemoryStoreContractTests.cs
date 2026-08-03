namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// Sozlesme testlerinin bellek ici uygulama uzerindeki kosumu.
/// </summary>
/// <remarks>
/// Bu sinif <c>AgentPrism.Core</c> icindeki uygulamayi test eder ancak burada durur:
/// sozlesmenin iki uygulama tarafindan da <em>ayni</em> kaynaktan dogrulanmasi,
/// aradaki farkin gozden kacmasini engeller. Veritabani gerektirmez.
/// </remarks>
public sealed class InMemoryAgentDefinitionStoreContractTests : AgentDefinitionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IAgentDefinitionStore> CreateStoreAsync()
        => ValueTask.FromResult<IAgentDefinitionStore>(new InMemoryAgentDefinitionStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryRunStoreContractTests : RunStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunStore>(new InMemoryRunStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemorySessionStoreContractTests : SessionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ISessionStore> CreateStoreAsync()
        => ValueTask.FromResult<ISessionStore>(new InMemorySessionStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryToolInvocationContractTests : ToolInvocationContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunStore>(new InMemoryRunStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryTraceStoreContractTests : TraceStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ITraceStore> CreateStoreAsync()
        => ValueTask.FromResult<ITraceStore>(new InMemoryTraceStore());

    /// <summary>
    /// Bellek ici span deposu bir calistirma kaydi aramaz; yabanci anahtar
    /// kisiti yoktur ve tohumlama gerekmez.
    /// </summary>
    protected override ValueTask SeedRunAsync(Guid runId) => default;
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryAuditLogContractTests : AuditLogContract
{
    /// <inheritdoc />
    protected override ValueTask<IAuditLog> CreateLogAsync()
        => ValueTask.FromResult<IAuditLog>(new InMemoryAuditLog());
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
