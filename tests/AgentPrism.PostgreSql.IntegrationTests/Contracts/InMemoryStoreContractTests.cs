namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// Sozlesme testlerinin bellek ici uygulama uzerindeki kosumu.
/// </summary>
/// <remarks>
/// Bu sinif <c>AgentPrism.Core</c> icindeki uygulamayi test eder ancak burada durur:
/// sozlesmenin iki uygulama tarafindan da <em>ayni</em> kaynaktan dogrulanmasi,
/// aradaki farkin gozden kacmasini engeller. Veritabani gerektirmez.
/// </remarks>
public sealed class InMemoryAgentDefinitionStoreContractTests : AgentDefinitionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IAgentDefinitionStore> CreateStoreAsync()
        => ValueTask.FromResult<IAgentDefinitionStore>(new InMemoryAgentDefinitionStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryRunStoreContractTests : RunStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunStore>(new InMemoryRunStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemorySessionStoreContractTests : SessionStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ISessionStore> CreateStoreAsync()
        => ValueTask.FromResult<ISessionStore>(new InMemorySessionStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryToolInvocationContractTests : ToolInvocationContract
{
    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunStore>(new InMemoryRunStore());
}

/// <inheritdoc cref="InMemoryAgentDefinitionStoreContractTests" />
public sealed class InMemoryTraceStoreContractTests : TraceStoreContract
{
    /// <inheritdoc />
    protected override ValueTask<ITraceStore> CreateStoreAsync()
        => ValueTask.FromResult<ITraceStore>(new InMemoryTraceStore());

    /// <summary>
    /// Bellek ici span deposu bir calistirma kaydi aramaz; yabanci anahtar
    /// kisiti yoktur ve tohumlama gerekmez.
    /// </summary>
    protected override ValueTask SeedRunAsync(Guid runId) => default;
}
