using Tracon.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// <see cref="AgentDefinitionCompiler.CompileCachedAsync"/> is the one high-level path
/// 101.7 asks every definition-backed <see cref="IAgentSource"/> to use instead of
/// re-deriving the cache key itself. These tests prove the three dependency
/// fingerprints it combines (skill, callable agent, shared instructions) actually
/// invalidate the cache when they change — none of them had a regression test before
/// this phase. Basic same-tenant cache reuse and the BYOK cache-bypass rule already have
/// dedicated coverage in <c>DefinitionStoreAgentSourceTenantCredentialTests</c>; this file
/// does not repeat them.
/// </summary>
public sealed class CompileCachedAsyncTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public async Task Unchanged_dependencies_produce_a_cache_hit()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition();

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId);
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeTrue();
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Different_cultures_are_different_cache_entries()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition();

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId, culture: "en");
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId, culture: "tr");

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_skills_version_change_invalidates_the_cached_agent()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var skillStore = new InMemoryAgentSkillStore();
        await skillStore.SaveAsync(Skill("billing"));
        var skills = new AgentSkillCatalog([], skillStore, tenantContext, Options.Create(new TraconOptions()));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry(), skills: skills);
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition() with { SkillNames = ["billing"] };

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId);

        // Re-saving bumps the skill's Version, which feeds the fingerprint (AgentSkillCatalog.CreateFingerprint).
        await skillStore.SaveAsync(Skill("billing") with { Description = "updated" });
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_callable_agents_version_change_invalidates_the_cached_agent()
    {
        var catalog = new MutableAgentCatalog(
            new AgentDescriptor { Name = "sub", Origin = AgentDefinitionOrigin.Code, SourceName = "code", Version = 1 });
        var callableAgents = new CallableAgentResolver(catalog);
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            callableAgents: callableAgents,
            tenantContext: new FixedTenantContext(TenantId));
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition() with { CallableAgentNames = ["sub"] };

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId);

        // A sub-agent's description is embedded in the caller's instructions
        // (CreateCallableFingerprint), so a version bump must invalidate the caller too.
        catalog.Descriptor = catalog.Descriptor with { Version = 2 };
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_shared_instructions_blocks_version_change_invalidates_the_cached_agent()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var store = new InMemoryAgentDefinitionStore(tenantContext);
        await store.SaveAsync(TestData.Definition("shared-block") with { Instructions = "v1" });
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry(), definitionStore: store);
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition() with { SharedInstructionsName = "shared-block" };

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId);

        // Re-saving bumps the block's Version, which feeds the fingerprint (ResolveSharedInstructionsAsync: "{blockName}:{block.Version}").
        await store.SaveAsync(TestData.Definition("shared-block") with { Instructions = "v2" });
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(2);
    }

    private static AgentSkillDefinition Skill(string name)
        => new()
        {
            TenantId = TenantId,
            Name = name,
            Description = "Skill description.",
            Instructions = "Skill instructions.",
        };

    /// <summary>An <see cref="IAgentCatalog"/> whose single descriptor can be swapped between calls.</summary>
    private sealed class MutableAgentCatalog(AgentDescriptor descriptor) : IAgentCatalog, IServiceProvider
    {
        public AgentDescriptor Descriptor { get; set; } = descriptor;

        public object? GetService(Type serviceType) => serviceType == typeof(IAgentCatalog) ? this : null;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[Descriptor]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new((AIAgent?)null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, culture, cancellationToken);
    }
}
