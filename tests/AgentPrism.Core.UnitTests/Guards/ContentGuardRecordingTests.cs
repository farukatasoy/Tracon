using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Guards;

/// <summary>
/// Guard kararlarinin calistirma kaydina ve denetim izine yazilmasini dogrular.
/// </summary>
/// <remarks>
/// Testler gercek yoldan — <see cref="RunRecordingAgent"/> uzerinden — kosar:
/// olay yazicisi calistirma kapsamindan (<see cref="AgentPrismRunContext"/>)
/// okunur ve kapsam yalnizca orada kurulur. Dogrudan kurulan bir boru hatti bu
/// zinciri hic kanitlamazdi.
/// </remarks>
public sealed class ContentGuardRecordingTests
{
    private const string CardNumber = "4539578763621486";

    [Fact]
    public async Task Maskeleme_ContentMasked_olayi_yazar()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Masking(CardNumber, "[redacted]"));

        await agent.RunAsync($"kart numaram {CardNumber}");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        run.Status.ShouldBe(RunStatus.Completed);

        var masked = (await ReadEventsAsync(store, run.Id))
            .Where(static runEvent => runEvent.Type == RunEventType.ContentMasked)
            .ShouldHaveSingleItem();

        masked.Text.ShouldBe("stub/trigger (Input)");
    }

    [Fact]
    public async Task Olay_yuku_maskelenen_icerigi_tasimaz()
    {
        // 🚨 Olay maskelemenin YAPILDIGINI bildirir, ne maskelendigini bildirmez.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Masking(CardNumber, "[redacted]"));

        await agent.RunAsync($"kart numaram {CardNumber}");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        foreach (var runEvent in await ReadEventsAsync(store, run.Id))
        {
            if (runEvent.Type is RunEventType.ContentMasked or RunEventType.ContentBlocked)
            {
                (runEvent.Text ?? string.Empty).ShouldNotContain(CardNumber, Case.Sensitive);
                (runEvent.Payload ?? string.Empty).ShouldNotContain(CardNumber, Case.Sensitive);
            }
        }
    }

    [Fact]
    public async Task Engelleme_calistirmayi_content_blocked_ile_dusurur()
    {
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Blocking("gizli-proje"));

        await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => agent.RunAsync("gizli-proje nedir"));

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull();
        run.Error!.Type.ShouldBe("content_blocked");
        run.Error.Class.ShouldBe(RunErrorClass.ContentBlocked);

        (await ReadEventsAsync(store, run.Id))
            .Where(static runEvent => runEvent.Type == RunEventType.ContentBlocked)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Engelleme_denetim_izine_yazilir()
    {
        var auditLog = new InMemoryAuditLog();
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Blocking("gizli-proje"), auditLog);

        await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => agent.RunAsync("gizli-proje nedir"));

        var entry = (await auditLog.QueryAsync(new AuditQuery { TenantId = "default" })).ShouldHaveSingleItem();

        entry.Action.ShouldBe("content.blocked");
        entry.Entity.ShouldStartWith("run:");
        entry.After.ShouldNotBeNull();
        entry.After!.ShouldContain("\"guard\":\"stub\"", Case.Sensitive);
        entry.After.ShouldContain("\"rule\":\"trigger\"", Case.Sensitive);
    }

    [Fact]
    public async Task Denetim_izi_engellenen_metni_tasimaz()
    {
        // 🚨 Engellenen icerik tanimi geregi hassastir. Denetim izine yazmak
        // sorunu KALICI hale getirir (K-059'un ruhu).
        var auditLog = new InMemoryAuditLog();
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Blocking("gizli-proje"), auditLog);

        await Should.ThrowAsync<AgentPrismContentBlockedException>(
            () => agent.RunAsync("gizli-proje kod adiyla anilan urun"));

        foreach (var entry in await auditLog.QueryAsync(new AuditQuery { TenantId = "default" }))
        {
            (entry.Before ?? string.Empty).ShouldNotContain("gizli-proje", Case.Insensitive);
            (entry.After ?? string.Empty).ShouldNotContain("gizli-proje", Case.Insensitive);
            entry.Entity.ShouldNotContain("gizli-proje", Case.Insensitive);
        }
    }

    [Fact]
    public async Task Maskeleme_denetim_izine_yazilmaz()
    {
        // Maskeleme calistirma olayina yazilir; denetim izi engelleme kararlarina
        // ayrilmistir (calistirma kaydi silinse de izlenebilir kalmalidir).
        var auditLog = new InMemoryAuditLog();
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var agent = Agent(store, new FakeChatClient(), StubContentGuard.Masking(CardNumber, "***"), auditLog);

        await agent.RunAsync($"kart {CardNumber}");

        (await auditLog.QueryAsync(new AuditQuery { TenantId = "default" })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Guard_baglami_calistirma_kimligini_ve_kiraciyi_tasir()
    {
        // Kiraci bazli kural yazilabilmesinin sarti.
        var store = new InMemoryRunStore(tenantContext: FixedTenantContext.Default);
        var guard = StubContentGuard.Blocking("asla-eslesmez");
        var agent = Agent(store, new FakeChatClient(), guard);

        await agent.RunAsync("selam");

        var run = (await store.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();

        guard.LastContext.ShouldNotBeNull();
        guard.LastContext!.RunId.ShouldBe(run.Id);
        guard.LastContext.TenantId.ShouldBe("default");
        guard.LastContext.AgentName.ShouldBe("test-agent");
        guard.LastContext.ModelId.ShouldBe("fake-model");
    }

    private static RunRecordingAgent Agent(
        InMemoryRunStore store,
        FakeChatClient client,
        IContentGuard guard,
        IAuditLog? auditLog = null)
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(
                TestData.ContentGuards(auditLog, guards: guard),
                new FakeModelProvider(client)),
            TestData.Registry());

        return new RunRecordingAgent(
            compiler.Compile(TestData.Definition()),
            store,
            FixedTenantContext.Default,
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            errorClassifier: new DefaultRunErrorClassifier());
    }

    private static async Task<List<RunEvent>> ReadEventsAsync(InMemoryRunStore store, Guid runId)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in store.ReadEventsAsync(runId))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
