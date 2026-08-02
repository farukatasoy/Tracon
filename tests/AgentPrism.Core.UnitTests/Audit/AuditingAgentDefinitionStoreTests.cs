using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Audit;

/// <summary>
/// <see cref="AuditingAgentDefinitionStore"/>'un yazma yollarinda denetim izi
/// uretmesi ve defter hatasinda islemi kesmemesi.
/// </summary>
public sealed class AuditingAgentDefinitionStoreTests
{
    [Fact]
    public async Task Yeni_tanim_create_olarak_yazilir()
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
    public async Task Var_olan_tanim_update_olarak_yazilir_ve_onceki_hali_tasir()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        await store.SaveAsync(TestData.Definition("support") with { Description = "ilk" });
        await store.SaveAsync(TestData.Definition("support") with { Description = "ikinci" });

        var entries = await log.QueryAsync(new AuditQuery());
        entries.Count.ShouldBe(2);

        var update = entries.Single(static e => string.Equals(e.Action, "agent.update", StringComparison.Ordinal));
        update.Before.ShouldNotBeNull();
        update.Before!.ShouldContain("ilk");
        update.After.ShouldNotBeNull();
        update.After!.ShouldContain("ikinci");
    }

    [Fact]
    public async Task Silme_yalnizca_basariliysa_yazilir()
    {
        var log = new InMemoryAuditLog();
        var store = CreateStore(log);

        (await store.DeleteAsync("yok-boyle")).ShouldBeFalse();
        (await log.QueryAsync(new AuditQuery())).ShouldBeEmpty();

        await store.SaveAsync(TestData.Definition("support"));
        (await store.DeleteAsync("support")).ShouldBeTrue();

        var entry = (await log.QueryAsync(new AuditQuery { Action = "agent.delete" })).ShouldHaveSingleItem();
        entry.Entity.ShouldBe("agent:support");
        entry.After.ShouldBeNull();
    }

    [Fact]
    public async Task Geri_alma_surum_numaralarini_tasir()
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
    public async Task Defter_hata_verirse_islem_yine_de_tamamlanir()
    {
        var store = CreateStore(new ThrowingAuditLog());

        // AuditRecorder hatayi yutar ve loglar; SaveAsync'in kendisi patlamamalidir.
        var saved = await store.SaveAsync(TestData.Definition("support"));

        saved.Name.ShouldBe("support");
    }

    private static AuditingAgentDefinitionStore CreateStore(IAuditLog auditLog)
        => new(
            new InMemoryAgentDefinitionStore(),
            auditLog,
            new SingleTenantContext(Options.Create(new AgentPrismOptions())),
            new NullAuditActorResolver(),
            NullLogger<AuditingAgentDefinitionStore>.Instance);

    private sealed class NullAuditActorResolver : IAuditActorResolver
    {
        public string? Resolve() => null;
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("depo erisilemez");

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());
    }
}
