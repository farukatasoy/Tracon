using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IAuditLog"/> contract.
/// </summary>
/// <remarks>
/// Every implementation must pass the same scenarios.
/// </remarks>
public abstract class AuditLogContract : TenantIsolationContract<IAuditLog>
{
    /// <summary>The log under test.</summary>
    protected IAuditLog Log => Store;

    /// <inheritdoc />
    /// <remarks>
    /// The audit trail entry carries its own <c>TenantId</c> field and the
    /// query filters by <see cref="AuditQuery.TenantId"/>; it is not read
    /// from the tenant context.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        var entry = Entry(tenantId, action: "agent.update", entity: name);
        await Log.WriteAsync(entry, cancellationToken);
        return entry.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var entries = await Log.QueryAsync(new AuditQuery { TenantId = tenantId });
        return entries.Any(entry => entry.Id == (Guid)key);
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Log.QueryAsync(new AuditQuery { TenantId = tenantId }, cancellationToken)).Count;

    [Fact]
    public async Task Written_entry_round_trips()
    {
        var entry = Entry(tenantId: "tenant-a", action: "agent.update", entity: "agent:support");
        await Log.WriteAsync(entry);

        var found = (await Log.QueryAsync(new AuditQuery { TenantId = "tenant-a" })).ShouldHaveSingleItem();

        found.Id.ShouldBe(entry.Id);
        string.Equals(found.Actor, entry.Actor, StringComparison.Ordinal).ShouldBeTrue();
        found.Action.ShouldBe(entry.Action);
        found.Entity.ShouldBe(entry.Entity);

        // PostgreSQL's jsonb column can change formatting (whitespace);
        // Key ORDER is not the point here; what matters is semantic
        // equality.
        JsonSemanticallyEquals(found.Before, entry.Before);
        JsonSemanticallyEquals(found.After, entry.After);
    }

    [Fact]
    public async Task No_leakage_between_tenants()
    {
        await Log.WriteAsync(Entry(tenantId: "tenant-a", action: "agent.update", entity: "agent:x"));
        await Log.WriteAsync(Entry(tenantId: "tenant-b", action: "agent.update", entity: "agent:x"));

        var mine = await Log.QueryAsync(new AuditQuery { TenantId = "tenant-a" });

        mine.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Action_and_entity_filter_works()
    {
        const string Tenant = "tenant-c";

        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:support"));
        await Log.WriteAsync(Entry(Tenant, "agent.delete", "agent:support"));
        await Log.WriteAsync(Entry(Tenant, "mcp.create", "mcp:github"));

        var byAction = await Log.QueryAsync(new AuditQuery { TenantId = Tenant, Action = "agent.delete" });
        byAction.ShouldHaveSingleItem().Entity.ShouldBe("agent:support");

        var byEntity = await Log.QueryAsync(new AuditQuery { TenantId = Tenant, Entity = "agent:support" });
        byEntity.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Entries_are_returned_newest_to_oldest()
    {
        const string Tenant = "tenant-d";
        var start = DateTimeOffset.UtcNow;

        // Written in reverse order on purpose: the log must do the ordering.
        await Log.WriteAsync(Entry(Tenant, "agent.create", "agent:second") with { CreatedAt = start.AddSeconds(2) });
        await Log.WriteAsync(Entry(Tenant, "agent.create", "agent:first") with { CreatedAt = start });

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = Tenant });

        entries.Select(static entry => entry.Entity).ShouldBe(["agent:second", "agent:first"]);
    }

    [Fact]
    public async Task Limit_constrains_the_result()
    {
        const string Tenant = "tenant-e";

        for (var index = 0; index < 3; index++)
        {
            await Log.WriteAsync(Entry(Tenant, "agent.create", $"agent:{index}"));
        }

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = Tenant, Limit = 2 });

        entries.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Entry_with_unknown_actor_carries_null()
    {
        var entry = Entry("tenant-f", "session.delete", "session:abc") with { Actor = null };
        await Log.WriteAsync(entry);

        var found = (await Log.QueryAsync(new AuditQuery { TenantId = "tenant-f" })).ShouldHaveSingleItem();

        found.Actor.ShouldBeNull();
    }

    // --- Ambient-tenant fallback ---
    // AuditQuery.TenantId == null / AuditChainQuery.TenantId == null is a CONTRACT,
    // not a convenience: it MUST resolve to the caller's own ambient tenant, never to
    // "every tenant". IAuditLog's own remarks make this explicit; these three scenarios
    // are what would fail if an implementation instead treated a missing filter as "no
    // filter" (the shape InMemoryAuditLog had before this defect was closed).

    [Fact]
    public async Task Null_tenant_resolves_to_the_ambient_tenant_and_does_not_leak_others()
    {
        AmbientTenant.TenantId = TenantA;

        await Log.WriteAsync(Entry(TenantA, "agent.update", "agent:mine"));
        await Log.WriteAsync(Entry(TenantB, "agent.update", "agent:theirs"));

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = null });

        // Both directions, same reasoning as TenantIsolationContract: a query that
        // silently returns nothing would also "not leak" without proving the fallback
        // actually reached tenant A's own data.
        entries.ShouldHaveSingleItem().Entity.ShouldBe("agent:mine");
    }

    [Fact]
    public async Task Null_tenant_follows_the_ambient_tenant_when_it_changes()
    {
        AmbientTenant.TenantId = TenantA;
        await Log.WriteAsync(Entry(TenantA, "agent.update", "agent:a-owned"));

        AmbientTenant.TenantId = TenantB;
        await Log.WriteAsync(Entry(TenantB, "agent.update", "agent:b-owned"));

        // The store must consult the CURRENT ambient tenant on every call, not one
        // captured when the store was constructed.
        var entries = await Log.QueryAsync(new AuditQuery { TenantId = null });

        entries.ShouldHaveSingleItem().Entity.ShouldBe("agent:b-owned");
    }

    [Fact]
    public async Task VerifyChainAsync_with_null_tenant_checks_the_ambient_tenants_own_chain()
    {
        AmbientTenant.TenantId = TenantA;
        await Log.WriteAsync(Entry(TenantA, "agent.update", "agent:mine"));

        AmbientTenant.TenantId = TenantB;
        await Log.WriteAsync(Entry(TenantB, "agent.update", "agent:theirs"));

        AmbientTenant.TenantId = TenantA;
        var result = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = null });

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(1, "the null-tenant chain walk must scope to tenant A alone, not every tenant's entries.");
    }

    // --- Raw-storage tamper hooks ---
    // IAuditLog has (deliberately, "there is no delete or edit endpoint, and
    // there will not be one") no way to alter or remove a written entry, so
    // Broken/Gap detection cannot be driven through the public interface alone
    // — a subclass backed by real storage overrides these three members to
    // reach the raw row directly. The in-memory store leaves them unsupported;
    // the tests below skip when that is the case.

    /// <summary>Whether this store's backing state can be tampered with directly (SQL providers only).</summary>
    protected virtual bool SupportsRawTamper => false;

    /// <summary>Runs raw SQL against the backing store. Only called when <see cref="SupportsRawTamper"/> is <see langword="true"/>.</summary>
    protected virtual ValueTask ExecuteRawAsync(string sql) => throw new NotSupportedException();

    /// <summary>The fully qualified <c>audit_log</c> table reference this provider's raw SQL should use.</summary>
    protected virtual string QualifiedAuditLogTable => throw new NotSupportedException();

    /// <summary>
    /// Formats an id for embedding in raw SQL. A provider that stores a
    /// <see cref="Guid"/> as text and compares case-sensitively (SQLite, in
    /// uppercase) must override the default (lowercase
    /// <see cref="Guid.ToString()"/>).
    /// </summary>
    protected virtual string FormatIdForRawSql(Guid id) => id.ToString();

    [Fact]
    public async Task Tampering_a_row_directly_is_detected_as_broken()
    {
        if (!SupportsRawTamper)
        {
            return;
        }

        const string Tenant = "tenant-tamper-broken";

        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:first"));
        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:second"));

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = Tenant });
        var target = entries.Single(static e => string.Equals(e.Entity, "agent:first", StringComparison.Ordinal));

        await ExecuteRawAsync(
            $"UPDATE {QualifiedAuditLogTable} SET after = '{{\"tampered\":true}}' " +
            $"WHERE id = '{FormatIdForRawSql(target.Id)}';");

        var result = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(target.Id);
    }

    [Fact]
    public async Task Deleting_a_row_directly_is_detected_as_a_gap()
    {
        if (!SupportsRawTamper)
        {
            return;
        }

        const string Tenant = "tenant-tamper-gap";

        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:first"));
        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:second"));
        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:third"));

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = Tenant });
        var middle = entries.Single(static e => string.Equals(e.Entity, "agent:second", StringComparison.Ordinal));

        await ExecuteRawAsync($"DELETE FROM {QualifiedAuditLogTable} WHERE id = '{FormatIdForRawSql(middle.Id)}';");

        var result = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Gap);
    }

    // --- Hash chain ---

    [Fact]
    public async Task First_entry_of_a_tenant_carries_no_previous_hash()
    {
        var entry = Entry("tenant-chain-1", "agent.update", "agent:x");
        await Log.WriteAsync(entry);

        var found = (await Log.QueryAsync(new AuditQuery { TenantId = "tenant-chain-1" })).ShouldHaveSingleItem();

        found.PreviousHash.ShouldBeNull();
        found.Hash.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Second_entry_links_to_the_first()
    {
        const string Tenant = "tenant-chain-2";

        await Log.WriteAsync(Entry(Tenant, "agent.create", "agent:first"));
        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:second"));

        var entries = await Log.QueryAsync(new AuditQuery { TenantId = Tenant });
        var first = entries.Single(static e => string.Equals(e.Entity, "agent:first", StringComparison.Ordinal));
        var second = entries.Single(static e => string.Equals(e.Entity, "agent:second", StringComparison.Ordinal));

        second.PreviousHash.ShouldBe(first.Hash);
    }

    [Fact]
    public async Task VerifyChainAsync_reports_valid_for_a_normally_written_chain()
    {
        const string Tenant = "tenant-chain-3";

        for (var i = 0; i < 5; i++)
        {
            await Log.WriteAsync(Entry(Tenant, "agent.update", $"agent:{i}"));
        }

        var result = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(5);
    }

    [Fact]
    public async Task Two_tenants_chains_verify_independently()
    {
        const string TenantX = "tenant-chain-4a";
        const string TenantY = "tenant-chain-4b";

        await Log.WriteAsync(Entry(TenantX, "agent.update", "agent:x1"));
        await Log.WriteAsync(Entry(TenantX, "agent.update", "agent:x2"));
        await Log.WriteAsync(Entry(TenantY, "agent.update", "agent:y1"));

        var resultX = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = TenantX });
        var resultY = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = TenantY });

        resultX.Status.ShouldBe(AuditChainStatus.Valid);
        resultX.EntriesChecked.ShouldBe(2);
        resultY.Status.ShouldBe(AuditChainStatus.Valid);
        resultY.EntriesChecked.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_writes_for_one_tenant_produce_a_single_valid_chain()
    {
        // 🚨 The scenario the retry-on-uniqueness-violation (SQL) / per-tenant
        // lock (in-memory) machinery exists for: N writers racing to read the
        // SAME "last hash" must still end up as ONE unbroken chain, never a fork.
        const string Tenant = "tenant-chain-concurrent";
        const int WriterCount = 20;

        var writers = Enumerable.Range(0, WriterCount)
            .Select(i => Log.WriteAsync(Entry(Tenant, "agent.update", $"agent:{i}")).AsTask());

        await Task.WhenAll(writers);

        var result = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid, $"chain forked or broke under {WriterCount} concurrent writers");
        result.EntriesChecked.ShouldBe(WriterCount);
    }

    // --- Signed chain ---
    // A log built with a signing key writes an HMAC-SHA256 hash and the key's id; the key value itself never
    // reaches the store. The scenarios below need a SECOND log over the same storage (a different key set, or
    // none), which only an implementation backed by shared storage can offer: it overrides the two members below.

    private static readonly byte[] KeyOne = Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray();
    private static readonly byte[] KeyTwo = Enumerable.Range(101, 32).Select(static i => (byte)i).ToArray();

    /// <summary>
    /// Builds a log of the implementation under test that signs new entries with the key named
    /// <paramref name="signingKeyId"/> (or writes them unsigned when it is <see langword="null"/>) and checks
    /// entries with <paramref name="keys"/>. Return <see langword="null"/> when the implementation cannot be
    /// built with keys; the signed-chain scenarios then do nothing.
    /// </summary>
    /// <param name="signingKeyId">The id of the signing key, or <see langword="null"/> for none.</param>
    /// <param name="keys">Every key the log holds, by id. Each value is 32 bytes.</param>
    /// <returns>The log, or <see langword="null"/>.</returns>
    protected virtual ValueTask<IAuditLog?> CreateKeyedLogAsync(string? signingKeyId, IReadOnlyDictionary<string, byte[]> keys)
        => ValueTask.FromResult<IAuditLog?>(null);

    /// <summary>
    /// Whether a log built by <see cref="CreateKeyedLogAsync"/> reads and writes the SAME storage as
    /// <see cref="Log"/> (SQL providers). The scenarios that mix unsigned and signed history, or change the key
    /// set between two verifications, run only then.
    /// </summary>
    protected virtual bool KeyedLogsShareStorage => false;

    [Fact]
    public async Task A_keyed_chain_verifies_as_valid()
    {
        const string Tenant = "tenant-key-valid";

        if (await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        for (var i = 0; i < 5; i++)
        {
            await signed.WriteAsync(Entry(Tenant, "agent.update", $"agent:{i}"));
        }

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(5);
        result.UnkeyedEntries.ShouldBe(0);

        var entries = await signed.QueryAsync(new AuditQuery { TenantId = Tenant });

        entries.ShouldAllBe(static entry => entry.KeyId == "k1" && !string.IsNullOrEmpty(entry.Hash));
    }

    [Fact]
    public async Task The_write_path_sets_the_key_id_and_ignores_a_caller_supplied_one()
    {
        const string Tenant = "tenant-key-ignored";

        // The log under test has no key: a key id the caller hands in must not reach the row.
        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:plain") with { KeyId = "forged" });

        var unsigned = (await Log.QueryAsync(new AuditQuery { TenantId = Tenant })).ShouldHaveSingleItem();

        unsigned.KeyId.ShouldBeNull();

        if (await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        const string SignedTenant = "tenant-key-ignored-signed";

        await signed.WriteAsync(Entry(SignedTenant, "agent.update", "agent:signed") with { KeyId = "forged" });

        var written = (await signed.QueryAsync(new AuditQuery { TenantId = SignedTenant })).ShouldHaveSingleItem();

        written.KeyId.ShouldBe("k1");
    }

    [Fact]
    public async Task Two_tenants_keyed_chains_verify_independently()
    {
        if (await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        await signed.WriteAsync(Entry("tenant-key-x", "agent.update", "agent:x1"));
        await signed.WriteAsync(Entry("tenant-key-x", "agent.update", "agent:x2"));
        await signed.WriteAsync(Entry("tenant-key-y", "agent.update", "agent:y1"));

        var resultX = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = "tenant-key-x" });
        var resultY = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = "tenant-key-y" });

        resultX.Status.ShouldBe(AuditChainStatus.Valid);
        resultX.EntriesChecked.ShouldBe(2);
        resultY.Status.ShouldBe(AuditChainStatus.Valid);
        resultY.EntriesChecked.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_keyed_writes_for_one_tenant_produce_a_single_valid_chain()
    {
        // The same race as the unsigned scenario: every retry re-reads the last hash and re-derives its own
        // HMAC from it, so the signed chain must converge to one chain too.
        const string Tenant = "tenant-key-concurrent";
        const int WriterCount = 20;

        if (await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        var writers = Enumerable.Range(0, WriterCount)
            .Select(i => signed.WriteAsync(Entry(Tenant, "agent.update", $"agent:{i}")).AsTask());

        await Task.WhenAll(writers);

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid, $"signed chain forked or broke under {WriterCount} concurrent writers");
        result.EntriesChecked.ShouldBe(WriterCount);
    }

    [Fact]
    public async Task Unkeyed_history_before_the_first_keyed_entry_stays_valid_and_is_counted()
    {
        const string Tenant = "tenant-key-history";

        if (!KeyedLogsShareStorage || await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        for (var i = 0; i < 3; i++)
        {
            await Log.WriteAsync(Entry(Tenant, "agent.update", $"agent:old-{i}"));
        }

        for (var i = 0; i < 2; i++)
        {
            await signed.WriteAsync(Entry(Tenant, "agent.update", $"agent:new-{i}"));
        }

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(5);
        result.UnkeyedEntries.ShouldBe(3);

        // A host that never had the key cannot check the signed rows, and says so instead of calling them forged.
        var withoutKey = await Log.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        withoutKey.Status.ShouldBe(AuditChainStatus.UnknownKey);
    }

    [Fact]
    public async Task A_chain_signed_by_two_keys_verifies_after_rotation()
    {
        const string Tenant = "tenant-key-rotation";

        if (!KeyedLogsShareStorage
            || await CreateKeyedLogAsync("old", Keys(("old", KeyOne))) is not { } beforeRotation
            || await CreateKeyedLogAsync("new", Keys(("old", KeyOne), ("new", KeyTwo))) is not { } afterRotation
            || await CreateKeyedLogAsync("new", Keys(("new", KeyTwo))) is not { } oldKeyRemoved)
        {
            return;
        }

        await beforeRotation.WriteAsync(Entry(Tenant, "agent.update", "agent:1"));
        await beforeRotation.WriteAsync(Entry(Tenant, "agent.update", "agent:2"));
        await afterRotation.WriteAsync(Entry(Tenant, "agent.update", "agent:3"));
        await afterRotation.WriteAsync(Entry(Tenant, "agent.update", "agent:4"));

        var result = await afterRotation.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(4);
        result.UnkeyedEntries.ShouldBe(0);

        var entries = await afterRotation.QueryAsync(new AuditQuery { TenantId = Tenant });

        entries.OrderBy(static entry => entry.Entity, StringComparer.Ordinal).Select(static entry => entry.KeyId)
            .ShouldBe(["old", "old", "new", "new"]);

        // Removing the old key from configuration leaves rows the walk cannot check: UnknownKey, never Valid.
        var removed = await oldKeyRemoved.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        removed.Status.ShouldBe(AuditChainStatus.UnknownKey);
        removed.FirstFailingEntryId.ShouldNotBeNull();
        entries.Single(entry => entry.Id == removed.FirstFailingEntryId).Entity.ShouldBe("agent:1");
    }

    [Fact]
    public async Task Turning_the_key_off_keeps_the_entry_and_the_walk_reports_the_downgrade()
    {
        const string Tenant = "tenant-key-off";

        if (!KeyedLogsShareStorage || await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:1"));
        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:2"));

        // The key is turned off and the host restarts: the audit entry is still written (an audit trail must
        // not lose a record), unsigned.
        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:3-unsigned"));

        var entries = await signed.QueryAsync(new AuditQuery { TenantId = Tenant });
        var unsignedRow = entries.Single(static entry => string.Equals(entry.Entity, "agent:3-unsigned", StringComparison.Ordinal));

        entries.Count.ShouldBe(3);
        unsignedRow.KeyId.ShouldBeNull();

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(unsignedRow.Id);
    }

    [Fact]
    public async Task Two_writers_with_a_mixed_key_configuration_leave_the_chain_broken_at_the_unsigned_entry_for_good()
    {
        // 🚨 The rollout trap. Two replicas of one host write the same tenant while the key is being turned on: one
        // already has it, the other has not restarted yet (or is the previous release). The unsigned row lands after
        // a signed one, the walk reads that as a downgrade, and the row cannot be re-signed afterwards because
        // re-signing would rewrite history. Every later row, signed or not, leaves the chain where it is: broken at
        // the FIRST unsigned row. The guide tells the operator to move every replica to the key in one restart.
        const string Tenant = "tenant-key-mixed-writers";

        if (!KeyedLogsShareStorage || await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } restarted)
        {
            return;
        }

        await restarted.WriteAsync(Entry(Tenant, "agent.update", "agent:1-signed"));
        await Log.WriteAsync(Entry(Tenant, "agent.update", "agent:2-unsigned-by-the-old-replica"));
        await restarted.WriteAsync(Entry(Tenant, "agent.update", "agent:3-signed"));

        var entries = await restarted.QueryAsync(new AuditQuery { TenantId = Tenant });
        var firstUnsigned = entries.Single(static entry => entry.KeyId is null);

        var first = await restarted.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        first.Status.ShouldBe(AuditChainStatus.Broken);
        first.FirstFailingEntryId.ShouldBe(firstUnsigned.Id);
        firstUnsigned.Entity.ShouldBe("agent:2-unsigned-by-the-old-replica");

        // More signed writes do not heal it, and the unsigned row is still the first failing one.
        await restarted.WriteAsync(Entry(Tenant, "agent.update", "agent:4-signed"));

        var later = await restarted.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        later.Status.ShouldBe(AuditChainStatus.Broken);
        later.FirstFailingEntryId.ShouldBe(firstUnsigned.Id);
        later.EntriesChecked.ShouldBe(4);
    }

    [Fact]
    public async Task Downgrading_a_keyed_entry_is_detected_as_broken()
    {
        // 🚨 The attack the key exists to stop. An actor with write access to the table and no key rewrites
        // the last signed row: new content, `key_id` dropped, and a plain SHA-256 recomputed over the same
        // canonical form (public, and reimplemented below on purpose: this scenario must not trust the code
        // under test to define what a valid unsigned hash is). The row is now self-consistent and linked, and
        // only the order (unsigned after signed) gives it away.
        const string Tenant = "tenant-key-downgrade";

        if (!SupportsRawTamper || await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:first"));
        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:second"));
        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:third"));

        var entries = await signed.QueryAsync(new AuditQuery { TenantId = Tenant });
        var target = entries.Single(static e => string.Equals(e.Entity, "agent:third", StringComparison.Ordinal));
        const string Tampered = "{\"tampered\":true}";
        var plainHash = PlainSha256(target with { After = Tampered });

        await ExecuteRawAsync(
            $"UPDATE {QualifiedAuditLogTable} SET after = '{Tampered}', key_id = NULL, hash = '{plainHash}' " +
            $"WHERE id = '{FormatIdForRawSql(target.Id)}';");

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(target.Id);
    }

    [Fact]
    public async Task Recomputing_a_keyed_row_without_the_key_is_detected_as_broken()
    {
        // 🚨 The promise of the key. An actor with write access to the table and NO key changes the last row,
        // keeps its `key_id` and recomputes the hash the only way open to them: a plain SHA-256 over the same
        // canonical form (which now names the key id). Before keys existed this exact edit was invisible.
        const string Tenant = "tenant-key-recompute";

        if (!SupportsRawTamper || await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:first"));
        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:second"));

        var entries = await signed.QueryAsync(new AuditQuery { TenantId = Tenant });
        var target = entries.Single(static e => string.Equals(e.Entity, "agent:second", StringComparison.Ordinal));
        const string Tampered = "{\"tampered\":true}";
        var forgedHash = PlainSha256(target with { After = Tampered }, keyId: "k1");

        await ExecuteRawAsync(
            $"UPDATE {QualifiedAuditLogTable} SET after = '{Tampered}', hash = '{forgedHash}' " +
            $"WHERE id = '{FormatIdForRawSql(target.Id)}';");

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(target.Id);
    }

    [Fact]
    public async Task Re_signing_under_another_key_id_is_detected_as_broken()
    {
        // The key id is inside the signed text: pointing a row at the other KNOWN id breaks the row even when
        // both ids hold the same key bytes (an operator who registered one value twice), so the label cannot
        // be moved without a new signature over the new text.
        const string Tenant = "tenant-key-relabel";

        if (!SupportsRawTamper || await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne), ("k2", KeyOne))) is not { } signed)
        {
            return;
        }

        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:first"));
        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:second"));

        var entries = await signed.QueryAsync(new AuditQuery { TenantId = Tenant });
        var target = entries.Single(static e => string.Equals(e.Entity, "agent:second", StringComparison.Ordinal));

        await ExecuteRawAsync(
            $"UPDATE {QualifiedAuditLogTable} SET key_id = 'k2' WHERE id = '{FormatIdForRawSql(target.Id)}';");

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(target.Id);
    }

    [Fact]
    public async Task Tampering_a_keyed_row_directly_is_detected_as_broken()
    {
        const string Tenant = "tenant-key-tamper";

        if (!SupportsRawTamper || await CreateKeyedLogAsync("k1", Keys(("k1", KeyOne))) is not { } signed)
        {
            return;
        }

        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:first"));
        await signed.WriteAsync(Entry(Tenant, "agent.update", "agent:second"));

        var entries = await signed.QueryAsync(new AuditQuery { TenantId = Tenant });
        var target = entries.Single(static e => string.Equals(e.Entity, "agent:first", StringComparison.Ordinal));

        await ExecuteRawAsync(
            $"UPDATE {QualifiedAuditLogTable} SET after = '{{\"tampered\":true}}' WHERE id = '{FormatIdForRawSql(target.Id)}';");

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Broken);
        result.FirstFailingEntryId.ShouldBe(target.Id);
    }

    private static Dictionary<string, byte[]> Keys(params (string Id, byte[] Value)[] keys)
        => keys.ToDictionary(static key => key.Id, static key => key.Value, StringComparer.Ordinal);

    /// <summary>
    /// The plain SHA-256 hash of an entry's unsigned canonical form, computed independently of the code under
    /// test: the fixed field order, the four escaped characters and the microsecond timestamp are the on-disk contract.
    /// </summary>
    private static string PlainSha256(AuditEntry entry, string? keyId = null)
    {
        var ticks = entry.CreatedAt.UtcTicks - (entry.CreatedAt.UtcTicks % 10);
        var createdAt = new DateTimeOffset(ticks, TimeSpan.Zero).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);

        var canonical =
            "{\"prevHash\":" + JsonText(entry.PreviousHash)
            + ",\"tenantId\":" + JsonText(entry.TenantId)
            + ",\"actor\":" + JsonText(entry.Actor)
            + ",\"action\":" + JsonText(entry.Action)
            + ",\"entity\":" + JsonText(entry.Entity)
            + ",\"before\":" + JsonText(entry.Before)
            + ",\"after\":" + JsonText(entry.After)
            + ",\"createdAt\":\"" + createdAt + "\""
            + (keyId is null ? string.Empty : ",\"keyId\":" + JsonText(keyId))
            + "}";

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string JsonText(string? value)
        => value is null
            ? "null"
            : "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";

    private static void JsonSemanticallyEquals(string? actual, string? expected)
    {
        if (expected is null)
        {
            actual.ShouldBeNull();
            return;
        }

        actual.ShouldNotBeNull();

        using var actualDoc = JsonDocument.Parse(actual);
        using var expectedDoc = JsonDocument.Parse(expected);

        actualDoc.RootElement.GetRawText().Replace(" ", "", StringComparison.Ordinal)
            .ShouldBe(expectedDoc.RootElement.GetRawText().Replace(" ", "", StringComparison.Ordinal));
    }

    private static AuditEntry Entry(string tenantId, string action, string entity)
        => new()
        {
            Id = TraconId.NewId(),
            TenantId = tenantId,
            Actor = "user-1",
            Action = action,
            Entity = entity,
            Before = """{"version":1}""",
            After = """{"version":2}""",
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
