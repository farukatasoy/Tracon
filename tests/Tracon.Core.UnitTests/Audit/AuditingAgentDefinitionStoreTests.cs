using Tracon.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// <see cref="AuditingAgentDefinitionStore"/> produces an audit trail on its
/// write paths and does not interrupt the operation when the ledger fails.
/// </summary>
public sealed class AuditingAgentDefinitionStoreTests
{
    [Fact]
    public async Task New_definition_is_written_as_create()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        var saved = await store.SaveAsync(TestData.Definition("support"));

        var entry = (await log.QueryAsync(new AuditQuery())).ShouldHaveSingleItem();
        entry.Action.ShouldBe("agent.create");
        entry.Entity.ShouldBe("agent:support");
        entry.Before.ShouldBeNull();
        entry.After.ShouldNotBeNull();
        saved.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Existing_definition_is_written_as_update_and_carries_the_previous_state()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(TestData.Definition("support") with { Description = "first" });
        await store.SaveAsync(TestData.Definition("support") with { Description = "second" });

        var entries = await log.QueryAsync(new AuditQuery());
        entries.Count.ShouldBe(2);

        var update = entries.Single(static e => string.Equals(e.Action, "agent.update", StringComparison.Ordinal));
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

        (await store.DeleteAsync("no-such-agent")).ShouldBeFalse();
        (await log.QueryAsync(new AuditQuery())).ShouldBeEmpty();

        await store.SaveAsync(TestData.Definition("support"));
        (await store.DeleteAsync("support")).ShouldBeTrue();

        var entry = (await log.QueryAsync(new AuditQuery { Action = "agent.delete" })).ShouldHaveSingleItem();
        entry.Entity.ShouldBe("agent:support");
        entry.After.ShouldBeNull();
    }

    [Fact]
    public async Task Rollback_carries_the_version_numbers()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(TestData.Definition("support") with { Description = "v1" });
        await store.SaveAsync(TestData.Definition("support") with { Description = "v2" });

        var rolledBack = await store.RollbackAsync("support", 1);

        var entry = (await log.QueryAsync(new AuditQuery { Action = "agent.rollback" })).ShouldHaveSingleItem();
        entry.Before.ShouldBe("""{"version":2}""");
        entry.After.ShouldBe($$"""{"rolledBackToVersion":1,"newVersion":{{rolledBack.Version}}}""");
    }

    [Fact]
    public async Task Operation_still_completes_when_the_ledger_fails()
    {
        var store = CreateStore(new ThrowingAuditLog());

        // AuditRecorder swallows and logs the failure; SaveAsync itself must not throw.
        var saved = await store.SaveAsync(TestData.Definition("support"));

        saved.Name.ShouldBe("support");
    }

    private static AuditingAgentDefinitionStore CreateStore(IAuditLog auditLog)
        => new(
            new InMemoryAgentDefinitionStore(),
            auditLog,
            new SingleTenantContext(Options.Create(new TraconOptions())),
            new NullAuditActorResolver(),
            NullLogger<AuditingAgentDefinitionStore>.Instance);

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
