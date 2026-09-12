namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// An <see cref="ITenantContext"/> whose tenant can be swapped at run time.
/// </summary>
/// <remarks>
/// Some stores take the tenant as a method parameter, others read it from
/// <see cref="ITenantContext"/> (for example <c>IRunStore</c>,
/// <c>ISessionStore</c>, <c>IAgentDefinitionStore</c>). To write a two-tenant
/// scenario for the second group, we need either two separate store
/// instances or a single context whose tenant can be swapped. The latter was
/// chosen: the same store instance points at the same backend, and the
/// question "does B see what A wrote" can be asked against a single store.
/// </remarks>
/// <param name="tenantId">The initial tenant.</param>
public sealed class MutableTenantContext(string tenantId) : ITenantContext
{
    /// <inheritdoc />
    public string TenantId { get; set; } = tenantId;
}

/// <summary>
/// The shared base that checks a store's tenant isolation <strong>both
/// ways</strong>.
/// </summary>
/// <typeparam name="TStore">The store type under test.</typeparam>
/// <remarks>
/// <para>
/// This base also carries the lifecycle plumbing shared
/// by every store contract; derived contracts write only their own scenarios
/// and the four isolation hooks.
/// </para>
/// <para>
/// <strong>The two-way check cannot be skipped.</strong> If only "B must
/// not see it" is checked, a broken query that returns nothing at all would
/// also pass the test. Every scenario therefore also asks "does A see its
/// own data", proving the filter is both sufficient and not too narrow.
/// </para>
/// </remarks>
public abstract class TenantIsolationContract<TStore> : IAsyncLifetime
{
    /// <summary>The tenant that writes the data.</summary>
    protected const string TenantA = "tenant-a";

    /// <summary>The tenant that must not see the data.</summary>
    protected const string TenantB = "tenant-b";

    /// <summary>
    /// The tenant stores read from <see cref="ITenantContext"/>. For stores
    /// that do not take the tenant as a parameter, the contract sets this
    /// property to the relevant tenant on the first line of its hooks.
    /// </summary>
    protected MutableTenantContext AmbientTenant { get; private set; } = new(TenantA);

    /// <summary>The store under test.</summary>
    protected TStore Store { get; private set; } = default!;

    /// <summary>Produces an empty store for the test.</summary>
    /// <returns>A store ready for use.</returns>
    protected abstract ValueTask<TStore> CreateStoreAsync();

    /// <summary>
    /// Attaches to a tenant context shared per class (used by multiple
    /// tests) and resets the tenant back to <see cref="TenantA"/>.
    /// </summary>
    /// <param name="tenant">The shared context supplied by the schema/test CLASS fixture.</param>
    /// <remarks>
    /// When store instances are set up once per class (see the schema
    /// fixtures), they all capture the SAME <see cref="ITenantContext"/>
    /// object; each test must therefore call this method inside
    /// <see cref="CreateStoreAsync"/> to attach to that object and reset any
    /// state a previous test may have left on <see cref="TenantB"/>.
    /// </remarks>
    protected void UseAmbientTenant(MutableTenantContext tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        AmbientTenant = tenant;
        AmbientTenant.TenantId = TenantA;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Hook for a derived class to release its own resources.</summary>
    /// <returns>A completed task.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    // --- Tenant isolation hooks (phase 41) ---

    /// <summary>Writes a sample record for the given tenant.</summary>
    /// <param name="tenantId">The tenant that owns the record.</param>
    /// <param name="name">The name distinguishing the record. The same name may be used by two tenants.</param>
    /// <returns>The key to use for reading the record back.</returns>
    protected abstract ValueTask<object> SeedAsync(string tenantId, string name);

    /// <summary>Reports whether the record is visible from the given tenant's viewpoint.</summary>
    /// <param name="tenantId">The reading tenant.</param>
    /// <param name="key">The key returned by <see cref="SeedAsync"/>.</param>
    /// <returns><see langword="true"/> if the record is visible.</returns>
    protected abstract ValueTask<bool> ExistsAsync(string tenantId, object key);

    /// <summary>Reports how many records the given tenant sees through the listing endpoint.</summary>
    /// <param name="tenantId">The reading tenant.</param>
    /// <returns>The number of records seen.</returns>
    protected abstract ValueTask<int> CountAsync(string tenantId);

    /// <summary>
    /// Attempts to delete the record on behalf of the given tenant.
    /// </summary>
    /// <param name="tenantId">The tenant attempting the delete.</param>
    /// <param name="key">The key returned by <see cref="SeedAsync"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the delete succeeded, <see langword="false"/>
    /// if the record was not found; <see langword="null"/> if the store does
    /// not offer deletion.
    /// </returns>
    protected virtual ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => new((bool?)null);

    /// <summary>
    /// Attempts to change the record's content on behalf of the given
    /// tenant.
    /// </summary>
    /// <param name="tenantId">The tenant attempting the update.</param>
    /// <param name="name">The record's name.</param>
    /// <returns>
    /// <see langword="true"/> if the update was attempted; <see langword="false"/>
    /// if the store does not support this scenario.
    /// </returns>
    /// <remarks>
    /// The default implementation calls <see cref="SeedAsync"/> a second
    /// time: writing a record with the same name verifies that two tenants
    /// can carry the same name independently.
    /// </remarks>
    protected virtual async ValueTask<bool> TryOverwriteAsync(string tenantId, string name)
    {
        await SeedAsync(tenantId, name);
        return true;
    }

    // --- Isolation tests ---

    [Fact]
    public async Task Tenant_cannot_read_another_tenants_record()
    {
        var key = await SeedAsync(TenantA, "secret");

        (await ExistsAsync(TenantB, key)).ShouldBeFalse(
            "Tenant B can read tenant A's record.");
    }

    [Fact]
    public async Task Tenant_reads_its_own_record()
    {
        // 🚨 The second direction. Without this check, a broken query that
        // returns nothing at all would also pass the isolation test.
        var key = await SeedAsync(TenantA, "secret");

        (await ExistsAsync(TenantA, key)).ShouldBeTrue(
            "Tenant A cannot read its own record; the filter is too narrow.");
    }

    [Fact]
    public async Task Tenant_does_not_see_another_tenants_record_in_the_list()
    {
        await SeedAsync(TenantA, "secret");

        (await CountAsync(TenantB)).ShouldBe(0, "The listing endpoint leaks across tenants.");
        (await CountAsync(TenantA)).ShouldBeGreaterThan(0, "The tenant cannot list its own record.");
    }

    [Fact]
    public async Task Tenant_cannot_delete_another_tenants_record()
    {
        var key = await SeedAsync(TenantA, "secret");

        var deleted = await TryDeleteAsync(TenantB, key);

        if (deleted is null)
        {
            // The store does not offer deletion; the scenario does not apply.
            return;
        }

        deleted.Value.ShouldBeFalse("Tenant B can delete tenant A's record.");
        (await ExistsAsync(TenantA, key)).ShouldBeTrue("The record disappeared after a failed delete attempt.");

        // It must be able to delete its own record; otherwise the delete
        // query would be broken altogether.
        (await TryDeleteAsync(TenantA, key))!.Value.ShouldBeTrue("The tenant cannot delete its own record.");
    }

    [Fact]
    public async Task Same_name_lives_independently_across_two_tenants()
    {
        var keyA = await SeedAsync(TenantA, "shared-name");

        if (!await TryOverwriteAsync(TenantB, "shared-name"))
        {
            return;
        }

        (await ExistsAsync(TenantA, keyA)).ShouldBeTrue(
            "Tenant B writing under the same name overwrote tenant A's record.");
        (await CountAsync(TenantA)).ShouldBe(1);
        (await CountAsync(TenantB)).ShouldBe(1);
    }
}
