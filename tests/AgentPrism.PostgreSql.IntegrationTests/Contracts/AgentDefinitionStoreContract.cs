using System.Text.Json;
using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// <see cref="IAgentDefinitionStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bu testler <strong>her uygulama icin</strong> calistirilir. Bellek ici depo ile
/// PostgreSQL deposu arasindaki davranis farki hatadir; bu sinif o farki yakalar.
/// </remarks>
public abstract class AgentDefinitionStoreContract : IAsyncLifetime
{
    /// <summary>Test edilen depo.</summary>
    protected IAgentDefinitionStore Store { get; private set; } = null!;

    /// <summary>Test icin bos bir depo uretir.</summary>
    /// <returns>Kullanima hazir depo.</returns>
    protected abstract ValueTask<IAgentDefinitionStore> CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask InitializeAsync() => Store = await CreateStoreAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await OnDisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>Turetilmis sinifin kendi kaynaklarini birakmasi icin kanca.</summary>
    /// <returns>Tamamlanma gorevi.</returns>
    protected virtual ValueTask OnDisposeAsync() => default;

    [Fact]
    public async Task Kayit_surumu_artirir_ve_gecmisi_saklar()
    {
        var first = await Store.SaveAsync(TestData.Definition("a") with { Instructions = "birinci" });
        var second = await Store.SaveAsync(TestData.Definition("a") with { Instructions = "ikinci" });

        first.Version.ShouldBe(1);
        second.Version.ShouldBe(2);

        (await Store.GetAsync("a"))!.Instructions.ShouldBe("ikinci");
        (await Store.ListVersionsAsync("a")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Kayit_kaynagi_veritabani_olarak_isaretlenir()
    {
        var saved = await Store.SaveAsync(TestData.Definition("a") with { Origin = AgentDefinitionOrigin.Code });

        saved.Origin.ShouldBe(AgentDefinitionOrigin.Database);
    }

    [Fact]
    public async Task Surum_gecmisi_yeniden_eskiye_siralanir()
    {
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));

        var versions = await Store.ListVersionsAsync("a");

        versions.Select(static v => v.Version).ShouldBe([3, 2, 1]);
    }

    [Fact]
    public async Task Geri_alma_eski_surumu_yeni_surum_olarak_kaydeder()
    {
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "birinci" });
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "ikinci" });

        var restored = await Store.RollbackAsync("a", version: 1);

        restored.Version.ShouldBe(3);
        restored.Instructions.ShouldBe("birinci");

        // Geri alma gecmisi silmez.
        (await Store.ListVersionsAsync("a")).Count.ShouldBe(3);
        (await Store.GetAsync("a"))!.Instructions.ShouldBe("birinci");
    }

    [Fact]
    public async Task Olmayan_surume_geri_alma_hata_verir()
    {
        await Store.SaveAsync(TestData.Definition("a"));

        await Should.ThrowAsync<AgentPrismException>(async () => await Store.RollbackAsync("a", version: 99));
    }

    [Fact]
    public async Task Olmayan_agenta_geri_alma_hata_verir()
        => await Should.ThrowAsync<AgentPrismException>(async () => await Store.RollbackAsync("yok", version: 1));

    [Fact]
    public async Task Silme_tum_surumleri_kaldirir()
    {
        await Store.SaveAsync(TestData.Definition("a"));
        await Store.SaveAsync(TestData.Definition("a"));

        (await Store.DeleteAsync("a")).ShouldBeTrue();
        (await Store.GetAsync("a")).ShouldBeNull();
        (await Store.ListVersionsAsync("a")).ShouldBeEmpty();
        (await Store.DeleteAsync("a")).ShouldBeFalse();
    }

    [Fact]
    public async Task Olmayan_tanim_null_doner()
        => (await Store.GetAsync("yok")).ShouldBeNull();

    [Fact]
    public async Task Belirli_surum_getirilebilir()
    {
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "birinci" });
        await Store.SaveAsync(TestData.Definition("a") with { Instructions = "ikinci" });

        var first = await Store.GetVersionAsync("a", 1);
        var second = await Store.GetVersionAsync("a", 2);

        first!.Instructions.ShouldBe("birinci");
        second!.Instructions.ShouldBe("ikinci");
    }

    [Fact]
    public async Task Olmayan_surum_null_doner()
    {
        await Store.SaveAsync(TestData.Definition("a"));

        (await Store.GetVersionAsync("a", 99)).ShouldBeNull();
    }

    [Fact]
    public async Task Olmayan_agentin_surumu_null_doner()
        => (await Store.GetVersionAsync("yok", 1)).ShouldBeNull();

    [Fact]
    public async Task Listeleme_ada_gore_siralar()
    {
        await Store.SaveAsync(TestData.Definition("gamma"));
        await Store.SaveAsync(TestData.Definition("alpha"));
        await Store.SaveAsync(TestData.Definition("beta"));

        var all = await Store.ListAsync();

        all.Select(static definition => definition.Name).ShouldBe(["alpha", "beta", "gamma"]);
    }

    [Fact]
    public async Task Tanimin_tum_alanlari_gidip_gelir()
    {
        var original = TestData.Definition("full") with
        {
            CallableAgentNames = ["arastirmaci"],
            Harness = new HarnessSettings { MaxContextWindowTokens = 4096, DisableWebSearch = true },
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.Summarization,
                TriggerTokens = 8_000,
                MinimumPreservedGroups = 4,
                SummarizationPrompt = "kisa ve oz ozetle",
                SummarizationModel = new ModelBinding { Provider = "echo", Model = "echo-summarizer" },
            },
            Memory = new MemorySettings { EnableFileMemory = true, EnableTodo = true, EnableTextSearch = true },
            Metadata = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["owner"] = TestData.State("\"platform-ekibi\""),
                ["priority"] = TestData.State("3"),
            },
        };

        await Store.SaveAsync(original);
        var loaded = await Store.GetAsync("full");

        loaded.ShouldNotBeNull();
        loaded.DisplayName.ShouldBe(original.DisplayName);
        loaded.Description.ShouldBe(original.Description);
        loaded.Instructions.ShouldBe(original.Instructions);
        loaded.Model.Provider.ShouldBe("echo");
        loaded.Model.Model.ShouldBe("echo-1");
        loaded.Model.Temperature.ShouldBe(0.5f);
        loaded.ToolNames.ShouldBe(["alpha", "beta"]);
        loaded.CallableAgentNames.ShouldBe(["arastirmaci"]);
        loaded.Harness.ShouldNotBeNull();
        loaded.Harness.MaxContextWindowTokens.ShouldBe(4096);
        loaded.Harness.DisableWebSearch.ShouldBeTrue();
        loaded.Compaction.ShouldNotBeNull();
        loaded.Compaction.Strategy.ShouldBe(CompactionStrategyKind.Summarization);
        loaded.Compaction.TriggerTokens.ShouldBe(8_000);
        loaded.Compaction.MinimumPreservedGroups.ShouldBe(4);
        loaded.Compaction.SummarizationPrompt.ShouldBe("kisa ve oz ozetle");
        loaded.Compaction.SummarizationModel.ShouldNotBeNull();
        loaded.Compaction.SummarizationModel!.Provider.ShouldBe("echo");
        loaded.Compaction.SummarizationModel.Model.ShouldBe("echo-summarizer");
        loaded.Memory.ShouldNotBeNull();
        loaded.Memory.EnableFileMemory.ShouldBeTrue();
        loaded.Memory.EnableTodo.ShouldBeTrue();
        loaded.Memory.EnableTextSearch.ShouldBeTrue();
        loaded.Metadata["owner"].GetString().ShouldBe("platform-ekibi");
        loaded.Metadata["priority"].GetInt32().ShouldBe(3);
    }
}
