using System.Text.Json;

namespace AgentPrism.StoreContracts;

/// <summary>
/// <see cref="IAgentDefinitionStore"/> sozlesmesinin davranis testleri.
/// </summary>
/// <remarks>
/// Bu testler <strong>her uygulama icin</strong> calistirilir. Bellek ici depo ile
/// PostgreSQL deposu arasindaki davranis farki hatadir; bu sinif o farki yakalar.
/// </remarks>
public abstract class AgentDefinitionStoreContract : TenantIsolationContract<IAgentDefinitionStore>
{
    /// <inheritdoc />
    /// <remarks>
    /// Kiraci arayuzde bir parametre degildir; <see cref="ITenantContext"/>'ten
    /// okunur. Bu yuzden her kanca once gecerli kiraciyi ayarlar.
    /// </remarks>
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        AmbientTenant.TenantId = tenantId;
        await Store.SaveAsync(TestData.Definition(name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        var name = (string)key;

        // Uc okuma yolu da ayni yalitimi tasimalidir: tekil okuma, surum
        // okumasi ve surum gecmisi.
        if (await Store.GetAsync(name) is null)
        {
            (await Store.ListVersionsAsync(name)).ShouldBeEmpty();
            (await Store.GetVersionAsync(name, 1)).ShouldBeNull();
            return false;
        }

        (await Store.ListVersionsAsync(name)).ShouldNotBeEmpty();
        return true;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
    {
        AmbientTenant.TenantId = tenantId;
        return (await Store.ListAsync()).Count;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;
        return await Store.DeleteAsync((string)key);
    }

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
            Model = new ModelBinding
            {
                Provider = "echo",
                Model = "echo-1",
                Temperature = 0.5f,

                // Saglayiciya ozgu ayarlar da jsonb icinde tasinir (Faz 26).
                ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["anthropic.promptCaching"] = TestData.State("true"),
                    ["anthropic.thinking.budgetTokens"] = TestData.State("2048"),
                },

                // Yapilandirilmis cikti semasi da jsonb icinde tasinir (Faz 38).
                ResponseFormat = new AgentResponseFormat
                {
                    Kind = AgentResponseFormatKind.JsonSchema,
                    Schema = TestData.State("""{"type":"object","properties":{"total":{"type":"number"}}}"""),
                    SchemaName = "invoice",
                    SchemaDescription = "Bir fatura ozetinin semasi.",
                },
            },
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
        loaded.Model.ProviderSettings.Count.ShouldBe(2);
        loaded.Model.ProviderSettings["anthropic.promptCaching"].GetBoolean().ShouldBeTrue();
        loaded.Model.ProviderSettings["anthropic.thinking.budgetTokens"].GetInt32().ShouldBe(2048);
        loaded.Model.ResponseFormat.ShouldNotBeNull();
        loaded.Model.ResponseFormat!.Kind.ShouldBe(AgentResponseFormatKind.JsonSchema);
        loaded.Model.ResponseFormat.Schema.ShouldNotBeNull();
        loaded.Model.ResponseFormat.Schema!.Value.GetProperty("type").GetString().ShouldBe("object");
        loaded.Model.ResponseFormat.SchemaName.ShouldBe("invoice");
        loaded.Model.ResponseFormat.SchemaDescription.ShouldBe("Bir fatura ozetinin semasi.");
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

    [Fact]
    public async Task Kiraci_digerinin_tanimini_geri_alamaz()
    {
        AmbientTenant.TenantId = TenantA;
        await Store.SaveAsync(TestData.Definition("gizli"));
        await Store.SaveAsync(TestData.Definition("gizli") with { Instructions = "ikinci" });

        AmbientTenant.TenantId = TenantB;
        await Should.ThrowAsync<AgentPrismException>(async () => await Store.RollbackAsync("gizli", 1));

        AmbientTenant.TenantId = TenantA;
        (await Store.RollbackAsync("gizli", 1)).Version.ShouldBe(3);
    }

    [Fact]
    public async Task Her_kiracinin_surum_sayaci_kendine_aittir()
    {
        // IsolationTests.cs'ten tasindi (Faz 41).
        AmbientTenant.TenantId = TenantA;
        await Store.SaveAsync(TestData.Definition("destek") with { Instructions = "a talimati" });

        AmbientTenant.TenantId = TenantB;
        await Store.SaveAsync(TestData.Definition("destek") with { Instructions = "b talimati" });

        AmbientTenant.TenantId = TenantA;
        var first = (await Store.GetAsync("destek")).ShouldNotBeNull();
        first.Instructions.ShouldBe("a talimati");
        first.Version.ShouldBe(1);

        AmbientTenant.TenantId = TenantB;
        var second = (await Store.GetAsync("destek")).ShouldNotBeNull();
        second.Instructions.ShouldBe("b talimati");
        second.Version.ShouldBe(1);
    }
}
