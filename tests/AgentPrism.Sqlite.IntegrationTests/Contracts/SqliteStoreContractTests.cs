using AgentPrism.Sqlite.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.Sqlite.IntegrationTests.Contracts;

/// <summary>Sozlesme testlerinin SQLite uygulamasi uzerindeki kosumu.</summary>
public sealed class SqliteAgentDefinitionStoreContractTests(SqliteFixture fixture) : AgentDefinitionStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAgentDefinitionStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteSkillScriptGrantContractTests(SqliteFixture fixture) : SkillScriptGrantContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISkillScriptGrantStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteAuditLogContractTests(SqliteFixture fixture) : AuditLogContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAuditLog> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRunStoreContractTests(SqliteFixture fixture) : RunStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteSessionStoreContractTests(SqliteFixture fixture) : SessionStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISessionStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteToolInvocationContractTests(SqliteFixture fixture) : ToolInvocationContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteAttachmentStoreContractTests(SqliteFixture fixture) : AttachmentStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAttachmentStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteTraceStoreContractTests(SqliteFixture fixture) : TraceStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ITraceStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteWorkflowDefinitionStoreContractTests(SqliteFixture fixture)
    : WorkflowDefinitionStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteWorkflowCheckpointStoreContractTests(SqliteFixture fixture)
    : WorkflowCheckpointStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWorkflowCheckpointStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteJobStoreContractTests(SqliteFixture fixture) : JobStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IJobStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteJobScheduleStoreContractTests(SqliteFixture fixture) : JobScheduleStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IJobScheduleStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteEvalStoreContractTests(SqliteFixture fixture) : EvalStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IEvalStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteExperimentStoreContractTests(SqliteFixture fixture) : ExperimentStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IExperimentStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteQuotaStoreContractTests(SqliteFixture fixture) : QuotaStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IQuotaStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRetentionPolicyStoreContractTests(SqliteFixture fixture) : RetentionPolicyStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRetentionPolicyStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteWebhookStoreContractTests(SqliteFixture fixture) : WebhookStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWebhookStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteApiKeyStoreContractTests(SqliteFixture fixture) : ApiKeyStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IApiKeyStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
        return _context.ApiKeys;
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteVoiceSessionStoreContractTests(SqliteFixture fixture) : VoiceSessionStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IVoiceSessionStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRunScoreStoreContractTests(SqliteFixture fixture) : RunScoreStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunScoreStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteSingletonLeaseStoreContractTests(SqliteFixture fixture) : SingletonLeaseStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISingletonLeaseStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteIdempotencyStoreContractTests(SqliteFixture fixture) : IdempotencyStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IIdempotencyStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteAgentSkillStoreContractTests(SqliteFixture fixture) : AgentSkillStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAgentSkillStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteToolApprovalRuleStoreContractTests(SqliteFixture fixture) : ToolApprovalRuleStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IToolApprovalRuleStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteMcpServerStoreContractTests(SqliteFixture fixture) : McpServerStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IMcpServerStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRetentionStoreContractTests(SqliteFixture fixture) : RetentionStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRetentionStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; gerekce urun kodundaki ile ayni.
public sealed class SqliteAgentFileStoreContractTests(SqliteFixture fixture) : AgentFileStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<Microsoft.Agents.AI.AgentFileStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqliteAgentDefinitionStoreContractTests" />
public sealed class SqliteRunInputStoreContractTests(SqliteFixture fixture) : RunInputStoreContract
{
    private SqliteTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunInputStore> CreateStoreAsync()
    {
        _context = await SqliteTestContext.CreateAsync(fixture);
        return _context.RunInputs;
    }

    /// <summary>
    /// <c>run_inputs.run_id</c> <c>runs</c> tablosuna yabanci anahtardir; girdi
    /// yazilmadan once satirin var olmasi gerekir.
    /// </summary>
    /// <inheritdoc />
    protected override async ValueTask PrepareRunAsync(Guid runId, string tenantId)
        => await _context!.Runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "sozlesme",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenantId,
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
