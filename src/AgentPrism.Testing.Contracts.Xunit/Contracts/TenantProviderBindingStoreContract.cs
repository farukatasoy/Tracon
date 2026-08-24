namespace AgentPrism.Testing.Contracts.Storage;

/// <summary>Behavior tests for the <see cref="ITenantProviderBindingStore"/> contract.</summary>
/// <remarks>
/// This contract never asserts a secret value: the store carries only the
/// configuration key's NAME. It exists to prove tenant
/// isolation and round-tripping of that name plus the optional endpoint.
/// </remarks>
public abstract class TenantProviderBindingStoreContract : TenantIsolationContract<ITenantProviderBindingStore>
{
    private const string Tenant = "test";

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.UpsertAsync(Binding(tenantId, name));

        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    [Fact]
    public async Task Saved_binding_is_read_back()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai", "AgentPrism:ProviderKeys:Test:OpenAI", "https://proxy.example.com/"));

        var binding = await Store.GetAsync(Tenant, "openai");

        binding.ShouldNotBeNull();
        binding.TenantId.ShouldBe(Tenant);
        binding.ProviderName.ShouldBe("openai");
        binding.ApiKeyConfigurationName.ShouldBe("AgentPrism:ProviderKeys:Test:OpenAI");
        binding.Endpoint.ShouldBe("https://proxy.example.com/");
    }

    [Fact]
    public async Task Endpoint_is_optional()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai"));

        var binding = await Store.GetAsync(Tenant, "openai");

        binding.ShouldNotBeNull();
        binding.Endpoint.ShouldBeNull();
    }

    [Fact]
    public async Task Missing_binding_returns_null()
        => (await Store.GetAsync(Tenant, "unknown")).ShouldBeNull();

    [Fact]
    public async Task Upsert_replaces_the_existing_binding_for_the_same_tenant_and_provider()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai", "AgentPrism:ProviderKeys:Test:First"));
        await Store.UpsertAsync(Binding(Tenant, "openai", "AgentPrism:ProviderKeys:Test:Second"));

        var bindings = await Store.ListAsync(Tenant);

        bindings.Count.ShouldBe(1);
        bindings[0].ApiKeyConfigurationName.ShouldBe("AgentPrism:ProviderKeys:Test:Second");
    }

    [Fact]
    public async Task Listing_returns_every_provider_binding_of_the_tenant()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai"));
        await Store.UpsertAsync(Binding(Tenant, "anthropic"));

        var bindings = await Store.ListAsync(Tenant);

        bindings.Count.ShouldBe(2);
        var names = bindings.Select(static binding => binding.ProviderName).ToList();
        names.ShouldContain("openai", StringComparer.Ordinal);
        names.ShouldContain("anthropic", StringComparer.Ordinal);
    }

    private static TenantProviderBinding Binding(
        string tenantId,
        string providerName,
        string apiKeyConfigurationName = "AgentPrism:ProviderKeys:Test:Key",
        string? endpoint = null)
        => new()
        {
            TenantId = tenantId,
            ProviderName = providerName,
            ApiKeyConfigurationName = apiKeyConfigurationName,
            Endpoint = endpoint,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
}
