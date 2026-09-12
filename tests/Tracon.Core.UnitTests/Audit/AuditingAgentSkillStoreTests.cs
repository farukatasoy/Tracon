using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// <see cref="AuditingAgentSkillStore"/> produces an audit trail on its write
/// paths and does not interrupt the operation when the ledger fails (F-170).
/// </summary>
/// <remarks>
/// Before this decorator existed, <c>PUT</c> and <c>DELETE /api/skills/{name}</c>
/// changed the instructions and the server-side scripts an agent runs and wrote
/// NOTHING to the trail — while the store that grants those scripts permission
/// to run was audited. The trail recorded who permitted a script and not who
/// wrote it.
/// </remarks>
public sealed class AuditingAgentSkillStoreTests
{
    // Every query below names its tenant on purpose: InMemoryAuditLog.QueryAsync
    // falls back to the AMBIENT tenant when the query carries none, and these
    // writes are filed under the SKILL's own tenant (IAgentSkillStore takes
    // tenantId explicitly and never reads ITenantContext). A bare query would
    // read a different trail than the one written.

    [Fact]
    public async Task New_skill_is_written_as_create()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(Skill("tenant-a", "invoicing"));

        var entry = (await log.QueryAsync(new AuditQuery { TenantId = "tenant-a" })).ShouldHaveSingleItem();
        entry.Action.ShouldBe("skill.create");
        entry.Entity.ShouldBe("skill:invoicing");
        entry.TenantId.ShouldBe("tenant-a");
        entry.Before.ShouldBeNull();
        entry.After.ShouldNotBeNull();
    }

    [Fact]
    public async Task Existing_skill_is_written_as_update_and_carries_the_previous_state()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(Skill("tenant-a", "invoicing") with { Instructions = "first" });
        await store.SaveAsync(Skill("tenant-a", "invoicing") with { Instructions = "second" });

        var update = (await log.QueryAsync(new AuditQuery { TenantId = "tenant-a", Action = "skill.update" })).ShouldHaveSingleItem();
        update.Before.ShouldNotBeNull();
        update.Before!.ShouldContain("first");
        update.After.ShouldNotBeNull();
        update.After!.ShouldContain("second");
    }

    [Fact]
    public async Task Delete_is_written_only_when_it_succeeds()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        (await store.DeleteAsync("tenant-a", "no-such-skill")).ShouldBeFalse();
        (await log.QueryAsync(new AuditQuery { TenantId = "tenant-a" })).ShouldBeEmpty();

        await store.SaveAsync(Skill("tenant-a", "invoicing"));
        (await store.DeleteAsync("tenant-a", "invoicing")).ShouldBeTrue();

        var entry = (await log.QueryAsync(new AuditQuery { TenantId = "tenant-a", Action = "skill.delete" })).ShouldHaveSingleItem();
        entry.Entity.ShouldBe("skill:invoicing");
        entry.After.ShouldBeNull();
        entry.Before.ShouldNotBeNull();
    }

    /// <summary>
    /// The trail is filed under the SKILL's tenant, not the ambient one.
    /// </summary>
    /// <remarks>
    /// Every <see cref="IAgentSkillStore"/> member takes <c>tenantId</c>
    /// explicitly and an implementation never reads <c>ITenantContext</c> —
    /// unlike its neighbor <see cref="IAgentDefinitionStore"/>. A decorator
    /// that read the ambient tenant would file a background write under the
    /// wrong tenant's trail, which is exactly the kind of cross-tenant record
    /// an audit trail exists to prevent.
    /// </remarks>
    [Fact]
    public async Task The_trail_is_filed_under_the_skills_own_tenant()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(Skill("tenant-b", "invoicing"));

        // Asking tenant-a's trail for it must come back EMPTY: filing the row
        // under the ambient tenant instead of the skill's own would put one
        // tenant's change in another tenant's trail.
        (await log.QueryAsync(new AuditQuery { TenantId = "tenant-a" })).ShouldBeEmpty();

        var entry = (await log.QueryAsync(new AuditQuery { TenantId = "tenant-b" })).ShouldHaveSingleItem();
        entry.TenantId.ShouldBe("tenant-b");
    }

    [Fact]
    public async Task Operation_still_completes_when_the_ledger_fails()
    {
        var store = CreateStore(new ThrowingAuditLog());

        // Observability must not break functionality: AuditRecorder swallows
        // and logs the failure, unlike the script GRANT store, where an
        // unrecorded permission to run code is not acceptable.
        var saved = await store.SaveAsync(Skill("tenant-a", "invoicing"));

        saved.Name.ShouldBe("invoicing");
    }

    private static AgentSkillDefinition Skill(string tenantId, string name)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = "Analyzes invoices.",
            Instructions = "Review invoices carefully.",
        };

    private static AuditingAgentSkillStore CreateStore(IAuditLog auditLog)
        => new(
            new InMemoryAgentSkillStore(),
            auditLog,
            new NullAuditActorResolver(),
            NullLogger<AuditingAgentSkillStore>.Instance);

    private sealed class NullAuditActorResolver : IAuditActorResolver
    {
        public string? Resolve() => null;
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("store is unreachable");

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by this test");
    }
}
