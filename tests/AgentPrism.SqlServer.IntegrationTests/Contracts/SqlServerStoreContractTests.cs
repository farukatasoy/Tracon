using AgentPrism.SqlServer.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.SqlServer.IntegrationTests.Contracts;

/// <summary>Sozlesme testlerinin SQL Server uygulamasi uzerindeki kosumu.</summary>
public sealed class SqlServerAgentDefinitionStoreContractTests(SqlServerFixture fixture) : AgentDefinitionStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAgentDefinitionStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerSkillScriptGrantContractTests(SqlServerFixture fixture) : SkillScriptGrantContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISkillScriptGrantStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerAuditLogContractTests(SqlServerFixture fixture) : AuditLogContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAuditLog> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRunStoreContractTests(SqlServerFixture fixture) : RunStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerSessionStoreContractTests(SqlServerFixture fixture) : SessionStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISessionStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerToolInvocationContractTests(SqlServerFixture fixture) : ToolInvocationContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerAttachmentStoreContractTests(SqlServerFixture fixture) : AttachmentStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAttachmentStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerTraceStoreContractTests(SqlServerFixture fixture) : TraceStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ITraceStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerWorkflowDefinitionStoreContractTests(SqlServerFixture fixture)
    : WorkflowDefinitionStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWorkflowDefinitionStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerWorkflowCheckpointStoreContractTests(SqlServerFixture fixture)
    : WorkflowCheckpointStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWorkflowCheckpointStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerJobStoreContractTests(SqlServerFixture fixture) : JobStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IJobStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerJobScheduleStoreContractTests(SqlServerFixture fixture) : JobScheduleStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IJobScheduleStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerEvalStoreContractTests(SqlServerFixture fixture) : EvalStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IEvalStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerExperimentStoreContractTests(SqlServerFixture fixture) : ExperimentStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IExperimentStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerQuotaStoreContractTests(SqlServerFixture fixture) : QuotaStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IQuotaStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRetentionPolicyStoreContractTests(SqlServerFixture fixture) : RetentionPolicyStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRetentionPolicyStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerWebhookStoreContractTests(SqlServerFixture fixture) : WebhookStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IWebhookStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerVoiceSessionStoreContractTests(SqlServerFixture fixture) : VoiceSessionStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IVoiceSessionStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRunScoreStoreContractTests(SqlServerFixture fixture) : RunScoreStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunScoreStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerSingletonLeaseStoreContractTests(SqlServerFixture fixture) : SingletonLeaseStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<ISingletonLeaseStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerIdempotencyStoreContractTests(SqlServerFixture fixture) : IdempotencyStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IIdempotencyStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerAgentSkillStoreContractTests(SqlServerFixture fixture) : AgentSkillStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IAgentSkillStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerToolApprovalRuleStoreContractTests(SqlServerFixture fixture) : ToolApprovalRuleStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IToolApprovalRuleStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerMcpServerStoreContractTests(SqlServerFixture fixture) : McpServerStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IMcpServerStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRetentionStoreContractTests(SqlServerFixture fixture) : RetentionStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRetentionStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
#pragma warning disable MAAI001 // AgentFileStore "evaluation purposes only"; gerekce urun kodundaki ile ayni.
public sealed class SqlServerAgentFileStoreContractTests(SqlServerFixture fixture) : AgentFileStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<Microsoft.Agents.AI.AgentFileStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture, AmbientTenant);
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

/// <inheritdoc cref="SqlServerAgentDefinitionStoreContractTests" />
public sealed class SqlServerRunInputStoreContractTests(SqlServerFixture fixture) : RunInputStoreContract
{
    private SqlServerTestContext? _context;

    /// <inheritdoc />
    protected override async ValueTask<IRunInputStore> CreateStoreAsync()
    {
        _context = await SqlServerTestContext.CreateAsync(fixture);
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
