namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IApiKeyStore"/> contract.
/// </summary>
/// <remarks>
/// 🚨 In this contract, the raw key value is visible only in the
/// <see cref="ApiKeyCreationResult.PlaintextKey"/> returned by
/// <see cref="IApiKeyStore.CreateAsync"/>; no read path
/// (<see cref="IApiKeyStore.ListAsync"/>, <see cref="IApiKeyStore.FindByHashAsync"/>)
/// returns the raw value or its hash (section 53.2).
/// </remarks>
public abstract class ApiKeyStoreContract : TenantIsolationContract<IApiKeyStore>
{
    private const string Tenant = "test";

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var created = await Store.CreateAsync(Draft(tenantId, name));

        return created.Record.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var keys = await Store.ListAsync(tenantId);

        return keys.Any(record => record.Id == (Guid)key);
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.RevokeAsync(tenantId, (Guid)key);

    [Fact]
    public async Task Saved_key_is_read_back()
    {
        await Store.CreateAsync(Draft(Tenant, "ci", [ApiKeyScope.RunsRead, ApiKeyScope.AgentsRead]));

        var keys = await Store.ListAsync(Tenant);

        keys.Count.ShouldBe(1);
        keys[0].Name.ShouldBe("ci");
        keys[0].TenantId.ShouldBe(Tenant);
        keys[0].Scopes.ShouldBe([ApiKeyScope.RunsRead, ApiKeyScope.AgentsRead]);
        keys[0].KeyPrefix.ShouldStartWith("ap_");
        keys[0].RevokedAt.ShouldBeNull();
        keys[0].IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Found_by_hash_of_the_raw_value()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));
        var hash = ApiKeyGenerator.ComputeHash(created.PlaintextKey);

        var found = await Store.FindByHashAsync(hash);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(created.Record.Id);
    }

    [Fact]
    public async Task Unknown_hash_is_not_found()
    {
        await Store.CreateAsync(Draft(Tenant, "ci"));

        var randomHash = ApiKeyGenerator.ComputeHash("ap_other_value_never_saved");

        (await Store.FindByHashAsync(randomHash)).ShouldBeNull();
    }

    [Fact]
    public async Task Revoked_key_carries_RevokedAt_and_appears_inactive()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));

        (await Store.RevokeAsync(Tenant, created.Record.Id)).ShouldBeTrue();

        var loaded = (await Store.ListAsync(Tenant)).Single(record => record.Id == created.Record.Id);

        loaded.RevokedAt.ShouldNotBeNull();
        loaded.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Same_key_cannot_be_revoked_twice()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));

        (await Store.RevokeAsync(Tenant, created.Record.Id)).ShouldBeTrue();
        (await Store.RevokeAsync(Tenant, created.Record.Id)).ShouldBeFalse();
    }

    [Fact]
    public async Task Cannot_revoke_another_tenants_key()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));

        (await Store.RevokeAsync("other", created.Record.Id)).ShouldBeFalse();

        var loaded = (await Store.ListAsync(Tenant)).Single(record => record.Id == created.Record.Id);
        loaded.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Last_used_timestamp_is_updated()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "ci"));
        var usedAt = new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

        await Store.TouchLastUsedAsync(created.Record.Id, usedAt);

        var loaded = (await Store.ListAsync(Tenant)).Single(record => record.Id == created.Record.Id);
        loaded.LastUsedAt.ShouldBe(usedAt);
    }

    [Fact]
    public async Task Expiration_is_read_back()
    {
        var expiresAt = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await Store.CreateAsync(Draft(Tenant, "ci") with { ExpiresAt = expiresAt });

        var loaded = (await Store.ListAsync(Tenant)).Single();
        loaded.ExpiresAt.ShouldBe(expiresAt);
    }

    [Fact]
    public async Task Active_scope_is_found_system_wide()
    {
        (await Store.HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)).ShouldBeFalse();

        await Store.CreateAsync(Draft(Tenant, "mcp", [ApiKeyScope.ExternalInvoke]));

        (await Store.HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)).ShouldBeTrue();
        (await Store.HasActiveScopeAsync(ApiKeyScope.AgentsAdmin)).ShouldBeFalse();
    }

    [Fact]
    public async Task Revoked_keys_scope_is_no_longer_active()
    {
        var created = await Store.CreateAsync(Draft(Tenant, "mcp", [ApiKeyScope.ExternalInvoke]));
        await Store.RevokeAsync(Tenant, created.Record.Id);

        (await Store.HasActiveScopeAsync(ApiKeyScope.ExternalInvoke)).ShouldBeFalse();
    }

    [Fact]
    public async Task Finding_another_tenants_key_by_hash_returns_the_correct_tenant()
    {
        var created = await Store.CreateAsync(Draft("other", "ci"));
        var hash = ApiKeyGenerator.ComputeHash(created.PlaintextKey);

        var found = await Store.FindByHashAsync(hash);

        found.ShouldNotBeNull();
        found.TenantId.ShouldBe("other");
    }

    private static ApiKeyDraft Draft(string tenantId, string name, IReadOnlyList<ApiKeyScope>? scopes = null)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Scopes = scopes ?? [ApiKeyScope.RunsRead],
        };
}
