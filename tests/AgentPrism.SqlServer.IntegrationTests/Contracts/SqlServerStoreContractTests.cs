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
        _context = await SqlServerTestContext.CreateAsync(fixture);
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
    protected override async ValueTask<IAuditLog> CreateLogAsync()
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
        _context = await SqlServerTestContext.CreateAsync(fixture);
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
        _context = await SqlServerTestContext.CreateAsync(fixture);
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
        _context = await SqlServerTestContext.CreateAsync(fixture);
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
        _context = await SqlServerTestContext.CreateAsync(fixture);
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
