using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// <see cref="AuditingWorkflowDefinitionStore"/> writes a save, a rollback and a delete to the
/// trail, and only a write that happened.
/// </summary>
public sealed class AuditingWorkflowDefinitionStoreTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task A_rollback_is_written_as_workflow_rollback_with_the_versions_it_names()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(Tenant, Definition("first"));
        await store.SaveAsync(Tenant, Definition("second"));

        var rolledBack = await store.RollbackAsync(Tenant, "review", 1);

        var entry = (await log.QueryAsync(new AuditQuery { TenantId = Tenant, Action = "workflow.rollback" })).ShouldHaveSingleItem();

        entry.Entity.ShouldBe("workflow:review");
        entry.Before.ShouldBe("""{"version":2}""");
        entry.After.ShouldBe($$"""{"rolledBackToVersion":1,"newVersion":{{rolledBack.Version}}}""");
    }

    [Fact]
    public async Task A_rollback_is_not_written_as_a_save()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(Tenant, Definition("first"));
        await store.SaveAsync(Tenant, Definition("second"));
        await store.RollbackAsync(Tenant, "review", 1);

        (await log.QueryAsync(new AuditQuery { TenantId = Tenant, Action = "workflow.save" })).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_rollback_that_throws_writes_nothing()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(Tenant, Definition("first"));

        await Should.ThrowAsync<TraconException>(async () => await store.RollbackAsync(Tenant, "review", 9));

        (await log.QueryAsync(new AuditQuery { TenantId = Tenant, Action = "workflow.rollback" })).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_version_reads_write_nothing()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(Tenant, Definition("first"));

        (await store.GetVersionAsync(Tenant, "review", 1)).ShouldNotBeNull();
        (await store.ListVersionsAsync(Tenant, "review")).Count.ShouldBe(1);

        (await log.QueryAsync(new AuditQuery { TenantId = Tenant })).Count.ShouldBe(1, "only the save");
    }

    private static WorkflowDefinition Definition(string description)
        => new()
        {
            Name = "review",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer"],
            Description = description,
        };

    private static AuditingWorkflowDefinitionStore CreateStore(IAuditLog auditLog)
        => new(
            new InMemoryWorkflowDefinitionStore(),
            auditLog,
            new NullAuditActorResolver(),
            NullLogger<AuditingWorkflowDefinitionStore>.Instance,
            metrics: null);

    private sealed class NullAuditActorResolver : IAuditActorResolver
    {
        public string? Resolve() => null;
    }
}
