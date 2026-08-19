using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// Verifies <see cref="ModelProviderRegistry.CreateChatClientAsync"/>'s tenant
/// credential and egress policy resolution (phase 65, BYOK/F-119).
/// </summary>
public sealed class ModelProviderRegistryTenantCredentialTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Without_a_tenant_context_the_async_overload_behaves_like_the_sync_one()
    {
        var provider = new FakeModelProvider(name: "openai");
        var registry = new ModelProviderRegistry(
            [provider],
            tenantProviderBindings: new InMemoryTenantProviderBindingStore(),
            tenantEgressPolicies: new InMemoryTenantEgressPolicyStore(),
            credentialResolver: CreateResolver());

        await registry.CreateChatClientAsync(TestData.Binding(provider: "openai"));

        provider.LastCredential.ShouldBeNull();
    }

    [Fact]
    public async Task Tenant_with_no_binding_falls_back_to_the_global_credential()
    {
        var provider = new FakeModelProvider(name: "openai");
        var registry = CreateRegistry(provider, new FixedTenantContext(Tenant));

        await registry.CreateChatClientAsync(TestData.Binding(provider: "openai"));

        provider.LastCredential.ShouldBeNull();
    }

    [Fact]
    public async Task Tenant_with_a_resolvable_binding_gets_its_own_credential()
    {
        var provider = new FakeModelProvider(name: "openai");
        var bindings = new InMemoryTenantProviderBindingStore();

        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "openai",
            ApiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI",
            Endpoint = "https://proxy.example.com/",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var registry = CreateRegistry(
            provider,
            new FixedTenantContext(Tenant),
            bindings,
            configuration: BuildConfiguration(("AgentPrism:ProviderKeys:Acme:OpenAI", "sk-tenant-key")));

        await registry.CreateChatClientAsync(TestData.Binding(provider: "openai"));

        provider.LastCredential.ShouldNotBeNull();
        provider.LastCredential.ApiKey.ShouldBe("sk-tenant-key");
        provider.LastCredential.Endpoint.ShouldBe("https://proxy.example.com/");
    }

    [Fact]
    public async Task A_binding_that_exists_but_resolves_to_no_value_does_not_fall_back_silently()
    {
        var provider = new FakeModelProvider(name: "openai");
        var bindings = new InMemoryTenantProviderBindingStore();

        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "openai",
            ApiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // No configuration value set for the key: it exists as a NAME only.
        var registry = CreateRegistry(provider, new FixedTenantContext(Tenant), bindings);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await registry.CreateChatClientAsync(TestData.Binding(provider: "openai")));

        exception.Message.ShouldContain("AgentPrism:ProviderKeys:Acme:OpenAI");
        // The provider must never have been reached with a fallback credential.
        provider.LastCredential.ShouldBeNull();
        provider.LastBinding.ShouldBeNull();
    }

    [Fact]
    public async Task Egress_policy_rejects_a_provider_not_in_the_allowed_list()
    {
        var provider = new FakeModelProvider(name: "openai");
        var egress = new InMemoryTenantEgressPolicyStore();
        await egress.UpsertAsync(Tenant, ["anthropic"]);

        var registry = CreateRegistry(provider, new FixedTenantContext(Tenant), egressPolicies: egress);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await registry.CreateChatClientAsync(TestData.Binding(provider: "openai")));

        exception.Message.ShouldContain("openai");
        exception.Message.ShouldContain("anthropic");
        provider.LastBinding.ShouldBeNull();
    }

    [Fact]
    public async Task Egress_check_runs_before_the_binding_lookup()
    {
        // 🚨 Section 65.4: looking up a key for a forbidden provider is a path
        // that must not run at all. A binding STORE that throws if queried
        // proves the egress check short-circuits before it.
        var provider = new FakeModelProvider(name: "openai");
        var egress = new InMemoryTenantEgressPolicyStore();
        await egress.UpsertAsync(Tenant, ["anthropic"]);

        var registry = CreateRegistry(
            provider,
            new FixedTenantContext(Tenant),
            tenantProviderBindings: new ThrowingTenantProviderBindingStore(),
            egressPolicies: egress);

        await Should.ThrowAsync<AgentPrismException>(
            async () => await registry.CreateChatClientAsync(TestData.Binding(provider: "openai")));
    }

    [Fact]
    public async Task Egress_policy_allows_a_listed_provider()
    {
        var provider = new FakeModelProvider(name: "openai");
        var egress = new InMemoryTenantEgressPolicyStore();
        await egress.UpsertAsync(Tenant, ["openai"]);

        var registry = CreateRegistry(provider, new FixedTenantContext(Tenant), egressPolicies: egress);

        await registry.CreateChatClientAsync(TestData.Binding(provider: "openai"));

        provider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public async Task Egress_policy_rejects_a_fallback_provider_not_in_the_allowed_list()
    {
        // 🚨 Regression for an independent-audit finding (phase 65): a
        // fallback link is a first-class ModelBinding.Provider in its own
        // right and must be checked at compile time too, not only lazily the
        // first time the primary actually fails over to it.
        var provider = new FakeModelProvider(name: "openai");
        var egress = new InMemoryTenantEgressPolicyStore();
        await egress.UpsertAsync(Tenant, ["openai"]);

        var registry = CreateRegistry(provider, new FixedTenantContext(Tenant), egressPolicies: egress);

        var binding = TestData.Binding(provider: "openai") with
        {
            Fallbacks = [new ModelFallback { Provider = "anthropic", Model = "claude" }],
        };

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await registry.CreateChatClientAsync(binding));

        exception.Message.ShouldContain("anthropic");
        exception.Message.ShouldContain("fallback");
    }

    [Fact]
    public async Task A_triggered_fallback_resolves_its_own_tenant_credential_through_the_async_entry_point()
    {
        // 🚨 Regression for an independent-audit finding (phase 65):
        // FallbackChatClient used to be built with the SYNC, global-only
        // CreateChatClient method group even when reached through
        // CreateChatClientAsync — a triggered fallback silently used the
        // global credential and bypassed the tenant's own binding.
        var primary = new FakeModelProvider(
            new FakeChatClient(_ => throw new AgentPrismProviderUnavailableException("circuit open")),
            name: "openai");
        var fallback = new FakeModelProvider(
            new FakeChatClient(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, "answer"))),
            name: "anthropic");

        var bindings = new InMemoryTenantProviderBindingStore();
        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "anthropic",
            ApiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:Anthropic",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var registry = new ModelProviderRegistry(
            [primary, fallback],
            tenantContext: new FixedTenantContext(Tenant),
            tenantProviderBindings: bindings,
            tenantEgressPolicies: new InMemoryTenantEgressPolicyStore(),
            credentialResolver: CreateResolver(BuildConfiguration(("AgentPrism:ProviderKeys:Acme:Anthropic", "sk-fallback-key"))));

        var binding = TestData.Binding(provider: "openai") with
        {
            Fallbacks = [new ModelFallback { Provider = "anthropic", Model = "claude" }],
        };

        var chatClient = await registry.CreateChatClientAsync(binding);
        var response = await chatClient.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        response.Text.ShouldBe("answer");
        fallback.LastCredential.ShouldNotBeNull();
        fallback.LastCredential.ApiKey.ShouldBe("sk-fallback-key");
    }

    [Fact]
    public async Task The_sync_overload_never_resolves_a_tenant_credential_even_when_one_would_be_available()
    {
        // 🚨 Documented boundary: CreateChatClient (sync) cannot perform an
        // async store lookup, so it always uses the global credential (null),
        // even when a tenant context and a resolvable binding both exist.
        var provider = new FakeModelProvider(name: "openai");
        var bindings = new InMemoryTenantProviderBindingStore();
        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "openai",
            ApiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var registry = CreateRegistry(
            provider,
            new FixedTenantContext(Tenant),
            bindings,
            configuration: BuildConfiguration(("AgentPrism:ProviderKeys:Acme:OpenAI", "sk-tenant-key")));

        registry.CreateChatClient(TestData.Binding(provider: "openai"));

        provider.LastCredential.ShouldBeNull();
    }

    [Fact]
    public async Task HasTenantProviderOverride_is_false_with_no_binding()
    {
        var registry = CreateRegistry(new FakeModelProvider(name: "openai"), new FixedTenantContext(Tenant));

        (await registry.HasTenantProviderOverrideAsync(TestData.Binding(provider: "openai"))).ShouldBeFalse();
    }

    [Fact]
    public async Task HasTenantProviderOverride_is_true_when_the_primary_provider_has_a_binding()
    {
        var bindings = new InMemoryTenantProviderBindingStore();
        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "openai",
            ApiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:OpenAI",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var registry = CreateRegistry(new FakeModelProvider(name: "openai"), new FixedTenantContext(Tenant), bindings);

        (await registry.HasTenantProviderOverrideAsync(TestData.Binding(provider: "openai"))).ShouldBeTrue();
    }

    [Fact]
    public async Task HasTenantProviderOverride_is_true_when_only_a_fallback_provider_has_a_binding()
    {
        var bindings = new InMemoryTenantProviderBindingStore();
        await bindings.UpsertAsync(new TenantProviderBinding
        {
            TenantId = Tenant,
            ProviderName = "anthropic",
            ApiKeyConfigurationName = "AgentPrism:ProviderKeys:Acme:Anthropic",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var registry = CreateRegistry(new FakeModelProvider(name: "openai"), new FixedTenantContext(Tenant), bindings);

        var binding = TestData.Binding(provider: "openai") with
        {
            Fallbacks = [new ModelFallback { Provider = "anthropic", Model = "claude" }],
        };

        (await registry.HasTenantProviderOverrideAsync(binding)).ShouldBeTrue();
    }

    private static ModelProviderRegistry CreateRegistry(
        FakeModelProvider provider,
        ITenantContext tenantContext,
        ITenantProviderBindingStore? tenantProviderBindings = null,
        ITenantEgressPolicyStore? egressPolicies = null,
        IConfiguration? configuration = null)
        => new(
            [provider],
            tenantContext: tenantContext,
            tenantProviderBindings: tenantProviderBindings ?? new InMemoryTenantProviderBindingStore(),
            tenantEgressPolicies: egressPolicies ?? new InMemoryTenantEgressPolicyStore(),
            credentialResolver: CreateResolver(configuration));

    private static TenantProviderCredentialResolver CreateResolver(IConfiguration? configuration = null)
        => new(
            configuration ?? BuildConfiguration(),
            new StaticOptionsMonitor<AgentPrismTenantProviderOptions>(new AgentPrismTenantProviderOptions()));

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId => tenantId;
    }

    private sealed class ThrowingTenantProviderBindingStore : ITenantProviderBindingStore
    {
        public ValueTask<TenantProviderBinding?> GetAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The binding store must not be queried when the egress policy already rejects the provider.");

        public ValueTask<IReadOnlyList<TenantProviderBinding>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();

        public ValueTask UpsertAsync(TenantProviderBinding binding, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();

        public ValueTask<bool> DeleteAsync(string tenantId, string providerName, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException();
    }
}
