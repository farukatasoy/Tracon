using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Storage;

public sealed class InMemoryAgentDefinitionStoreTests
{
    [Fact]
    public async Task Kayit_surumu_artirir_ve_gecmisi_saklar()
    {
        var store = new InMemoryAgentDefinitionStore();

        var first = await store.SaveAsync(TestData.Definition("a") with { Instructions = "birinci" });
        var second = await store.SaveAsync(TestData.Definition("a") with { Instructions = "ikinci" });

        first.Version.ShouldBe(1);
        second.Version.ShouldBe(2);

        (await store.GetAsync("a"))!.Instructions.ShouldBe("ikinci");
        (await store.ListVersionsAsync("a")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Kayit_kaynagi_veritabani_olarak_isaretlenir()
    {
        var store = new InMemoryAgentDefinitionStore();

        var saved = await store.SaveAsync(TestData.Definition("a") with { Origin = AgentDefinitionOrigin.Code });

        saved.Origin.ShouldBe(AgentDefinitionOrigin.Database);
    }

    [Fact]
    public async Task Surum_gecmisi_yeniden_eskiye_siralanir()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition("a"));
        await store.SaveAsync(TestData.Definition("a"));
        await store.SaveAsync(TestData.Definition("a"));

        var versions = await store.ListVersionsAsync("a");

        versions.Select(static v => v.Version).ShouldBe([3, 2, 1]);
    }

    [Fact]
    public async Task Geri_alma_eski_surumu_yeni_surum_olarak_kaydeder()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition("a") with { Instructions = "birinci" });
        await store.SaveAsync(TestData.Definition("a") with { Instructions = "ikinci" });

        var restored = await store.RollbackAsync("a", version: 1);

        restored.Version.ShouldBe(3);
        restored.Instructions.ShouldBe("birinci");

        // Geri alma gecmisi silmez.
        (await store.ListVersionsAsync("a")).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Olmayan_surume_geri_alma_hata_verir()
    {
        var store = new InMemoryAgentDefinitionStore();
        await store.SaveAsync(TestData.Definition("a"));

        await Should.ThrowAsync<AgentPrismException>(async () => await store.RollbackAsync("a", version: 99));
    }

    [Fact]
    public async Task Silme_tum_surumleri_kaldirir()
    {
        var store = new InMemoryAgentDefinitionStore();
        await store.SaveAsync(TestData.Definition("a"));
        await store.SaveAsync(TestData.Definition("a"));

        (await store.DeleteAsync("a")).ShouldBeTrue();
        (await store.GetAsync("a")).ShouldBeNull();
        (await store.DeleteAsync("a")).ShouldBeFalse();
    }
}

public sealed class InMemoryRunStoreTests
{
    [Fact]
    public async Task Olaylar_sira_numarasina_gore_okunur()
    {
        var store = new InMemoryRunStore();
        var runId = AgentPrismId.NewId();

        await store.StartRunAsync(NewRun(runId));

        for (var i = 0; i < 5; i++)
        {
            await store.AppendEventAsync(NewEvent(runId, i));
        }

        var sequences = new List<long>();
        await foreach (var runEvent in store.ReadEventsAsync(runId, fromSequence: 2))
        {
            sequences.Add(runEvent.Sequence);
        }

        sequences.ShouldBe([2, 3, 4]);
    }

    [Fact]
    public async Task Olmayan_calistirmaya_olay_eklenemez()
    {
        var store = new InMemoryRunStore();

        await Should.ThrowAsync<AgentPrismException>(
            async () => await store.AppendEventAsync(NewEvent(AgentPrismId.NewId(), 0)));
    }

    [Fact]
    public async Task Sorgu_agent_adina_gore_filtreler()
    {
        var store = new InMemoryRunStore();

        await store.StartRunAsync(NewRun(AgentPrismId.NewId(), "alpha"));
        await store.StartRunAsync(NewRun(AgentPrismId.NewId(), "beta"));
        await store.StartRunAsync(NewRun(AgentPrismId.NewId(), "alpha"));

        var results = await store.QueryRunsAsync(new RunQuery { AgentName = "alpha" });

        results.Count.ShouldBe(2);
        results.ShouldAllBe(static run => run.AgentName == "alpha");
    }

    [Fact]
    public async Task Sorgu_en_yeniden_eskiye_siralar()
    {
        var store = new InMemoryRunStore();
        var now = DateTimeOffset.UtcNow;

        await store.StartRunAsync(NewRun(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-10) });
        await store.StartRunAsync(NewRun(AgentPrismId.NewId()) with { StartedAt = now });
        await store.StartRunAsync(NewRun(AgentPrismId.NewId()) with { StartedAt = now.AddMinutes(-5) });

        var results = await store.QueryRunsAsync(new RunQuery());

        results[0].StartedAt.ShouldBe(now);
        results[^1].StartedAt.ShouldBe(now.AddMinutes(-10));
    }

    [Fact]
    public async Task Ust_sinir_asilinca_en_eski_calistirma_dusurulur()
    {
        var store = new InMemoryRunStore { MaxRuns = 3 };
        var ids = new List<Guid>();

        for (var i = 0; i < 5; i++)
        {
            var id = AgentPrismId.NewId();
            ids.Add(id);
            await store.StartRunAsync(NewRun(id));
        }

        (await store.GetRunAsync(ids[0])).ShouldBeNull();
        (await store.GetRunAsync(ids[4])).ShouldNotBeNull();
    }

    private static RunStartInfo NewRun(Guid id, string agentName = "test-agent")
        => new() { RunId = id, AgentName = agentName, StartedAt = DateTimeOffset.UtcNow };

    private static RunEvent NewEvent(Guid runId, long sequence)
        => new()
        {
            RunId = runId,
            Sequence = sequence,
            Type = RunEventType.MessageDelta,
            Timestamp = DateTimeOffset.UtcNow,
        };
}

public sealed class AgentPrismIdTests
{
    [Fact]
    public void Uretilen_kimlik_surum_7_dir()
    {
        var id = AgentPrismId.NewId();

        // 7. baytin ust 4 biti surum numarasini tasir (big-endian gosterimde).
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _).ShouldBeTrue();

        (bytes[6] >> 4).ShouldBe(7);
        (bytes[8] >> 6).ShouldBe(2); // RFC 9562 varyanti: ikili 10
    }

    [Fact]
    public void Kimlikler_zaman_siralidir()
    {
        var baseTime = DateTimeOffset.UtcNow;

        var earlier = AgentPrismId.NewId(baseTime);
        var later = AgentPrismId.NewId(baseTime.AddSeconds(1));

        // UUIDv7 metin gosterimi zaman sirasini korur.
        string.CompareOrdinal(earlier.ToString(), later.ToString()).ShouldBeLessThan(0);
    }

    [Fact]
    public void Zaman_damgasi_geri_okunabilir()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var id = AgentPrismId.NewId(timestamp);

        var recovered = AgentPrismId.GetTimestamp(id);

        // Milisaniye cozunurlugu; alt birimler kaybolur.
        recovered.ToUnixTimeMilliseconds().ShouldBe(timestamp.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void Surum_7_olmayan_kimlik_reddedilir()
    {
        Should.Throw<ArgumentException>(() => AgentPrismId.GetTimestamp(Guid.Empty));
    }
}
