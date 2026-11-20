using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Testing.Contracts;
using Tracon.Testing.Contracts.AgentSources;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// The shipped contract suites, run against the in-memory definition sources the tests use and against the adapter
/// that joins a definition source to the catalog.
/// </summary>
/// <remarks>
/// Two things are pinned: the definition source suites hold for a plain implementation, and the adapter passes the
/// <see cref="IAgentSource"/> suites a hand-written source must pass, so a definition source behaves like any other
/// source from the catalog's side.
/// </remarks>
public sealed class InMemoryDefinitionSourceContractTests : AgentDefinitionSourceContract
{
    protected override string KnownAgentName => "definition-contract";

    protected override ValueTask<IAgentDefinitionSource> CreateSourceAsync()
        => new(new InMemoryDefinitionSource().Add(TestData.Definition(KnownAgentName)));
}

public sealed class VersionedInMemoryDefinitionSourceContractTests : VersionedAgentDefinitionSourceContract
{
    protected override string KnownAgentName => "versioned-definition-contract";

    protected override int KnownVersion => 1;

    protected override ValueTask<IAgentDefinitionSource> CreateSourceAsync()
        => new(new VersionedInMemoryDefinitionSource().Add(TestData.Definition(KnownAgentName)));
}

public sealed class TenantAwareInMemoryDefinitionSourceContractTests : TenantAwareAgentDefinitionSourceContract
{
    private readonly FixedTenantContext _context = new();

    protected override string KnownAgentName => "default-definition-contract";

    protected override ValueTask<IAgentDefinitionSource> CreateSourceAsync()
    {
        var source = new InMemoryDefinitionSource(tenant: () => _context.TenantId)
            .Add(TestData.Definition(KnownAgentName))
            .Add(TestData.Definition("agent-a"), "contract-tenant-a")
            .Add(TestData.Definition("agent-b"), "contract-tenant-b");

        return new ValueTask<IAgentDefinitionSource>(source);
    }

    protected override ValueTask<string> UseTenantAsync(string tenantId)
    {
        _context.TenantId = tenantId;

        return new ValueTask<string>(string.Equals(tenantId, "contract-tenant-a", StringComparison.Ordinal) ? "agent-a" : "agent-b");
    }
}

public sealed class DefinitionSourceAdapterContractTests : AgentSourceContract
{
    protected override string KnownAgentName => "adapter-contract";

    protected override ValueTask<IAgentSource> CreateSourceAsync()
        => new(DefinitionSourceAdapters.Wrap(new InMemoryDefinitionSource().Add(TestData.Definition(KnownAgentName)), new FixedTenantContext()));
}

public sealed class VersionedDefinitionSourceAdapterContractTests : VersionedAgentSourceContract
{
    protected override string KnownAgentName => "versioned-adapter-contract";

    protected override int KnownVersion => 1;

    protected override ValueTask<IAgentSource> CreateSourceAsync()
        => new(DefinitionSourceAdapters.Wrap(new VersionedInMemoryDefinitionSource().Add(TestData.Definition(KnownAgentName)), new FixedTenantContext()));
}

public sealed class TenantAwareDefinitionSourceAdapterContractTests : TenantAwareAgentSourceContract
{
    private readonly FixedTenantContext _context = new();

    protected override string KnownAgentName => "default-adapter-contract";

    protected override ValueTask<IAgentSource> CreateSourceAsync()
    {
        var source = new InMemoryDefinitionSource(tenant: () => _context.TenantId)
            .Add(TestData.Definition(KnownAgentName))
            .Add(TestData.Definition("agent-a"), "contract-tenant-a")
            .Add(TestData.Definition("agent-b"), "contract-tenant-b");

        return new ValueTask<IAgentSource>(DefinitionSourceAdapters.Wrap(source, _context));
    }

    protected override ValueTask<string> UseTenantAsync(string tenantId)
    {
        _context.TenantId = tenantId;

        return new ValueTask<string>(string.Equals(tenantId, "contract-tenant-a", StringComparison.Ordinal) ? "agent-a" : "agent-b");
    }
}

/// <summary>Builds the catalog adapter of a definition source the way the container does.</summary>
internal static class DefinitionSourceAdapters
{
    public static IAgentSource Wrap(IAgentDefinitionSource source, ITenantContext tenant)
    {
        var services = new ServiceCollection()
            .AddSingleton(new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()))
            .AddSingleton(new CompiledAgentCache())
            .AddSingleton(tenant)
            .BuildServiceProvider();

        return DefinitionSourceAgentSource.Create(source, services);
    }
}
