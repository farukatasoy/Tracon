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
        _context = await PostgresTestContext.CreateAsync(fixture);
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
    protected override async ValueTask<IAuditLog> CreateLogAsync()
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
        _context = await PostgresTestContext.CreateAsync(fixture);
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
        _context = await PostgresTestContext.CreateAsync(fixture);
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
        _context = await PostgresTestContext.CreateAsync(fixture);
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
        _context = await PostgresTestContext.CreateAsync(fixture);
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
