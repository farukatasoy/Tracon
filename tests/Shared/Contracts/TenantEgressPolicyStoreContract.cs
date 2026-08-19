namespace AgentPrism.StoreContracts;

/// <summary>Behavior tests for the <see cref="ITenantEgressPolicyStore"/> contract.</summary>
/// <remarks>
/// A tenant carries at most ONE policy (there is no "name" dimension), so the
/// isolation base's generic <c>name</c> parameter is ignored here; every
/// scenario still proves the SAME thing the other store contracts do:
/// tenant A's policy is invisible to and unwritable by tenant B.
/// </remarks>
public abstract class TenantEgressPolicyStoreContract : TenantIsolationContract<ITenantEgressPolicyStore>
{
    private const string Tenant = "test";

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.UpsertAsync(tenantId, ["openai"]);

        return tenantId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => await Store.GetAsync(tenantId) is null ? 0 : 1;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId);

    [Fact]
    public async Task Saved_policy_is_read_back()
    {
        await Store.UpsertAsync(Tenant, ["openai", "anthropic"]);

        var policy = await Store.GetAsync(Tenant);

        policy.ShouldNotBeNull();
        policy.TenantId.ShouldBe(Tenant);
        policy.AllowedProviders.ShouldBe(["openai", "anthropic"]);
    }

    [Fact]
    public async Task Tenant_with_no_saved_policy_is_unrestricted()
        => (await Store.GetAsync(Tenant)).ShouldBeNull();

    [Fact]
    public async Task Upsert_replaces_the_existing_policy()
    {
        await Store.UpsertAsync(Tenant, ["openai"]);
        await Store.UpsertAsync(Tenant, ["anthropic", "google"]);

        var policy = await Store.GetAsync(Tenant);

        policy.ShouldNotBeNull();
        policy.AllowedProviders.ShouldBe(["anthropic", "google"]);
    }

    [Fact]
    public async Task Deleting_the_policy_makes_the_tenant_unrestricted_again()
    {
        await Store.UpsertAsync(Tenant, ["openai"]);

        (await Store.DeleteAsync(Tenant)).ShouldBeTrue();

        (await Store.GetAsync(Tenant)).ShouldBeNull();
    }
}
