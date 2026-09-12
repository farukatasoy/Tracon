using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests.Contracts;

/// <summary>Runs the contract tests against the SQL Server implementation.</summary>
public sealed class SqlServerAgentDefinitionStoreContractTests(SqlServerSchemaFixture schema)
    : AgentDefinitionStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAgentDefinitionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentDefinitions;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerSkillScriptGrantContractTests(SqlServerSchemaFixture schema)
    : SkillScriptGrantContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISkillScriptGrantStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.SkillScriptGrants;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerAuditLogContractTests(SqlServerSchemaFixture schema)
    : AuditLogContract, IClassFixture<SqlServerSchemaFixture>
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRunStoreContractTests(SqlServerSchemaFixture schema)
    : RunStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Runs;
    }

    /// <inheritdoc />
    protected override ValueTask<IRunScoreStore?> CreateScoreStoreAsync()
        => ValueTask.FromResult<IRunScoreStore?>(schema.Context.RunScores);
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerSessionStoreContractTests(SqlServerSchemaFixture schema)
    : SessionStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISessionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Sessions;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerToolInvocationContractTests(SqlServerSchemaFixture schema)
    : ToolInvocationContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Runs;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerAttachmentStoreContractTests(SqlServerSchemaFixture schema)
    : AttachmentStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAttachmentStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Attachments;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerTraceStoreContractTests(SqlServerSchemaFixture schema)
    : TraceStoreContract, IClassFixture<SqlServerSchemaFixture>
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerWorkflowDefinitionStoreContractTests(SqlServerSchemaFixture schema)
    : WorkflowDefinitionStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Workflows;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerWorkflowCheckpointStoreContractTests(SqlServerSchemaFixture schema)
    : WorkflowCheckpointStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWorkflowCheckpointStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.WorkflowCheckpoints;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerJobStoreContractTests(SqlServerSchemaFixture schema)
    : JobStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IJobStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Jobs;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerJobScheduleStoreContractTests(SqlServerSchemaFixture schema)
    : JobScheduleStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IJobScheduleStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.JobSchedules;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerEvalStoreContractTests(SqlServerSchemaFixture schema)
    : EvalStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IEvalStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Evals;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerExperimentStoreContractTests(SqlServerSchemaFixture schema)
    : ExperimentStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IExperimentStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Experiments;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerQuotaStoreContractTests(SqlServerSchemaFixture schema)
    : QuotaStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IQuotaStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Quotas;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRetentionPolicyStoreContractTests(SqlServerSchemaFixture schema)
    : RetentionPolicyStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRetentionPolicyStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.RetentionPolicies;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerWebhookStoreContractTests(SqlServerSchemaFixture schema)
    : WebhookStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWebhookStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Webhooks;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerApiKeyStoreContractTests(SqlServerSchemaFixture schema)
    : ApiKeyStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IApiKeyStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.ApiKeys;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerTenantProviderBindingStoreContractTests(SqlServerSchemaFixture schema)
    : TenantProviderBindingStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ITenantProviderBindingStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.TenantProviderBindings;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerInboundTriggerStoreContractTests(SqlServerSchemaFixture schema)
    : InboundTriggerStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IInboundTriggerStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.InboundTriggers;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerTenantEgressPolicyStoreContractTests(SqlServerSchemaFixture schema)
    : TenantEgressPolicyStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ITenantEgressPolicyStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.TenantEgressPolicies;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerVoiceSessionStoreContractTests(SqlServerSchemaFixture schema)
    : VoiceSessionStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IVoiceSessionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.VoiceSessions;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRunScoreStoreContractTests(SqlServerSchemaFixture schema)
    : RunScoreStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunScoreStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.RunScores;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerSingletonLeaseStoreContractTests(SqlServerSchemaFixture schema)
    : SingletonLeaseStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISingletonLeaseStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.SingletonLeases;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerIdempotencyStoreContractTests(SqlServerSchemaFixture schema)
    : IdempotencyStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IIdempotencyStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.IdempotencyKeys;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerAgentSkillStoreContractTests(SqlServerSchemaFixture schema)
    : AgentSkillStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAgentSkillStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentSkills;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerToolApprovalRuleStoreContractTests(SqlServerSchemaFixture schema)
    : ToolApprovalRuleStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IToolApprovalRuleStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.ApprovalRules;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerMcpServerStoreContractTests(SqlServerSchemaFixture schema)
    : McpServerStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IMcpServerStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.McpServers;
    }
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRetentionStoreContractTests(SqlServerSchemaFixture schema)
    : RetentionStoreContract, IClassFixture<SqlServerSchemaFixture>
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
            Id = TraconId.NewId(),
            TenantId = tenantId,
            SessionId = $"session-{Guid.NewGuid():N}",
            AgentName = "support",
            StartedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2020, 1, 1, 0, 5, 0, TimeSpan.Zero),
            Turns = 1,
        });
}

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; same rationale as in the product code.
public sealed class SqlServerAgentFileStoreContractTests(SqlServerSchemaFixture schema)
    : AgentFileStoreContract, IClassFixture<SqlServerSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<Microsoft.Agents.AI.AgentFileStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentFiles;
    }

    /// <inheritdoc />
    protected override void EnterRunScope()
        => TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            AgentName = "isolation-agent",
        });

    /// <inheritdoc />
    protected override void ExitRunScope() => TraconRunContext.SetCurrent(null);
}
#pragma warning restore MAAI001

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRunInputStoreContractTests(SqlServerSchemaFixture schema)
    : RunInputStoreContract, IClassFixture<SqlServerSchemaFixture>
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerPendingApprovalStoreContractTests(SqlServerSchemaFixture schema)
    : PendingApprovalStoreContract, IClassFixture<SqlServerSchemaFixture>
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
