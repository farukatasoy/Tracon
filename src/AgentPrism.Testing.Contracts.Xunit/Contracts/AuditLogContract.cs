using System.Text.Json;

namespace AgentPrism.Testing.Contracts.Storage;

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
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        var entry = Entry(tenantId, action: "agent.update", entity: name);
        await Log.WriteAsync(entry);
        return entry.Id;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        var entries = await Log.QueryAsync(new AuditQuery { TenantId = tenantId });
        return entries.Any(entry => entry.Id == (Guid)key);
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Log.QueryAsync(new AuditQuery { TenantId = tenantId })).Count;

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
        // K-027 is only about key ORDER, what matters here is semantic
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

    // --- Ambient-tenant fallback (BL-046, Phase 121) ---
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

    // --- Raw-storage tamper hooks (phase 64) ---
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

    // --- Hash chain (phase 64) ---

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
            Id = AgentPrismId.NewId(),
            TenantId = tenantId,
            Actor = "user-1",
            Action = action,
            Entity = entity,
            Before = """{"version":1}""",
            After = """{"version":2}""",
            CreatedAt = DateTimeOffset.UtcNow,
        };
}
