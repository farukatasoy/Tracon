using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

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
