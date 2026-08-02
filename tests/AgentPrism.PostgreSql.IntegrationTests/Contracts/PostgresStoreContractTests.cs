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
