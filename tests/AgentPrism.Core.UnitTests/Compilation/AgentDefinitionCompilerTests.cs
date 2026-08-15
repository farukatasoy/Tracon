using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class AgentDefinitionCompilerTests
{
    [Fact]
    public void Harness_ayari_yoksa_sade_sohbet_agenti_uretilir()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition());

        agent.ShouldBeOfType<ChatClientAgent>();
        agent.Name.ShouldBe("test-agent");
    }

    [Fact]
    public void Harness_ayari_varsa_harness_agenti_uretilir()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition(harness: new HarnessSettings
        {
            MaxContextWindowTokens = 4_096,
            DisableWebSearch = true,
        }));

        agent.ShouldBeOfType<HarnessAgent>();
    }

    [Fact]
    public void Model_ayarlari_sohbet_seceneklerine_aktarilir()
    {
        var client = new FakeChatClient();
        var provider = new FakeModelProvider(client);
        var compiler = new AgentDefinitionCompiler(TestData.Providers(provider), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = new ModelBinding
            {
                Provider = "fake",
                Model = "fake-model",
                Temperature = 0.3f,
                TopP = 0.9f,
                MaxOutputTokens = 512,
            },
        };

        compiler.Compile(definition);

        provider.LastBinding.ShouldNotBeNull();
        provider.LastBinding!.Temperature.ShouldBe(0.3f);
        provider.LastBinding.TopP.ShouldBe(0.9f);
        provider.LastBinding.MaxOutputTokens.ShouldBe(512);
    }

    [Fact]
    public void Kayitli_tool_adlari_cozulur()
    {
        var registry = TestData.Registry(TestData.Tool("get_order"), TestData.Tool("search"));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), registry);

        var agent = compiler.Compile(TestData.Definition(toolNames: ["get_order", "search"]));

        agent.ShouldNotBeNull();
    }

    [Fact]
    public void Bilinmeyen_tool_adi_derlemeyi_durdurur()
    {
        var registry = TestData.Registry(TestData.Tool("get_order"));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), registry);

        var exception = Should.Throw<AgentPrismCompilationException>(
            () => compiler.Compile(TestData.Definition(toolNames: ["get_order", "silinmis_tool"])));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("silinmis_tool");
        // Hata mesaji kullaniciya ne yapacagini soylemeli.
        exception.Message.ShouldContain("get_order");
        exception.Message.ShouldContain("AddTool");
    }

    [Fact]
    public void Bilinmeyen_saglayici_derlemeyi_durdurur()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(), TestData.Registry());

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "yok-boyle") };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("yok-boyle");
        exception.Message.ShouldContain("UseOpenAI");
    }

    [Fact]
    public void Akil_yurutme_cabasi_sohbet_seceneklerine_aktarilir()
    {
        var client = new FakeChatClient();
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider(client)),
            TestData.Registry());

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with { ReasoningEffort = "high" },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        options.ShouldNotBeNull();
        options!.ChatOptions!.Reasoning!.Effort.ShouldBe(ReasoningEffort.High);
    }

    [Fact]
    public void Akil_yurutme_cabasi_verilmezse_ayar_bos_kalir()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition());
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Reasoning.ShouldBeNull();
    }

    [Fact]
    public void Gecersiz_akil_yurutme_cabasi_derlemeyi_durdurur()
    {
        // Sessizce yok saymak yanlis olurdu: bu ayar hem maliyeti hem gecikmeyi
        // degistirir; yanlis yazilmis bir deger fark edilmeden calisirsa kullanici
        // bekledigi davranisi alamaz ve sebebini goremez.
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with { ReasoningEffort = "cok-yuksek" },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("cok-yuksek");
        exception.Message.ShouldContain(nameof(ReasoningEffort.Medium));
    }

    [Fact]
    public void Null_tanim_reddedilir()
    {
        var compiler = CreateCompiler();

        Should.Throw<ArgumentNullException>(() => compiler.Compile(null!));
    }

    [Fact]
    public void Sikistirma_ayari_yoksa_baglam_saglayicisi_eklenmez()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition());
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders.ShouldBeNull();
    }

    [Theory]
    [InlineData(CompactionStrategyKind.SlidingWindow)]
    [InlineData(CompactionStrategyKind.Truncation)]
    [InlineData(CompactionStrategyKind.ToolResult)]
    [InlineData(CompactionStrategyKind.Summarization)]
    [InlineData(CompactionStrategyKind.Pipeline)]
    public void Tetikleyicisiz_strateji_derlemeyi_durdurur(CompactionStrategyKind strategy)
    {
        // Sessizce yok saymak yanlis olurdu: tetikleyicisiz bir strateji hicbir
        // zaman calismaz ve kullanici sebebini goremez (K-034 deseniyle ayni).
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = strategy },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain(strategy.ToString());
    }

    [Fact]
    public void ContextWindow_max_pencere_olmadan_derlemeyi_durdurur()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.ContextWindow },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain(nameof(CompactionSettings.MaxContextWindowTokens));
    }

    [Fact]
    public void ContextWindow_stratejisi_tetikleyici_gerektirmez()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.ContextWindow,
                MaxContextWindowTokens = 8_000,
            },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders.ShouldNotBeNull();
        options.AIContextProviders!.Count().ShouldBe(1);
    }

    [Theory]
    [InlineData(CompactionStrategyKind.SlidingWindow)]
    [InlineData(CompactionStrategyKind.Truncation)]
    [InlineData(CompactionStrategyKind.ToolResult)]
    [InlineData(CompactionStrategyKind.Summarization)]
    [InlineData(CompactionStrategyKind.Pipeline)]
    public void Gecerli_tetikleyiciyle_her_strateji_kuruluyor(CompactionStrategyKind strategy)
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = strategy, TriggerMessages = 20 },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders!.Count().ShouldBe(1);
    }

    [Fact]
    public void Pipeline_sabit_sirada_uc_strateji_icerir()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.Pipeline, TriggerMessages = 20 },
        };

#pragma warning disable MAAI001 // Microsoft.Agents.AI.Compaction.* "evaluation purposes only" — yapisal dogrulama icin.
        var strategy = compiler.BuildCompactionStrategy(definition);
        var pipeline = strategy!.Inner.ShouldBeOfType<PipelineCompactionStrategy>();

        pipeline.Strategies[0].ShouldBeOfType<ToolResultCompactionStrategy>();
        pipeline.Strategies[1].ShouldBeOfType<SlidingWindowCompactionStrategy>();
        pipeline.Strategies[2].ShouldBeOfType<SummarizationCompactionStrategy>();
#pragma warning restore MAAI001
    }

    [Fact]
    public void Ozetleme_modeli_agent_ayarindan_cozulur()
    {
        var mainClient = new FakeChatClient();
        var agentModelProvider = new FakeModelProvider(mainClient, name: "fake");
        var summarizerClient = new FakeChatClient();
        var summarizerProvider = new FakeModelProvider(summarizerClient, name: "agent-secti");

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(agentModelProvider, summarizerProvider),
            TestData.Registry());

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.Summarization,
                TriggerMessages = 20,
                SummarizationModel = new ModelBinding { Provider = "agent-secti", Model = "m" },
            },
        };

        compiler.Compile(definition);

        summarizerProvider.LastBinding.ShouldNotBeNull();
        agentModelProvider.LastBinding!.Provider.ShouldBe("fake");
    }

    [Fact]
    public void Ozetleme_modeli_agent_ayari_yoksa_yardimci_modele_duser()
    {
        var mainClient = new FakeChatClient();
        var agentModelProvider = new FakeModelProvider(mainClient, name: "fake");
        var utilityClient = new FakeChatClient();
        var utilityProvider = new FakeModelProvider(utilityClient, name: "yardimci");

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(agentModelProvider, utilityProvider),
            TestData.Registry(),
            utilityModel: new ModelBinding { Provider = "yardimci", Model = "m" });

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.Summarization, TriggerMessages = 20 },
        };

        compiler.Compile(definition);

        utilityProvider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void Ozetleme_modeli_hicbiri_yoksa_agentin_kendi_modeline_duser()
    {
        var mainClient = new FakeChatClient();
        var agentModelProvider = new FakeModelProvider(mainClient, name: "fake");

        var compiler = new AgentDefinitionCompiler(TestData.Providers(agentModelProvider), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.Summarization, TriggerMessages = 20 },
        };

        compiler.Compile(definition);

        // Ozetleme cagrisi da agent'in kendi saglayicisini kullandi (ayrica cozulen bir baglanti yok).
        agentModelProvider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void Dosya_bellegi_istenip_depo_kayitli_degilse_derlemeyi_durdurur()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Memory = new MemorySettings { EnableFileMemory = true },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
    }

    [Fact]
    public void Todo_ve_metin_aramasi_dosya_deposu_kayitliyken_kuruluyor()
    {
#pragma warning disable MAAI001 // InMemoryAgentFileStore "evaluation purposes only" — yalniz test kurulumu icin.
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            fileStore: new Microsoft.Agents.AI.InMemoryAgentFileStore());
#pragma warning restore MAAI001

        var definition = TestData.Definition() with
        {
            TenantId = "default",
            Memory = new MemorySettings { EnableFileMemory = true, EnableTodo = true, EnableTextSearch = true },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders!.Count().ShouldBe(3);
    }

    [Fact]
    public void Mcp_kaynaklari_istenip_fabrika_kayitli_degilse_derlemeyi_durdurur()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition(mcpResourceUris: ["github:https://example.com/readme"]) with
        {
            TenantId = "default",
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("UseMcp");
    }

    [Fact]
    public void Mcp_kaynaklari_fabrika_kayitliyken_baglama_ekleniyor()
    {
        var factory = new FakeMcpResourceContextProviderFactory();
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            mcpResources: factory);

        var definition = TestData.Definition(mcpResourceUris: ["github:https://example.com/readme"]) with
        {
            TenantId = "acme",
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders!.Count().ShouldBe(1);
        factory.LastResourceReferences.ShouldBe(["github:https://example.com/readme"]);
        factory.LastTenantId.ShouldBe("acme");
    }

    [Fact]
    public void Anlamsal_arama_istenip_depo_kayitli_degilse_derlemeyi_durdurur()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Memory = new MemorySettings { EnableVectorSearch = true },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("IVectorSearchStore");
    }

    [Fact]
    public void Anlamsal_arama_istenip_gomu_ureticisi_kayitli_degilse_derlemeyi_durdurur()
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            vectorSearchStore: new FakeVectorSearchStore());

        var definition = TestData.Definition() with
        {
            Memory = new MemorySettings { EnableVectorSearch = true },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("IEmbeddingGenerator");
    }

    [Fact]
    public void Anlamsal_arama_ikisi_de_kayitliyken_tool_baglanir()
    {
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            tenantContext: FixedTenantContext.Default,
            vectorSearchStore: new FakeVectorSearchStore(),
            embeddingGenerator: new FakeEmbeddingGenerator());

        var definition = TestData.Definition() with
        {
            Memory = new MemorySettings { EnableVectorSearch = true },
        };

        var agent = compiler.Compile(definition);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Tools.ShouldNotBeNull();
        options.ChatOptions.Tools!.ShouldContain(static tool => tool.Name == "search_knowledge");
    }

    [Fact]
    public void Anlamsal_arama_kiraci_cozulemezse_derlemeyi_durdurur()
    {
        // tenantContext verilmiyor VE definition.TenantId bos — kiraci hicbir
        // kaynaktan cozulemez.
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            vectorSearchStore: new FakeVectorSearchStore(),
            embeddingGenerator: new FakeEmbeddingGenerator());

        var definition = TestData.Definition() with
        {
            Memory = new MemorySettings { EnableVectorSearch = true },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("tenant");
    }

    private static AgentDefinitionCompiler CreateCompiler()
        => new(TestData.Providers(new FakeModelProvider()), TestData.Registry());
}
