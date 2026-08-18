using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>Runs the contract tests against the PostgreSQL implementation.</summary>
public sealed class PostgresAgentDefinitionStoreContractTests(PostgresSchemaFixture schema)
    : AgentDefinitionStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAgentDefinitionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentDefinitions;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresSkillScriptGrantContractTests(PostgresSchemaFixture schema)
    : SkillScriptGrantContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISkillScriptGrantStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.SkillScriptGrants;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresAuditLogContractTests(PostgresSchemaFixture schema)
    : AuditLogContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAuditLog> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AuditLog;
    }

    /// <inheritdoc />
    protected override bool SupportsRawTamper => true;

    /// <inheritdoc />
    protected override string QualifiedAuditLogTable => $"{schema.Context.SchemaName}.audit_log";

    /// <inheritdoc />
    protected override async ValueTask ExecuteRawAsync(string sql) => await schema.Context.ExecuteAsync(sql);
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRunStoreContractTests(PostgresSchemaFixture schema)
    : RunStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Runs;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresSessionStoreContractTests(PostgresSchemaFixture schema)
    : SessionStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISessionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Sessions;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresToolInvocationContractTests(PostgresSchemaFixture schema)
    : ToolInvocationContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Runs;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresAttachmentStoreContractTests(PostgresSchemaFixture schema)
    : AttachmentStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAttachmentStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Attachments;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresTraceStoreContractTests(PostgresSchemaFixture schema)
    : TraceStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ITraceStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Traces;
    }

    /// <summary>
    /// <c>traces.run_id</c> is a foreign key; a span cannot be written without a
    /// run record.
    /// </summary>
    protected override async ValueTask SeedRunAsync(Guid runId)
        => await schema.Context.Runs.StartRunAsync(TestData.Run(runId));
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresWorkflowDefinitionStoreContractTests(PostgresSchemaFixture schema)
    : WorkflowDefinitionStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Workflows;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresWorkflowCheckpointStoreContractTests(PostgresSchemaFixture schema)
    : WorkflowCheckpointStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWorkflowCheckpointStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.WorkflowCheckpoints;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresJobStoreContractTests(PostgresSchemaFixture schema)
    : JobStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IJobStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Jobs;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresJobScheduleStoreContractTests(PostgresSchemaFixture schema)
    : JobScheduleStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IJobScheduleStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.JobSchedules;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresEvalStoreContractTests(PostgresSchemaFixture schema)
    : EvalStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IEvalStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Evals;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresExperimentStoreContractTests(PostgresSchemaFixture schema)
    : ExperimentStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IExperimentStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Experiments;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresQuotaStoreContractTests(PostgresSchemaFixture schema)
    : QuotaStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IQuotaStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Quotas;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRetentionPolicyStoreContractTests(PostgresSchemaFixture schema)
    : RetentionPolicyStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRetentionPolicyStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.RetentionPolicies;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresWebhookStoreContractTests(PostgresSchemaFixture schema)
    : WebhookStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWebhookStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Webhooks;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresApiKeyStoreContractTests(PostgresSchemaFixture schema)
    : ApiKeyStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IApiKeyStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.ApiKeys;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresVoiceSessionStoreContractTests(PostgresSchemaFixture schema)
    : VoiceSessionStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IVoiceSessionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.VoiceSessions;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRunScoreStoreContractTests(PostgresSchemaFixture schema)
    : RunScoreStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunScoreStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.RunScores;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresSingletonLeaseStoreContractTests(PostgresSchemaFixture schema)
    : SingletonLeaseStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISingletonLeaseStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.SingletonLeases;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresIdempotencyStoreContractTests(PostgresSchemaFixture schema)
    : IdempotencyStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IIdempotencyStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.IdempotencyKeys;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresAgentSkillStoreContractTests(PostgresSchemaFixture schema)
    : AgentSkillStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAgentSkillStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentSkills;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresToolApprovalRuleStoreContractTests(PostgresSchemaFixture schema)
    : ToolApprovalRuleStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IToolApprovalRuleStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.ApprovalRules;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresMcpServerStoreContractTests(PostgresSchemaFixture schema)
    : McpServerStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IMcpServerStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.McpServers;
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRetentionStoreContractTests(PostgresSchemaFixture schema)
    : RetentionStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRetentionStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.RetentionData;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The voice-session-record target was chosen: it has its own <c>tenant_id</c>
    /// column, carries no foreign key, and can be seeded with a single write.
    /// </remarks>
    protected override async ValueTask SeedOldRowAsync(string tenantId)
        => await schema.Context.VoiceSessions.SaveAsync(new VoiceSessionRecord
        {
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            SessionId = $"session-{Guid.NewGuid():N}",
            AgentName = "support",
            StartedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2020, 1, 1, 0, 5, 0, TimeSpan.Zero),
            Turns = 1,
        });
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; same rationale as in the product code.
public sealed class PostgresAgentFileStoreContractTests(PostgresSchemaFixture schema)
    : AgentFileStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<Microsoft.Agents.AI.AgentFileStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentFiles;
    }
}
#pragma warning restore MAAI001

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRunInputStoreContractTests(PostgresSchemaFixture schema)
    : RunInputStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunInputStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.RunInputs;
    }

    /// <summary>
    /// <c>run_inputs.run_id</c> is a foreign key to the <c>runs</c> table; the row
    /// must exist before an input is written.
    /// </summary>
    /// <inheritdoc />
    protected override async ValueTask PrepareRunAsync(Guid runId, string tenantId)
        => await schema.Context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "contract",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
        });
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresPendingApprovalStoreContractTests(PostgresSchemaFixture schema)
    : PendingApprovalStoreContract, IClassFixture<PostgresSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IPendingApprovalStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.PendingApprovals;
    }

    /// <summary>
    /// <c>pending_approvals.run_id</c> is a foreign key to the <c>runs</c> table;
    /// the row must exist before an approval is written.
    /// </summary>
    /// <inheritdoc />
    protected override async ValueTask PrepareRunAsync(Guid runId, string tenantId)
        => await schema.Context.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "contract",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
        });
}
