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
        _context = await SqliteTestContext.CreateAsync(fixture);
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
    protected override async ValueTask<IAuditLog> CreateLogAsync()
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
        _context = await SqliteTestContext.CreateAsync(fixture);
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
        _context = await SqliteTestContext.CreateAsync(fixture);
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
        _context = await SqliteTestContext.CreateAsync(fixture);
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
        _context = await SqliteTestContext.CreateAsync(fixture);
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
