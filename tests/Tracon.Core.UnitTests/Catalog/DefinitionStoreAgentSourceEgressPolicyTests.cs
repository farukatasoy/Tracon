using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// Verifies that a tenant egress policy saved after an agent was compiled and
/// cached reaches the very next resolve (F-304).
/// </summary>
/// <remarks>
/// 🚨 Regression (2026-10-11): the policy is checked only when a model client
/// is built, and the compiled-agent cache key carried no trace of it. An agent
/// cached before the policy was saved kept calling the provider the policy
/// forbids, until its definition changed or the process restarted.
/// </remarks>
public sealed class DefinitionStoreAgentSourceEgressPolicyTests
{
    private const string Tenant = "acme";
    private const string AgentName = "support";

    [Fact]
    public async Task A_policy_saved_after_the_agent_was_cached_refuses_the_next_resolve()
    {
        var (source, cache, egress) = await CreateSourceAsync();

        await source.ResolveAsync(AgentName);
        cache.Count.ShouldBe(1);

        await egress.UpsertAsync(Tenant, ["anthropic"]);

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await source.ResolveAsync(AgentName));

        exception.Message.ShouldContain("openai");
    }

    [Fact]
    public async Task An_unchanged_policy_keeps_serving_the_cached_agent()
    {
        // The other direction: the policy is part of the key, not a reason to
        // stop caching. A tenant that saved a policy once must not recompile
        // the agent on every run.
        var (source, cache, egress) = await CreateSourceAsync();
        await egress.UpsertAsync(Tenant, ["openai"]);

        var first = await source.ResolveAsync(AgentName);
        var second = await source.ResolveAsync(AgentName);

        second.ShouldBeSameAs(first);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_providers_saved_in_another_order_or_case_keep_the_cached_agent()
    {
        // The registry compares provider names case-insensitively, so this is
        // the same policy. Re-saving it must not cost a recompilation.
        var (source, cache, egress) = await CreateSourceAsync();
        await egress.UpsertAsync(Tenant, ["openai", "anthropic"]);

        var first = await source.ResolveAsync(AgentName);

        await egress.UpsertAsync(Tenant, ["Anthropic", "OpenAI"]);
        var second = await source.ResolveAsync(AgentName);

        second.ShouldBeSameAs(first);
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_the_policy_lets_the_agent_compile_again()
    {
        var (source, _, egress) = await CreateSourceAsync();
        await egress.UpsertAsync(Tenant, ["anthropic"]);

        await Should.ThrowAsync<TraconCompilationException>(async () => await source.ResolveAsync(AgentName));

        await egress.DeleteAsync(Tenant);

        (await source.ResolveAsync(AgentName)).ShouldNotBeNull();
    }

    private static async Task<(
        DefinitionStoreAgentSource Source,
        CompiledAgentCache Cache,
        InMemoryTenantEgressPolicyStore Egress)> CreateSourceAsync()
    {
        var tenantContext = new FixedTenantContext(Tenant);
        var egress = new InMemoryTenantEgressPolicyStore();

        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(name: "openai")],
            tenantContext: tenantContext,
            tenantProviderBindings: new InMemoryTenantProviderBindingStore(),
            tenantEgressPolicies: egress);

        var store = new InMemoryAgentDefinitionStore(tenantContext);
        await store.SaveAsync(TestData.Definition(AgentName) with { Model = TestData.Binding(provider: "openai") });

        var compiler = new AgentDefinitionCompiler(
            registry,
            TestData.Registry(),
            tenantContext: tenantContext,
            tenantEgressPolicies: egress);

        var cache = new CompiledAgentCache();

        return (new DefinitionStoreAgentSource(store, compiler, cache, tenantContext), cache, egress);
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
