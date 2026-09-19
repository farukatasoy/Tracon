namespace Tracon.Testing.Contracts.Storage;

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
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.UpsertAsync(Binding(tenantId, name), cancellationToken);

        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    [Fact]
    public async Task Saved_binding_is_read_back()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai", "Tracon:ProviderKeys:Test:OpenAI", "https://proxy.example.com/"));

        var binding = await Store.GetAsync(Tenant, "openai");

        binding.ShouldNotBeNull();
        binding.TenantId.ShouldBe(Tenant);
        binding.ProviderName.ShouldBe("openai");
        binding.ApiKeyConfigurationName.ShouldBe("Tracon:ProviderKeys:Test:OpenAI");
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

    [Theory]
    [InlineData("OpenAI", "openai")]
    [InlineData("openai", "OpenAI")]
    [InlineData("OPENAI", "openai")]
    public async Task A_provider_name_is_matched_case_insensitively(string savedAs, string requestedAs)
    {
        // 🚨 A case-sensitive store is a SECURITY defect, not an ergonomic one.
        // The provider registry, the egress policy and the admin endpoint all
        // match the name case-insensitively. A store that does not turns an
        // existing tenant binding into a silent MISS -- and a miss falls back
        // to the global setup credential, billing the wrong tenant with no
        // error raised.
        await Store.UpsertAsync(Binding(Tenant, savedAs));

        (await Store.GetAsync(Tenant, requestedAs)).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("acme", "Acme")]
    [InlineData("Acme", "acme")]
    [InlineData("ACME", "acme")]
    public async Task A_tenant_is_matched_case_insensitively(string savedAs, string requestedAs)
    {
        // 🚨 The other half of the key. K-639 made the PROVIDER name canonical
        // after a case-sensitive store silently billed the wrong tenant; the
        // TENANT half of the same key stayed raw until phase 179. A miss falls
        // through to the global setup credential exactly the same way, with no
        // error raised.
        await Store.UpsertAsync(Binding(savedAs, "openai"));

        var binding = await Store.GetAsync(requestedAs, "openai");

        binding.ShouldNotBeNull();
        // Spelled out, not recomputed: an assertion that applies the rule
        // under test passes whatever the rule does.
        binding.TenantId.ShouldBe("acme");

        (await Store.ListAsync(requestedAs)).Count.ShouldBe(1);
        (await Store.DeleteAsync(requestedAs, "openai")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_binding_saved_under_a_different_case_replaces_the_existing_one()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai", "Tracon:ProviderKeys:Test:First"));
        await Store.UpsertAsync(Binding(Tenant, "OpenAI", "Tracon:ProviderKeys:Test:Second"));

        // One binding, not two: otherwise which of the pair wins at resolution
        // time depends on the storage engine's collation.
        var bindings = await Store.ListAsync(Tenant);

        bindings.Count.ShouldBe(1);
        bindings[0].ApiKeyConfigurationName.ShouldBe("Tracon:ProviderKeys:Test:Second");
    }

    [Fact]
    public async Task A_binding_is_deleted_whatever_case_the_caller_uses()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai"));

        (await Store.DeleteAsync(Tenant, "OpenAI")).ShouldBeTrue();
        (await Store.GetAsync(Tenant, "openai")).ShouldBeNull();
    }

    [Fact]
    public async Task Upsert_replaces_the_existing_binding_for_the_same_tenant_and_provider()
    {
        await Store.UpsertAsync(Binding(Tenant, "openai", "Tracon:ProviderKeys:Test:First"));
        await Store.UpsertAsync(Binding(Tenant, "openai", "Tracon:ProviderKeys:Test:Second"));

        var bindings = await Store.ListAsync(Tenant);

        bindings.Count.ShouldBe(1);
        bindings[0].ApiKeyConfigurationName.ShouldBe("Tracon:ProviderKeys:Test:Second");
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
        string apiKeyConfigurationName = "Tracon:ProviderKeys:Test:Key",
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
