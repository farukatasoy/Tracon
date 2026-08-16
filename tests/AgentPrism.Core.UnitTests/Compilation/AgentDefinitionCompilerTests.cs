using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class AgentDefinitionCompilerTests
{
    [Fact]
    public void Plain_chat_agent_is_produced_when_no_harness_setting_is_present()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition());

        agent.ShouldBeOfType<ChatClientAgent>();
        agent.Name.ShouldBe("test-agent");
    }

    [Fact]
    public void Harness_agent_is_produced_when_a_harness_setting_is_present()
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
    public void Model_settings_are_carried_to_the_chat_options()
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
    public void Registered_tool_names_are_resolved()
    {
        var registry = TestData.Registry(TestData.Tool("get_order"), TestData.Tool("search"));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), registry);

        var agent = compiler.Compile(TestData.Definition(toolNames: ["get_order", "search"]));

        agent.ShouldNotBeNull();
    }

    [Fact]
    public void Unknown_tool_name_stops_compilation()
    {
        var registry = TestData.Registry(TestData.Tool("get_order"));
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), registry);

        var exception = Should.Throw<AgentPrismCompilationException>(
            () => compiler.Compile(TestData.Definition(toolNames: ["get_order", "deleted_tool"])));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("deleted_tool");
        // The error message should tell the user what to do.
        exception.Message.ShouldContain("get_order");
        exception.Message.ShouldContain("AddTool");
    }

    [Fact]
    public void Unknown_provider_stops_compilation()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(), TestData.Registry());

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "no-such-provider") };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("no-such-provider");
        exception.Message.ShouldContain("UseOpenAI");
    }

    [Fact]
    public void Reasoning_effort_is_carried_to_the_chat_options()
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
    public void Reasoning_setting_stays_empty_when_effort_is_not_given()
    {
        var compiler = CreateCompiler();

        var agent = compiler.Compile(TestData.Definition());
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Reasoning.ShouldBeNull();
    }

    [Fact]
    public void Invalid_reasoning_effort_stops_compilation()
    {
        // Silently ignoring this would be wrong: this setting changes both cost
        // and latency; if a misspelled value runs unnoticed, the user does not
        // get the expected behavior and cannot see why.
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Model = TestData.Binding() with { ReasoningEffort = "very-high" },
        };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.AgentName.ShouldBe("test-agent");
        exception.Message.ShouldContain("very-high");
        exception.Message.ShouldContain(nameof(ReasoningEffort.Medium));
    }

    [Fact]
    public void Null_definition_is_rejected()
    {
        var compiler = CreateCompiler();

        Should.Throw<ArgumentNullException>(() => compiler.Compile(null!));
    }

    [Fact]
    public void No_context_provider_is_added_without_a_compaction_setting()
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
    public void Strategy_without_a_trigger_stops_compilation(CompactionStrategyKind strategy)
    {
        // Silently ignoring this would be wrong: a strategy without a trigger
        // never runs, and the user cannot see why (same pattern as K-034).
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
    public void ContextWindow_without_a_max_window_stops_compilation()
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
    public void ContextWindow_strategy_does_not_require_a_trigger()
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
    public void Every_strategy_is_set_up_with_a_valid_trigger(CompactionStrategyKind strategy)
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
    public void Pipeline_contains_three_strategies_in_a_fixed_order()
    {
        var compiler = CreateCompiler();

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.Pipeline, TriggerMessages = 20 },
        };

#pragma warning disable MAAI001 // Microsoft.Agents.AI.Compaction.* is "evaluation purposes only" — for structural validation.
        var strategy = compiler.BuildCompactionStrategy(definition);
        var pipeline = strategy!.Inner.ShouldBeOfType<PipelineCompactionStrategy>();

        pipeline.Strategies[0].ShouldBeOfType<ToolResultCompactionStrategy>();
        pipeline.Strategies[1].ShouldBeOfType<SlidingWindowCompactionStrategy>();
        pipeline.Strategies[2].ShouldBeOfType<SummarizationCompactionStrategy>();
#pragma warning restore MAAI001
    }

    [Fact]
    public void Summarization_model_is_resolved_from_the_agent_setting()
    {
        var mainClient = new FakeChatClient();
        var agentModelProvider = new FakeModelProvider(mainClient, name: "fake");
        var summarizerClient = new FakeChatClient();
        var summarizerProvider = new FakeModelProvider(summarizerClient, name: "agent-selected");

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(agentModelProvider, summarizerProvider),
            TestData.Registry());

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings
            {
                Strategy = CompactionStrategyKind.Summarization,
                TriggerMessages = 20,
                SummarizationModel = new ModelBinding { Provider = "agent-selected", Model = "m" },
            },
        };

        compiler.Compile(definition);

        summarizerProvider.LastBinding.ShouldNotBeNull();
        agentModelProvider.LastBinding!.Provider.ShouldBe("fake");
    }

    [Fact]
    public void Summarization_model_falls_back_to_the_utility_model_when_the_agent_setting_is_absent()
    {
        var mainClient = new FakeChatClient();
        var agentModelProvider = new FakeModelProvider(mainClient, name: "fake");
        var utilityClient = new FakeChatClient();
        var utilityProvider = new FakeModelProvider(utilityClient, name: "utility");

        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(agentModelProvider, utilityProvider),
            TestData.Registry(),
            utilityModel: new ModelBinding { Provider = "utility", Model = "m" });

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.Summarization, TriggerMessages = 20 },
        };

        compiler.Compile(definition);

        utilityProvider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void Summarization_model_falls_back_to_the_agents_own_model_when_neither_is_set()
    {
        var mainClient = new FakeChatClient();
        var agentModelProvider = new FakeModelProvider(mainClient, name: "fake");

        var compiler = new AgentDefinitionCompiler(TestData.Providers(agentModelProvider), TestData.Registry());

        var definition = TestData.Definition() with
        {
            Compaction = new CompactionSettings { Strategy = CompactionStrategyKind.Summarization, TriggerMessages = 20 },
        };

        compiler.Compile(definition);

        // The summarization call also used the agent's own provider (there is
        // no separately resolved binding).
        agentModelProvider.LastBinding.ShouldNotBeNull();
    }

    [Fact]
    public void File_memory_requested_without_a_registered_store_stops_compilation()
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
    public void Todo_and_text_search_are_set_up_when_a_file_store_is_registered()
    {
#pragma warning disable MAAI001 // InMemoryAgentFileStore is "evaluation purposes only" — for test setup only.
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
    public void Mcp_resources_requested_without_a_registered_factory_stops_compilation()
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
    public void Mcp_resources_are_added_to_the_context_when_the_factory_is_registered()
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
    public void Vector_search_requested_without_a_registered_store_stops_compilation()
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
    public void Vector_search_requested_without_a_registered_embedding_generator_stops_compilation()
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
    public void Vector_search_tool_is_wired_up_when_both_are_registered()
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
    public void Vector_search_stops_compilation_when_the_tenant_cannot_be_resolved()
    {
        // tenantContext is not supplied AND definition.TenantId is empty — the
        // tenant cannot be resolved from any source.
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
