using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// A callable agent can be pinned to a version of the callee (<see cref="CallableAgentReference.Version"/>,
/// Phase 250): the compiler describes the callee from that version, keeps the caller's cache entry while the
/// callee moves on, and fails the caller by name when the pinned version is gone.
/// </summary>
public sealed class CallableAgentPinCompilationTests
{
    private const string TenantId = "tenant-a";
    private const string Callee = "sub";

    [Fact]
    public async Task A_pinned_callee_is_described_from_the_pinned_version()
    {
        var rig = await RigAsync();
        var definition = Caller(version: 1);

        var resolved = await rig.Compiler.ResolveCallableAgentsAsync(definition, CancellationToken.None);

        var info = resolved.Agents.ShouldHaveSingleItem();

        info.Name.ShouldBe(Callee);
        info.Version.ShouldBe(1);
        info.Description.ShouldBe("ALPHA description");
    }

    [Fact]
    public async Task An_unpinned_callee_is_described_from_the_current_version()
    {
        var rig = await RigAsync();

        var resolved = await rig.Compiler.ResolveCallableAgentsAsync(Caller(version: null), CancellationToken.None);

        var info = resolved.Agents.ShouldHaveSingleItem();

        info.Version.ShouldBe(2);
        info.Description.ShouldBe("BETA description");
    }

    [Fact]
    public async Task A_new_callee_version_leaves_the_cache_entry_of_a_pinned_caller_alone()
    {
        var rig = await RigAsync();
        var cache = new CompiledAgentCache();
        var definition = Caller(version: 1);

        var first = await rig.Compiler.CompileCachedAsync(definition, cache, TenantId);

        await rig.SaveCalleeAsync("GAMMA");
        var second = await rig.Compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeTrue("the pinned callee's description is that of version 1, which did not move");
    }

    [Fact]
    public async Task A_new_callee_version_replaces_the_cache_entry_of_an_unpinned_caller()
    {
        var rig = await RigAsync();
        var cache = new CompiledAgentCache();
        var definition = Caller(version: null);

        var first = await rig.Compiler.CompileCachedAsync(definition, cache, TenantId);

        await rig.SaveCalleeAsync("GAMMA");
        var second = await rig.Compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_the_callee_does_not_have_fails_the_compile_and_names_the_callee_and_the_version()
    {
        var rig = await RigAsync();

        var failure = await Should.ThrowAsync<TraconCompilationException>(
            async () => await rig.Compiler.ResolveCallableAgentsAsync(Caller(version: 9), CancellationToken.None));

        failure.AgentName.ShouldBe("caller");
        failure.Message.ShouldContain("pins callable agent 'sub' to version 9", Case.Sensitive);
        failure.Message.ShouldContain("has no version 9", Case.Sensitive);
    }

    [Fact]
    public async Task A_name_listed_twice_fails_the_compile_instead_of_sharing_one_pin()
    {
        // A definition written in code never met the save, which refuses the repeat. Compiled as it was, the second
        // entry decided the version of both wrappers.
        var rig = await RigAsync();
        var definition = TestData.Definition("caller") with
        {
            CallableAgents =
            [
                new CallableAgentReference { Name = Callee, Version = 1 },
                new CallableAgentReference { Name = Callee, Version = 2 },
            ],
        };

        var failure = await Should.ThrowAsync<TraconCompilationException>(
            async () => await rig.Compiler.ResolveCallableAgentsAsync(definition, CancellationToken.None));

        failure.AgentName.ShouldBe("caller");
        failure.Message.ShouldContain("lists callable agent 'sub' more than once", Case.Sensitive);
    }

    [Fact]
    public async Task A_pin_on_an_agent_defined_in_code_fails_the_compile()
    {
        var rig = await RigAsync(origin: AgentDefinitionOrigin.Code);

        var failure = await Should.ThrowAsync<TraconCompilationException>(
            async () => await rig.Compiler.ResolveCallableAgentsAsync(Caller(version: 1), CancellationToken.None));

        failure.Message.ShouldContain("keeps no version history", Case.Sensitive);
    }

    [Fact]
    public async Task A_pin_on_an_agent_of_a_custom_source_keeps_its_current_description()
    {
        // The store cannot read a custom source's versions; the catalog refuses the call when the source has none.
        var rig = await RigAsync(origin: AgentDefinitionOrigin.Custom);

        var info = (await rig.Compiler.ResolveCallableAgentsAsync(Caller(version: 1), CancellationToken.None)).Agents.ShouldHaveSingleItem();

        info.Version.ShouldBe(1);
        info.Description.ShouldBe("BETA description");
    }

    [Fact]
    public async Task A_callee_missing_from_the_catalog_compiles_with_no_description_as_before()
    {
        var rig = await RigAsync(listed: false);

        var info = (await rig.Compiler.ResolveCallableAgentsAsync(Caller(version: 1), CancellationToken.None)).Agents.ShouldHaveSingleItem();

        info.Description.ShouldBeNull();
    }

    private static AgentDefinition Caller(int? version)
        => TestData.Definition("caller") with
        {
            CallableAgents = [new CallableAgentReference { Name = Callee, Version = version }],
        };

    private static async Task<Rig> RigAsync(AgentDefinitionOrigin origin = AgentDefinitionOrigin.Database, bool listed = true)
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var store = new InMemoryAgentDefinitionStore(tenantContext);

        await store.SaveAsync(TestData.Definition(Callee) with { Description = "ALPHA description" });
        await store.SaveAsync(TestData.Definition(Callee) with { Description = "BETA description" });

        var catalog = new Catalog(store, origin, listed);
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            callableAgents: new CallableAgentResolver(catalog),
            tenantContext: tenantContext);

        return new Rig(compiler, store);
    }

    private sealed record Rig(AgentDefinitionCompiler Compiler, InMemoryAgentDefinitionStore Store)
    {
        public async Task SaveCalleeAsync(string description)
            => await Store.SaveAsync(TestData.Definition(Callee) with { Description = description });
    }

    /// <summary>A catalog that lists the stored callee as its current version, and a service provider that also serves the store.</summary>
    private sealed class Catalog(InMemoryAgentDefinitionStore store, AgentDefinitionOrigin origin, bool listed) : IAgentCatalog, IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(IAgentCatalog) ? this
                : serviceType == typeof(IAgentDefinitionStore) ? store
                : null;

        public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        {
            if (!listed)
            {
                return [];
            }

            var current = await store.GetAsync(Callee, cancellationToken);

            return
            [
                new AgentDescriptor
                {
                    Name = Callee,
                    Description = current!.Description,
                    Origin = origin,
                    SourceName = "test",
                    Version = current.Version,
                },
            ];
        }

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new((AIAgent?)null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, culture, cancellationToken);
    }
}
