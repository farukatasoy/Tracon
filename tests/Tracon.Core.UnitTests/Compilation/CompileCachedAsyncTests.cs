using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// <see cref="AgentDefinitionCompiler.CompileCachedAsync"/> is the one high-level path
/// the source contract asks every definition-backed <see cref="IAgentSource"/> to use instead of
/// re-deriving the cache key itself. These tests prove the three dependency
/// fingerprints it combines (skill, callable agent, shared instructions) actually
/// invalidate the cache when they change — none of them had a regression test before
/// this change. Basic same-tenant cache reuse and the BYOK cache-bypass rule already have
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
    public async Task Cultures_that_select_different_instructions_are_different_cache_entries()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache();
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId, culture: "en");
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId, culture: "tr");

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(2);
        InstructionsOf(first).ShouldBe("Hello.");
        InstructionsOf(second).ShouldBe("Merhaba.");
    }

    [Fact]
    public async Task Cultures_that_select_the_same_entry_share_one_compiled_agent()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache();
        var definition = WithCultures(("en", "Hello."), ("TR", "Merhaba."));

        // Spelled four ways, the culture picks the "TR" entry each time; the default text is picked by the others.
        var turkish = new[] { "tr", "TR", "tr-TR", "Tr-tr-x" };
        var agents = new List<AIAgent>();

        foreach (var culture in turkish)
        {
            agents.Add(await compiler.CompileCachedAsync(definition, cache, TenantId, culture));
        }

        agents.Distinct().Count().ShouldBe(1);
        cache.Count.ShouldBe(1);

        var fallback = new[] { null, string.Empty, " ", "de", "xx-yy", "-tr", "t", "tr_TR" };
        var defaults = new List<AIAgent>();

        foreach (var culture in fallback)
        {
            defaults.Add(await compiler.CompileCachedAsync(definition, cache, TenantId, culture));
        }

        defaults.Distinct().Count().ShouldBe(1, "every culture that matches no entry compiles the default text once");
        cache.Count.ShouldBe(2);
        InstructionsOf(defaults[0]).ShouldBe(definition.Instructions);
        InstructionsOf(agents[0]).ShouldBe("Merhaba.");
    }

    [Fact]
    public async Task A_request_cannot_open_a_slot_by_inventing_a_culture()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache();
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        for (var index = 0; index < 2_000; index++)
        {
            await compiler.CompileCachedAsync(definition, cache, TenantId, culture: $"a{index}");
        }

        await compiler.CompileCachedAsync(definition, cache, TenantId, culture: new string('x', 10_000));
        await compiler.CompileCachedAsync(definition, cache, TenantId, culture: "tr-TR");
        await compiler.CompileCachedAsync(definition, cache, TenantId, culture: "en-GB");

        cache.Count.ShouldBe(3, "the default text and the two entries of the dictionary are the only slots");
    }

    [Fact]
    public async Task Two_tenants_asking_for_the_same_culture_do_not_share_a_slot()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache();
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));

        var first = await compiler.CompileCachedAsync(definition, cache, "tenant-a", culture: "tr-TR");
        var second = await compiler.CompileCachedAsync(definition, cache, "tenant-b", culture: "tr-TR");

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_requests_that_spell_a_culture_differently_end_with_one_slot()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache();
        var definition = WithCultures(("en", "Hello."), ("tr", "Merhaba."));
        var spellings = new[] { "tr", "TR", "tr-TR", "Tr-tr", "tr-x-y" };

        var agents = await Task.WhenAll(Enumerable.Range(0, 64).Select(
            index => Task.Run(() => compiler.CompileCachedAsync(definition, cache, TenantId, spellings[index % spellings.Length]).AsTask())));

        cache.Count.ShouldBe(1);

        // The factory may run more than once when callers miss together; every caller after the first store gets
        // the stored instance, and every instance carries the same text.
        agents.Select(InstructionsOf).Distinct(StringComparer.Ordinal).ShouldBe(["Merhaba."]);
    }

    private static AgentDefinition WithCultures(params (string Culture, string Text)[] entries)
        => TestData.Definition() with
        {
            InstructionsByCulture = entries.ToDictionary(
                static entry => entry.Culture,
                static entry => entry.Text,
                StringComparer.Ordinal),
        };

    private static string? InstructionsOf(AIAgent agent)
        => agent.GetService<ChatClientAgentOptions>()!.ChatOptions!.Instructions;

    [Fact]
    public async Task A_skills_version_change_invalidates_the_cached_agent()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var skillStore = new InMemoryAgentSkillStore();
        await skillStore.SaveAsync(Skill("billing"));
        var skills = new AgentSkillCatalog([], skillStore, tenantContext, Options.Create(new TraconOptions()));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry(), new AgentCompilationServices { Skills = skills });
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition() with { SkillNames = ["billing"] };

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId);

        // Re-saving bumps the skill's Version, which feeds the fingerprint (AgentSkillCatalog.CreateFingerprint).
        await skillStore.SaveAsync(Skill("billing") with { Description = "updated" });
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(1, "the dependency change replaces the entry");
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
            new AgentCompilationServices
            {
                CallableAgents = callableAgents,
                TenantContext = new FixedTenantContext(TenantId),
            });
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition() with { CallableAgents = ["sub"] };

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId);

        // A sub-agent's description is embedded in the caller's instructions
        // (CreateCallableFingerprint), so a version bump must invalidate the caller too.
        catalog.Descriptor = catalog.Descriptor with { Version = 2 };
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(1, "the dependency change replaces the entry");
    }

    [Fact]
    public async Task A_shared_instructions_blocks_version_change_invalidates_the_cached_agent()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var store = new InMemoryAgentDefinitionStore(tenantContext);
        await store.SaveAsync(TestData.Definition("shared-block") with { Instructions = "v1" });
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry(), new AgentCompilationServices { DefinitionStore = store });
        var cache = new CompiledAgentCache();
        var definition = TestData.Definition() with { SharedInstructionsName = "shared-block" };

        var first = await compiler.CompileCachedAsync(definition, cache, TenantId);

        // Re-saving bumps the block's Version, which feeds the fingerprint (ResolveSharedInstructionsAsync: "{blockName}:{block.Version}").
        await store.SaveAsync(TestData.Definition("shared-block") with { Instructions = "v2" });
        var second = await compiler.CompileCachedAsync(definition, cache, TenantId);

        ReferenceEquals(first, second).ShouldBeFalse();
        cache.Count.ShouldBe(1, "the dependency change replaces the entry");
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
