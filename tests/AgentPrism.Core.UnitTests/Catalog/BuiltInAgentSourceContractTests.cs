using System.Reflection;
using AgentPrism.Core.UnitTests.Fakes;
using AgentPrism.Testing.Contracts;
using AgentPrism.Testing.Contracts.AgentSources;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Catalog;

public sealed class CodeAgentSourceContractTests : AgentSourceContract
{
    protected override string KnownAgentName => "code-contract";

    protected override ValueTask<IAgentSource> CreateSourceAsync()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        IAgentSource source = new CodeAgentSource(
            [CodeAgentRegistration.FromDefinition(TestData.Definition(KnownAgentName))],
            compiler,
            new CompiledAgentCache(),
            new ServiceCollection().BuildServiceProvider(),
            new FixedTenantContext());
        return new ValueTask<IAgentSource>(source);
    }
}

public sealed class DefinitionStoreAgentSourceContractTests : AgentSourceContract
{
    protected override string KnownAgentName => "database-contract";

    protected override async ValueTask<IAgentSource> CreateSourceAsync()
    {
        var context = new FixedTenantContext();
        var store = new InMemoryAgentDefinitionStore(context);
        await store.SaveAsync(TestData.Definition(KnownAgentName)).ConfigureAwait(false);
        return new DefinitionStoreAgentSource(
            store,
            new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()),
            new CompiledAgentCache(),
            context);
    }
}

public sealed class DefinitionStoreVersionedAgentSourceContractTests : VersionedAgentSourceContract
{
    protected override string KnownAgentName => "versioned-contract";

    protected override int KnownVersion => 1;

    protected override async ValueTask<IAgentSource> CreateSourceAsync()
    {
        var context = new FixedTenantContext();
        var store = new InMemoryAgentDefinitionStore(context);
        await store.SaveAsync(TestData.Definition(KnownAgentName)).ConfigureAwait(false);
        return new DefinitionStoreAgentSource(
            store,
            new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()),
            new CompiledAgentCache(),
            context);
    }
}

public sealed class DefinitionStoreTenantAwareAgentSourceContractTests : TenantAwareAgentSourceContract
{
    private readonly FixedTenantContext _context = new();

    protected override string KnownAgentName => "default-contract";

    protected override async ValueTask<IAgentSource> CreateSourceAsync()
    {
        var store = new InMemoryAgentDefinitionStore(_context);
        await store.SaveAsync(TestData.Definition(KnownAgentName)).ConfigureAwait(false);
        _context.TenantId = "contract-tenant-a";
        await store.SaveAsync(TestData.Definition("agent-a")).ConfigureAwait(false);
        _context.TenantId = "contract-tenant-b";
        await store.SaveAsync(TestData.Definition("agent-b")).ConfigureAwait(false);
        _context.TenantId = "default";

        return new DefinitionStoreAgentSource(
            store,
            new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()),
            new CompiledAgentCache(),
            _context);
    }

    protected override ValueTask<string> UseTenantAsync(string tenantId)
    {
        _context.TenantId = tenantId;
        return new ValueTask<string>(string.Equals(tenantId, "contract-tenant-a", StringComparison.Ordinal) ? "agent-a" : "agent-b");
    }
}

public sealed class AgentSourceContractCoverageTests
{
    [Fact]
    public void Every_agent_source_contract_has_a_derived_test()
        => ContractCoverage.MissingDerivedTypes(
            Assembly.GetExecutingAssembly(), ContractCoverage.AgentSourceContracts).ShouldBeEmpty();
}

internal sealed class FixedTenantContext : ITenantContext
{
    public string TenantId { get; set; } = "default";
}
