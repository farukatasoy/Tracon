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
