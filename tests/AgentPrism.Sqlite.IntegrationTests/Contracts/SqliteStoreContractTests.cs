using AgentPrism.Sqlite.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.Sqlite.IntegrationTests.Contracts;

/// <summary>Runs the contract tests against the SQLite implementation.</summary>
public sealed class SqliteAgentDefinitionStoreContractTests(SqliteSchemaFixture schema)
    : AgentDefinitionStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAgentDefinitionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentDefinitions;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteSkillScriptGrantContractTests(SqliteSchemaFixture schema)
    : SkillScriptGrantContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISkillScriptGrantStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.SkillScriptGrants;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteAuditLogContractTests(SqliteSchemaFixture schema)
    : AuditLogContract, IClassFixture<SqliteSchemaFixture>
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
    protected override string QualifiedAuditLogTable => $"{schema.Context.TablePrefix}audit_log";

    /// <inheritdoc />
    protected override async ValueTask ExecuteRawAsync(string sql) => await schema.Context.ExecuteAsync(sql);

    /// <inheritdoc />
    protected override string FormatIdForRawSql(Guid id)
        => id.ToString("D", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRunStoreContractTests(SqliteSchemaFixture schema)
    : RunStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Runs;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteSessionStoreContractTests(SqliteSchemaFixture schema)
    : SessionStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISessionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Sessions;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteToolInvocationContractTests(SqliteSchemaFixture schema)
    : ToolInvocationContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Runs;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteAttachmentStoreContractTests(SqliteSchemaFixture schema)
    : AttachmentStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAttachmentStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Attachments;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteTraceStoreContractTests(SqliteSchemaFixture schema)
    : TraceStoreContract, IClassFixture<SqliteSchemaFixture>
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteWorkflowDefinitionStoreContractTests(SqliteSchemaFixture schema)
    : WorkflowDefinitionStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Workflows;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteWorkflowCheckpointStoreContractTests(SqliteSchemaFixture schema)
    : WorkflowCheckpointStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWorkflowCheckpointStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.WorkflowCheckpoints;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteJobStoreContractTests(SqliteSchemaFixture schema)
    : JobStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IJobStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Jobs;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteJobScheduleStoreContractTests(SqliteSchemaFixture schema)
    : JobScheduleStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IJobScheduleStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.JobSchedules;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteEvalStoreContractTests(SqliteSchemaFixture schema)
    : EvalStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IEvalStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Evals;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteExperimentStoreContractTests(SqliteSchemaFixture schema)
    : ExperimentStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IExperimentStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Experiments;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteQuotaStoreContractTests(SqliteSchemaFixture schema)
    : QuotaStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IQuotaStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Quotas;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRetentionPolicyStoreContractTests(SqliteSchemaFixture schema)
    : RetentionPolicyStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRetentionPolicyStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.RetentionPolicies;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteWebhookStoreContractTests(SqliteSchemaFixture schema)
    : WebhookStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IWebhookStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.Webhooks;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteApiKeyStoreContractTests(SqliteSchemaFixture schema)
    : ApiKeyStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IApiKeyStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.ApiKeys;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteVoiceSessionStoreContractTests(SqliteSchemaFixture schema)
    : VoiceSessionStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IVoiceSessionStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.VoiceSessions;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRunScoreStoreContractTests(SqliteSchemaFixture schema)
    : RunScoreStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IRunScoreStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.RunScores;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteSingletonLeaseStoreContractTests(SqliteSchemaFixture schema)
    : SingletonLeaseStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<ISingletonLeaseStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.SingletonLeases;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteIdempotencyStoreContractTests(SqliteSchemaFixture schema)
    : IdempotencyStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IIdempotencyStore> CreateStoreAsync()
    {
        await schema.ResetAsync();
        return schema.Context.IdempotencyKeys;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteAgentSkillStoreContractTests(SqliteSchemaFixture schema)
    : AgentSkillStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IAgentSkillStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.AgentSkills;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteToolApprovalRuleStoreContractTests(SqliteSchemaFixture schema)
    : ToolApprovalRuleStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IToolApprovalRuleStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.ApprovalRules;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteMcpServerStoreContractTests(SqliteSchemaFixture schema)
    : McpServerStoreContract, IClassFixture<SqliteSchemaFixture>
{
    /// <inheritdoc />
    protected override async ValueTask<IMcpServerStore> CreateStoreAsync()
    {
        UseAmbientTenant(schema.Tenant);
        await schema.ResetAsync();
        return schema.Context.McpServers;
    }
}

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRetentionStoreContractTests(SqliteSchemaFixture schema)
    : RetentionStoreContract, IClassFixture<SqliteSchemaFixture>
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; same rationale as in the product code.
public sealed class SqliteAgentFileStoreContractTests(SqliteSchemaFixture schema)
    : AgentFileStoreContract, IClassFixture<SqliteSchemaFixture>
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRunInputStoreContractTests(SqliteSchemaFixture schema)
    : RunInputStoreContract, IClassFixture<SqliteSchemaFixture>
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqlitePendingApprovalStoreContractTests(SqliteSchemaFixture schema)
    : PendingApprovalStoreContract, IClassFixture<SqliteSchemaFixture>
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
