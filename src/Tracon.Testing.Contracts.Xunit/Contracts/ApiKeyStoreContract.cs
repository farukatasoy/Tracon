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
        const int Rounds = 200;
        var revived = 0;

        for (var round = 0; round < Rounds; round++)
        {
            var created = await Store.CreateAsync(Draft(Tenant, $"race-{round}"));
            var id = created.Record.Id;
            var stop = false;

            // The pause between two stamps lets the revocation take the write lock of a store whose lock has no queue: SQLite
            // on Windows kept it waiting for a minute behind a loop that stamped without a pause and failed with "database is
            // locked". The stamps still run beside the revocation, a few hundred of them in each round.
            var touching = Task.Run(async () =>
            {
                while (!Volatile.Read(ref stop))
                {
                    await Store.TouchLastUsedAsync(id, DateTimeOffset.UtcNow);

                    // The store's write lock is a real database lock: the pause is the only way to leave it free for the revocation.
                    await Task.Delay(1); // delay: product
                }
            });

            try
            {
                // Each round starts the revocation at another point of the stamp loop; a stamp is a database round trip, so
                // the offset has to be a span of real time.
                await Task.Delay(round % 7); // delay: product

                (await Store.RevokeAsync(Tenant, id)).ShouldBeTrue();
            }
            finally
            {
                // Also when the revocation threw: a loop that outlives the test holds the database against the next one.
                Volatile.Write(ref stop, true);
                await touching;
            }

            var record = (await Store.ListAsync(Tenant)).Single(key => key.Id == id);

            if (record.RevokedAt is null)
            {
                revived++;
            }
        }

        revived.ShouldBe(0, "a revoked key was active again after a concurrent last-use update");
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
