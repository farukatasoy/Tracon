using System.Text.Json;

namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IAuditLog"/> contract.
/// </summary>
/// <remarks>
/// Added in phase 9. The in-memory log and the PostgreSQL log must pass the
/// same scenarios.
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
