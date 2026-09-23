using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// Verifies that <see cref="DefinitionStoreAgentSource"/> never hands
/// <see cref="CompiledAgentCache"/> an agent built with a tenant-specific
/// provider credential (phase 65, BYOK — independent audit finding).
/// </summary>
/// <remarks>
/// 🚨 Regression: before this fix, a compiled agent's chat client — with a
/// tenant credential already baked into it — was cached under a key that
/// carried no trace of that credential. Rotating or deleting the tenant's
/// binding had no effect on already-cached agents; calls kept silently
/// using the old credential forever.
/// </remarks>
public sealed class DefinitionStoreAgentSourceTenantCredentialTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Without_a_tenant_binding_the_compiled_agent_is_cached_normally()
    {
        var (source, cache, _, _) = CreateSource();

        await source.ResolveAsync("support");
        await source.ResolveAsync("support");

        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Adding_a_tenant_binding_makes_the_very_next_resolve_bypass_the_cache_and_use_it()
    {
        var (source, cache, bindings, provider) = CreateSource();

        // First resolve: no binding yet, global credential, gets cached.
        await source.ResolveAsync("support");
        cache.Count.ShouldBe(1);
        provider.LastCredential.ShouldBeNull();

        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "openai",
            ApiKeyConfigurationName = "Tracon:ProviderKeys:Acme:OpenAI",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Second resolve, SAME definition version: must NOT return the
        // cached (global-credential) agent — the binding must take effect
        // immediately, and the cache must not grow (the result is not stored).
        await source.ResolveAsync("support");

        provider.LastCredential.ShouldNotBeNull();
        provider.LastCredential.ApiKey.ShouldBe("sk-tenant-key");
        cache.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_the_binding_makes_the_next_resolve_fall_back_to_the_global_credential_again()
    {
        var (source, cache, bindings, provider) = CreateSource();

        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "openai",
            ApiKeyConfigurationName = "Tracon:ProviderKeys:Acme:OpenAI",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await source.ResolveAsync("support");
        provider.LastCredential.ShouldNotBeNull();
        cache.Count.ShouldBe(0); // bypassed, never cached

        await bindings.DeleteAsync(Tenant, "openai");

        await source.ResolveAsync("support");

        provider.LastCredential.ShouldBeNull();
        cache.Count.ShouldBe(1); // now cacheable again
    }

    private static (
        DefinitionStoreAgentSource Source,
        CompiledAgentCache Cache,
        InMemoryTenantProviderBindingStore Bindings,
        FakeModelProvider Provider) CreateSource()
    {
        var tenantContext = new FixedTenantContext(Tenant);
        var provider = new FakeModelProvider(name: "openai");
        var bindings = new InMemoryTenantProviderBindingStore();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Tracon:ProviderKeys:Acme:OpenAI", "sk-tenant-key")])
            .Build();

        var registry = new ModelProviderRegistry(
            [provider],
            tenantContext: tenantContext,
            tenantProviderBindings: bindings,
            tenantEgressPolicies: new InMemoryTenantEgressPolicyStore(),
            credentialResolver: new TenantProviderCredentialResolver(
                configuration,
                new StaticOptionsMonitor<TraconTenantProviderOptions>(new TraconTenantProviderOptions()),
                Options.Create(new TraconOptions())));

        var tools = TestData.Registry();
        var store = new InMemoryAgentDefinitionStore(tenantContext);

        store.SaveAsync(TestData.Definition("support") with { Model = TestData.Binding(provider: "openai") })
            .AsTask().GetAwaiter().GetResult();

        var compiler = new AgentDefinitionCompiler(registry, tools, tenantContext: tenantContext);
        var cache = new CompiledAgentCache();
        var source = new DefinitionStoreAgentSource(store, compiler, cache, tenantContext);

        return (source, cache, bindings, provider);
    }

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }
}
