namespace Tracon.Testing.Contracts.Storage;

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
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.UpsertAsync(tenantId, ["openai"], cancellationToken);

        return tenantId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => await Store.GetAsync(tenantId, cancellationToken) is null ? 0 : 1;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId);

    [Theory]
    [InlineData("acme", "Acme")]
    [InlineData("Acme", "acme")]
    [InlineData("ACME", "acme")]
    public async Task A_tenant_is_matched_case_insensitively(string savedAs, string requestedAs)
    {
        // 🚨 A case-sensitive store is a SECURITY defect here, not an
        // ergonomic one, and it is fail-OPEN: ModelProviderRegistry applies NO
        // egress restriction when the policy row is missing. A policy an admin
        // saved as "Acme" that the runtime looks up as "acme" therefore does
        // not restrict the tenant at all -- it silently stops existing.
        //
        // Matched by normalizing the VALUE, never by a case-insensitive
        // comparer: a bare `=` then behaves the same on PostgreSQL, SQLite and
        // SQL Server whatever their collation (AmbientTenantScope.Normalize).
        await Store.UpsertAsync(savedAs, ["openai"]);

        var policy = await Store.GetAsync(requestedAs);

        policy.ShouldNotBeNull();
        // Spelled out, not recomputed: an assertion that applies the rule
        // under test passes whatever the rule does.
        policy.TenantId.ShouldBe("acme");
    }

    [Fact]
    public async Task A_policy_saved_under_a_different_case_replaces_the_existing_one()
    {
        await Store.UpsertAsync("acme", ["openai"]);
        await Store.UpsertAsync("Acme", ["anthropic"]);

        // One policy, not two: otherwise which of the pair wins at call time
        // depends on the storage engine's collation.
        var policy = await Store.GetAsync("ACME");

        policy.ShouldNotBeNull();
        policy.AllowedProviders.ShouldBe(["anthropic"]);
    }

    [Fact]
    public async Task A_policy_is_deleted_whatever_case_the_caller_uses()
    {
        await Store.UpsertAsync("acme", ["openai"]);

        (await Store.DeleteAsync("Acme")).ShouldBeTrue();
        (await Store.GetAsync("acme")).ShouldBeNull();
    }

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
