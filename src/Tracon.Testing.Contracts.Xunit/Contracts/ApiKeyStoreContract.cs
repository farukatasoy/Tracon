using Tracon.Testing.Contracts.Internal;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IApiKeyStore"/> contract.
/// </summary>
/// <remarks>
/// In this contract, the raw key value is visible only in the
/// <see cref="ApiKeyCreationResult.PlaintextKey"/> returned by
/// <see cref="IApiKeyStore.CreateAsync"/>; no read path
/// (<see cref="IApiKeyStore.ListAsync"/>, <see cref="IApiKeyStore.FindByHashAsync"/>)
/// returns the raw value or its hash.
/// </remarks>
public abstract class ApiKeyStoreContract : TenantIsolationContract<IApiKeyStore>
{
    private const string Tenant = "test";

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var created = await Store.CreateAsync(Draft(tenantId, name), cancellationToken);

        return created.Record.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var keys = await Store.ListAsync(tenantId);

        return keys.Any(record => record.Id == (Guid)key);
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

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
    public async Task The_created_record_is_the_record_a_read_reports()
    {
        // The name and the times the row holds, not those of the call: a SQL store writes U+0000 as U+FFFD and
        // PostgreSQL keeps microseconds. The raw key is returned once, beside the record, and is not part of it.
        var expiry = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(7);

        var created = await Store.CreateAsync(Draft(Tenant, "ci\0key", [ApiKeyScope.RunsRead]) with { ExpiresAt = expiry });
        var read = (await Store.ListAsync(Tenant)).ShouldHaveSingleItem();

        created.Record.Id.ShouldBe(read.Id);
        created.Record.TenantId.ShouldBe(read.TenantId);
        created.Record.Name.ShouldBe(read.Name);
        created.Record.KeyPrefix.ShouldBe(read.KeyPrefix);
        created.Record.Scopes.ShouldBe(read.Scopes);
        created.Record.ExpiresAt.ShouldBe(read.ExpiresAt);
        created.Record.CreatedAt.ShouldBe(read.CreatedAt);
        created.PlaintextKey.ShouldNotBeNullOrEmpty();
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
    public async Task A_revoked_key_stays_revoked_while_its_last_use_is_being_recorded()
    {
        // The last-use stamp runs on every authenticated request, so it runs beside a revocation in
        // production. A stamp that reads the active key and writes it back after the revocation
        // brings the key back to life; the revocation was reported as done.
        //
        // Many keys at once, each with a burst of stamps and its revocation on threads of their own, all released together:
        // a store that keeps its rows in memory has a window of a few instructions between the read and the write, and a
        // revocation that lands inside one of many windows, round after round, is how a store that is not atomic is caught.
        // The burst is a few stamps long, not a loop that runs until the revocation is done: the write lock of a store with no
        // queue for it (SQLite on Windows) kept a revocation waiting for a minute behind a loop that stamped without a pause
        // and failed with "database is locked".
        const int Keys = 8;
        const int StampsPerKey = 5;
        const int Rounds = 10;

        for (var round = 0; round < Rounds; round++)
        {
            var ids = new List<Guid>(Keys);

            for (var key = 0; key < Keys; key++)
            {
                ids.Add((await Store.CreateAsync(Draft(Tenant, $"race-{round}-{key}"))).Record.Id);
            }

            var stampedAt = DateTimeOffset.UtcNow;

            var revoked = SimultaneousCalls.Run(
                Keys * 2,
                index =>
                {
                    var id = ids[index % Keys];

                    if (index >= Keys)
                    {
                        return Store.RevokeAsync(Tenant, id).AsTask().GetAwaiter().GetResult();
                    }

                    for (var stamp = 0; stamp < StampsPerKey; stamp++)
                    {
                        Store.TouchLastUsedAsync(id, stampedAt).AsTask().GetAwaiter().GetResult();
                    }

                    return true;
                });

            revoked.ShouldAllBe(static answer => answer, $"round {round}: a revocation was refused");

            var records = await Store.ListAsync(Tenant);

            ids.ShouldAllBe(id => records.Single(key => key.Id == id).RevokedAt != null, $"round {round}: a revoked key was active again after a concurrent last-use update");
        }
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
