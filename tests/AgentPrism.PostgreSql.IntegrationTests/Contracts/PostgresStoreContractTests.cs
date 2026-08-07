using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>Sozlesme testlerinin PostgreSQL uygulamasi uzerindeki kosumu.</summary>
public sealed class PostgresAgentDefinitionStoreContractTests(PostgresFixture fixture) : AgentDefinitionStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAgentDefinitionStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture, AmbientTenant);
        return _context.AgentDefinitions;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresSkillScriptGrantContractTests(PostgresFixture fixture) : SkillScriptGrantContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISkillScriptGrantStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.SkillScriptGrants;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresAuditLogContractTests(PostgresFixture fixture) : AuditLogContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAuditLog> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.AuditLog;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRunStoreContractTests(PostgresFixture fixture) : RunStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture, AmbientTenant);
        return _context.Runs;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresSessionStoreContractTests(PostgresFixture fixture) : SessionStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISessionStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture, AmbientTenant);
        return _context.Sessions;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresToolInvocationContractTests(PostgresFixture fixture) : ToolInvocationContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture, AmbientTenant);
        return _context.Runs;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresAttachmentStoreContractTests(PostgresFixture fixture) : AttachmentStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAttachmentStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.Attachments;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresTraceStoreContractTests(PostgresFixture fixture) : TraceStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ITraceStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture, AmbientTenant);
        return _context.Traces;
    }

    /// <summary>
    /// <c>traces.run_id</c> yabanci anahtardir; calistirma kaydi olmadan span
    /// yazilamaz.
    /// </summary>
    protected override async ValueTask SeedRunAsync(Guid runId)
        => await _context!.Runs.StartRunAsync(TestData.Run(runId));

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresWorkflowDefinitionStoreContractTests(PostgresFixture fixture)
    : WorkflowDefinitionStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.Workflows;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresWorkflowCheckpointStoreContractTests(PostgresFixture fixture)
    : WorkflowCheckpointStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWorkflowCheckpointStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.WorkflowCheckpoints;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresJobStoreContractTests(PostgresFixture fixture) : JobStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IJobStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.Jobs;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresJobScheduleStoreContractTests(PostgresFixture fixture) : JobScheduleStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IJobScheduleStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.JobSchedules;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresEvalStoreContractTests(PostgresFixture fixture) : EvalStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IEvalStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.Evals;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresExperimentStoreContractTests(PostgresFixture fixture) : ExperimentStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IExperimentStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.Experiments;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresQuotaStoreContractTests(PostgresFixture fixture) : QuotaStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IQuotaStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.Quotas;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRetentionPolicyStoreContractTests(PostgresFixture fixture) : RetentionPolicyStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRetentionPolicyStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.RetentionPolicies;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresWebhookStoreContractTests(PostgresFixture fixture) : WebhookStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWebhookStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.Webhooks;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresVoiceSessionStoreContractTests(PostgresFixture fixture) : VoiceSessionStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IVoiceSessionStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.VoiceSessions;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRunScoreStoreContractTests(PostgresFixture fixture) : RunScoreStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunScoreStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.RunScores;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresSingletonLeaseStoreContractTests(PostgresFixture fixture) : SingletonLeaseStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISingletonLeaseStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.SingletonLeases;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresIdempotencyStoreContractTests(PostgresFixture fixture) : IdempotencyStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IIdempotencyStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.IdempotencyKeys;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresAgentSkillStoreContractTests(PostgresFixture fixture) : AgentSkillStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAgentSkillStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.AgentSkills;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresToolApprovalRuleStoreContractTests(PostgresFixture fixture) : ToolApprovalRuleStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IToolApprovalRuleStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.ApprovalRules;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresMcpServerStoreContractTests(PostgresFixture fixture) : McpServerStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IMcpServerStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.McpServers;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
public sealed class PostgresRetentionStoreContractTests(PostgresFixture fixture) : RetentionStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRetentionStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture);
        return _context.RetentionData;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Konusma kaydi hedefi secildi: kendi <c>tenant_id</c> sutunu vardir,
    /// yabanci anahtar tasimaz ve tek bir yazma ile tohumlanabilir.
    /// </remarks>
    protected override async ValueTask SeedOldRowAsync(string tenantId)
        => await _context!.VoiceSessions.SaveAsync(new VoiceSessionRecord
        {
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            SessionId = $"oturum-{Guid.NewGuid():N}",
            AgentName = "destek",
            StartedAt = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2020, 1, 1, 0, 5, 0, TimeSpan.Zero),
            Turns = 1,
        });

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}

/// <inheritdoc cref="PostgresAgentDefinitionStoreContractTests" />
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; gerekce urun kodundaki ile ayni.
public sealed class PostgresAgentFileStoreContractTests(PostgresFixture fixture) : AgentFileStoreContract
{
    private PostgresTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<Microsoft.Agents.AI.AgentFileStore> CreateStoreAsync()
    {
        _context = await PostgresTestContext.CreateAsync(fixture, AmbientTenant);
        return _context.AgentFiles;
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        await base.OnDisposeAsync();

        if (_context is not null)
        {
            await _context.DisposeAsync();
        }
    }
}
#pragma warning restore MAAI001
