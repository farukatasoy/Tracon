using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;
using Tracon.Core.UnitTests.Fakes;
using Tracon.Core.UnitTests.Tools;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Memory;

/// <summary>
/// What the compiler does with a definition that asks for conversation memory: it builds the provider
/// when the host set memory up, and stops, naming the missing piece, when it did not.
/// </summary>
// MAAI001: the context provider types are marked "evaluation purposes only" - see K-020.
#pragma warning disable MAAI001
public sealed class ConversationMemoryCompileTests
{
    private static readonly MemorySettings On = new() { EnableConversationMemory = true };

    [Fact]
    public void A_definition_with_memory_on_gets_the_scope_provider()
    {
        IEnumerable<AIContextProvider>? providers = null;
        var compiler = Compiler(
            Services(new InMemoryVectorStore(), dimensions: 8),
            new DelegateAgentExtension("probe", context => providers = context.ChatAgentOptions!.AIContextProviders, null));

        _ = compiler.Compile(Definition() with { Memory = On, ExtensionNames = ["probe"] });

        providers.ShouldNotBeNull().OfType<ConversationMemoryScopeProvider>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_definition_with_memory_off_gets_no_provider_and_needs_no_setup()
    {
        IEnumerable<AIContextProvider>? providers = null;
        var compiler = Compiler(
            services: null,
            new DelegateAgentExtension("probe", context => providers = context.ChatAgentOptions!.AIContextProviders, null));

        _ = compiler.Compile(Definition() with { Memory = new MemorySettings { EnableTodo = true }, ExtensionNames = ["probe"] });

        (providers ?? []).OfType<ConversationMemoryScopeProvider>().ShouldBeEmpty();
    }

    [Fact]
    public void Memory_on_without_UseConversationMemory_stops_the_build_and_names_it()
    {
        var services = new ServiceCollection().AddSingleton<VectorStore>(new InMemoryVectorStore()).BuildServiceProvider();
        var compiler = Compiler(services);

        var failure = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with { Memory = On }));

        failure.Message.ShouldContain("UseConversationMemory");
        failure.AgentName.ShouldBe("test-agent");
    }

    [Fact]
    public void Memory_on_without_a_registered_VectorStore_stops_the_build_and_names_it()
    {
        var services = new ServiceCollection()
            .AddOptions<TraconConversationMemoryOptions>().Configure(static options => options.VectorDimensions = 8).Services
            .BuildServiceProvider();
        var compiler = Compiler(services);

        var failure = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with { Memory = On }));

        failure.Message.ShouldContain("VectorStore");
    }

    [Fact]
    public void Memory_on_in_a_compiler_built_without_a_container_stops_the_build()
    {
        var compiler = Compiler(services: null);

        Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with { Memory = On }))
            .Message.ShouldContain("UseConversationMemory");
    }

    [Fact]
    public void The_search_tool_of_the_on_demand_mode_cannot_share_its_name_with_a_tool_of_the_agent()
    {
        var compiler = Compiler(
            Services(new InMemoryVectorStore(), dimensions: 8),
            registry: TestData.Registry(TestData.Tool(ConversationMemoryScopeProvider.SearchToolName)));

        var failure = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with
        {
            ToolNames = [ConversationMemoryScopeProvider.SearchToolName],
            Memory = On with { ConversationMemorySearch = ConversationMemorySearch.OnDemand },
        }));

        failure.Message.ShouldContain(ConversationMemoryScopeProvider.SearchToolName);
    }

    [Fact]
    public void The_on_demand_mode_gets_the_scope_provider_when_the_tool_chain_is_registered()
    {
        IEnumerable<AIContextProvider>? providers = null;
        var compiler = Compiler(
            Services(new InMemoryVectorStore(), dimensions: 8, toolChain: true),
            new DelegateAgentExtension("probe", context => providers = context.ChatAgentOptions!.AIContextProviders, null));

        _ = compiler.Compile(Definition() with
        {
            Memory = On with { ConversationMemorySearch = ConversationMemorySearch.OnDemand },
            ExtensionNames = ["probe"],
        });

        providers.ShouldNotBeNull().OfType<ConversationMemoryScopeProvider>().ShouldHaveSingleItem();
    }

    [Fact]
    public void The_on_demand_mode_without_the_tool_chain_stops_the_build_instead_of_offering_an_ungoverned_tool()
    {
        // F-688: the search tool must run through the tool chain, so a compiler that cannot build it does not offer the tool.
        var compiler = Compiler(Services(new InMemoryVectorStore(), dimensions: 8));

        var failure = Should.Throw<TraconCompilationException>(() => compiler.Compile(Definition() with
        {
            Memory = On with { ConversationMemorySearch = ConversationMemorySearch.OnDemand },
        }));

        failure.Message.ShouldContain("AddTracon");
        failure.AgentName.ShouldBe("test-agent");
    }

    [Fact]
    public void The_before_each_turn_mode_leaves_the_tool_name_free()
    {
        var compiler = Compiler(
            Services(new InMemoryVectorStore(), dimensions: 8),
            registry: TestData.Registry(TestData.Tool(ConversationMemoryScopeProvider.SearchToolName)));

        Should.NotThrow(() => compiler.Compile(Definition() with { ToolNames = [ConversationMemoryScopeProvider.SearchToolName], Memory = On }));
    }

    [Fact]
    public void The_record_definition_is_the_one_the_framework_hands_the_store()
    {
        // The eraser reads and deletes the records the framework wrote, from a collection it opens with its
        // own copy of the definition. A framework version that changes the record fails here.
        var store = new InMemoryVectorStore();
        var compiler = Compiler(Services(store, dimensions: 8));

        _ = compiler.Compile(Definition() with { Memory = On });

        var framework = store.Definition.ShouldNotBeNull().Properties;
        var ours = ConversationMemorySchema.CreateDefinition(8).Properties;

        Describe(ours).ShouldBe(Describe(framework));

        static string[] Describe(IEnumerable<VectorStoreProperty> properties)
            => [.. properties.Select(static property => property switch
            {
                VectorStoreKeyProperty key => $"key {key.Name} {key.Type?.Name}",
                VectorStoreDataProperty data => $"data {data.Name} {data.Type?.Name} indexed={data.IsIndexed} fullText={data.IsFullTextIndexed}",
                VectorStoreVectorProperty vector => $"vector {vector.Name} {vector.Type?.Name} {vector.Dimensions}",
                _ => property.Name,
            })];
    }

    private static AgentDefinition Definition() => TestData.Definition();

    private static ServiceProvider Services(VectorStore store, int dimensions, bool toolChain = false)
    {
        var services = new ServiceCollection()
            .AddSingleton(store)
            .AddOptions<TraconConversationMemoryOptions>().Configure(options => options.VectorDimensions = dimensions).Services;

        if (toolChain)
        {
            services.AddSingleton(BuiltInToolGovernanceTestSupport.Create());
        }

        return services.BuildServiceProvider();
    }

    private static AgentDefinitionCompiler Compiler(
        IServiceProvider? services,
        IAgentExtension? extension = null,
        ToolRegistry? registry = null)
        => new(
            TestData.Providers(new FakeModelProvider()),
            registry ?? TestData.Registry(),
            services: services,
            tenantContext: new FixedTenantContext("tenant-a"),
            agentExtensions: extension is null ? null : [extension]);
}
