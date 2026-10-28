namespace Tracon.Testing.Contracts.Storage;

/// <summary>Behavior tests for the <see cref="ITenantToolPolicyStore"/> contract.</summary>
/// <remarks>
/// A tenant carries at most ONE policy (there is no "name" dimension), so the
/// isolation base's generic <c>name</c> parameter is ignored here; every
/// scenario still proves the SAME thing the other store contracts do:
/// tenant A's policy is invisible to and unwritable by tenant B.
/// </remarks>
public abstract class TenantToolPolicyStoreContract : TenantIsolationContract<ITenantToolPolicyStore>
{
    private const string Tenant = "test";

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.UpsertAsync(tenantId, ["get_weather"], ["orders.read"], cancellationToken);

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
        // 🚨 A case-sensitive store is a SECURITY defect here, not an ergonomic
        // one, and it is fail-OPEN while Tracon:Tools:RequireTenantPolicy is off:
        // the gate applies NO restriction when the policy row is missing. A
        // policy an admin saved as "Acme" that the runtime looks up as "acme"
        // therefore does not restrict the tenant at all -- it silently stops
        // existing.
        //
        // Matched by normalizing the VALUE, never by a case-insensitive
        // comparer: a bare `=` then behaves the same on PostgreSQL, SQLite and
        // SQL Server whatever their collation (AmbientTenantScope.Normalize).
        await Store.UpsertAsync(savedAs, ["get_weather"], ["orders.read"]);

        var policy = await Store.GetAsync(requestedAs);

        policy.ShouldNotBeNull();
        // Spelled out, not recomputed: an assertion that applies the rule
        // under test passes whatever the rule does.
        policy.TenantId.ShouldBe("acme");
    }

    [Fact]
    public async Task A_policy_saved_under_a_different_case_replaces_the_existing_one()
    {
        await Store.UpsertAsync("acme", ["get_weather"], ["orders.read"]);
        await Store.UpsertAsync("Acme", ["search_knowledge"], ["orders.write"]);

        // One policy, not two: otherwise which of the pair wins at call time
        // depends on the storage engine's collation.
        var policy = await Store.GetAsync("ACME");

        policy.ShouldNotBeNull();
        policy.AllowedTools.ShouldBe(["search_knowledge"]);
        policy.GrantedPermissions.ShouldBe(["orders.write"]);
    }

    [Fact]
    public async Task A_policy_is_deleted_whatever_case_the_caller_uses()
    {
        await Store.UpsertAsync("acme", ["get_weather"], []);

        (await Store.DeleteAsync("Acme")).ShouldBeTrue();
        (await Store.GetAsync("acme")).ShouldBeNull();
    }

    [Fact]
    public async Task Upsert_returns_the_canonical_tenant()
    {
        var saved = await Store.UpsertAsync("ACME", ["get_weather"], []);

        saved.TenantId.ShouldBe("acme");
    }

    [Fact]
    public async Task Saved_policy_is_read_back_with_both_lists_in_order()
    {
        await Store.UpsertAsync(Tenant, ["get_weather", "search_knowledge"], ["orders.read", "orders.write"]);

        var policy = await Store.GetAsync(Tenant);

        policy.ShouldNotBeNull();
        policy.TenantId.ShouldBe(Tenant);
        policy.AllowedTools.ShouldBe(["get_weather", "search_knowledge"]);
        policy.GrantedPermissions.ShouldBe(["orders.read", "orders.write"]);
    }

    [Fact]
    public async Task Each_list_is_kept_apart_from_the_other()
    {
        // A store that read both lists from one column would show the same names twice. Different
        // values in every position: a swapped pair of columns fails here.
        await Store.UpsertAsync(Tenant, ["only-a-tool"], ["only.a.permission"]);

        var policy = await Store.GetAsync(Tenant);

        policy.ShouldNotBeNull();
        policy.AllowedTools.ShouldBe(["only-a-tool"]);
        policy.GrantedPermissions.ShouldBe(["only.a.permission"]);
    }

    [Fact]
    public async Task Two_empty_lists_are_a_policy_and_not_the_absence_of_one()
    {
        // An empty policy allows nothing; no row allows everything (unless the host requires a policy).
        // A store that dropped the row for two empty lists would turn "no tool" into "every tool".
        await Store.UpsertAsync(Tenant, [], []);

        var policy = await Store.GetAsync(Tenant);

        policy.ShouldNotBeNull();
        policy.AllowedTools.ShouldBeEmpty();
        policy.GrantedPermissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tenant_with_no_saved_policy_has_none()
        => (await Store.GetAsync(Tenant)).ShouldBeNull();

    [Fact]
    public async Task Upsert_replaces_the_existing_policy()
    {
        await Store.UpsertAsync(Tenant, ["get_weather"], ["orders.read"]);
        await Store.UpsertAsync(Tenant, ["search_knowledge", "get_time"], []);

        var policy = await Store.GetAsync(Tenant);

        policy.ShouldNotBeNull();
        policy.AllowedTools.ShouldBe(["search_knowledge", "get_time"]);
        policy.GrantedPermissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Deleting_the_policy_removes_it()
    {
        await Store.UpsertAsync(Tenant, ["get_weather"], ["orders.read"]);

        (await Store.DeleteAsync(Tenant)).ShouldBeTrue();

        (await Store.GetAsync(Tenant)).ShouldBeNull();
        (await Store.DeleteAsync(Tenant)).ShouldBeFalse();
    }

    [Fact]
    public async Task Concurrent_saves_for_one_tenant_leave_one_whole_policy()
    {
        // Twenty writers, each with a policy whose two lists name the same writer. Whatever order they land
        // in, the row that remains is one writer's policy: a store that wrote the two lists in separate
        // statements, or kept two rows, would show a list of one writer next to the list of another.
        var writers = Enumerable.Range(0, 20).ToList();

        await Task.WhenAll(writers.Select(writer => Store.UpsertAsync(
            Tenant,
            [$"tool-{writer}"],
            [$"permission-{writer}"]).AsTask()));

        var policy = await Store.GetAsync(Tenant);

        policy.ShouldNotBeNull();
        policy.AllowedTools.Count.ShouldBe(1);
        policy.GrantedPermissions.Count.ShouldBe(1);

        var tool = policy.AllowedTools[0];
        var writerNumber = tool["tool-".Length..];

        policy.GrantedPermissions[0].ShouldBe($"permission-{writerNumber}");
    }
}
