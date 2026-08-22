using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Compilation;

public sealed class SharedInstructionsTests
{
    [Fact]
    public async Task Blocks_text_is_prepended_to_the_agents_own_instructions()
    {
        var store = new InMemoryAgentDefinitionStore();
        var block = await store.SaveAsync(
            TestData.Definition(name: "house-rules") with { Instructions = "Always be polite." });

        var compiler = CreateCompiler(store);
        var definition = TestData.Definition() with
        {
            Instructions = "Answer billing questions.",
            SharedInstructionsName = block.Name,
        };

        var agent = await compiler.CompileAsync(definition, CancellationToken.None);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Instructions.ShouldBe("Always be polite.\n\nAnswer billing questions.");
    }

    [Fact]
    public async Task Unknown_block_name_stops_compilation()
    {
        var compiler = CreateCompiler(new InMemoryAgentDefinitionStore());
        var definition = TestData.Definition() with { SharedInstructionsName = "does-not-exist" };

        var exception = await Should.ThrowAsync<AgentPrismCompilationException>(
            () => compiler.CompileAsync(definition, CancellationToken.None).AsTask());

        exception.Message.ShouldContain("does-not-exist");
    }

    [Fact]
    public async Task Block_referencing_another_block_is_rejected_at_compile_time()
    {
        var store = new InMemoryAgentDefinitionStore();
        var innerBlock = await store.SaveAsync(TestData.Definition(name: "inner") with { Instructions = "Inner." });
        await store.SaveAsync(TestData.Definition(name: "outer") with
        {
            Instructions = "Outer.",
            SharedInstructionsName = innerBlock.Name,
        });

        var compiler = CreateCompiler(store);
        var definition = TestData.Definition() with { SharedInstructionsName = "outer" };

        var exception = await Should.ThrowAsync<AgentPrismCompilationException>(
            () => compiler.CompileAsync(definition, CancellationToken.None).AsTask());

        exception.Message.ShouldContain("cannot reference another block");
    }

    [Fact]
    public void No_registered_store_stops_compilation_when_a_block_is_referenced()
    {
        var compiler = CreateCompiler(definitionStore: null);
        var definition = TestData.Definition() with { SharedInstructionsName = "house-rules" };

        var exception = Should.Throw<AggregateException>(
                () => compiler.CompileAsync(definition, CancellationToken.None).AsTask().Wait())
            .InnerException as AgentPrismCompilationException;

        exception.ShouldNotBeNull();
        exception!.Message.ShouldContain("IAgentDefinitionStore");
    }

    [Fact]
    public void Synchronous_Compile_refuses_a_definition_that_references_a_block()
    {
        // 🚨 The sync Compile() path never resolves SharedInstructionsName (it
        // would need an async store read). Silently dropping the block's
        // content would be a content bug hiding behind a green build; this
        // must fail loudly instead.
        var compiler = CreateCompiler(new InMemoryAgentDefinitionStore());
        var definition = TestData.Definition() with { SharedInstructionsName = "house-rules" };

        var exception = Should.Throw<AgentPrismCompilationException>(() => compiler.Compile(definition));

        exception.Message.ShouldContain("CompileAsync");
    }

    [Fact]
    public async Task Shared_block_resolves_to_the_calling_tenants_own_content_not_another_tenants()
    {
        // Shared-instructions blocks are ordinary AgentDefinition rows (no new
        // table); resolution is a plain IAgentDefinitionStore.GetAsync call,
        // scoped by the ambient ITenantContext exactly like every other read.
        // This proves that scoping actually reaches the compiler's new
        // consumption path, using the SAME store instance for both tenants so
        // a broken (unscoped) lookup would be caught, not hidden by two
        // separate stores.
        var tenantContext = new MutableTenantContext("tenant-a");
        var store = new InMemoryAgentDefinitionStore(tenantContext);

        await store.SaveAsync(TestData.Definition(name: "house-rules") with { Instructions = "Tenant A rules." });

        tenantContext.TenantId = "tenant-b";
        await store.SaveAsync(TestData.Definition(name: "house-rules") with { Instructions = "Tenant B rules." });

        tenantContext.TenantId = "tenant-a";
        var compiler = CreateCompiler(store);
        var definition = TestData.Definition() with
        {
            Instructions = "Answer billing questions.",
            SharedInstructionsName = "house-rules",
        };

        var agent = await compiler.CompileAsync(definition, CancellationToken.None);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Instructions.ShouldBe("Tenant A rules.\n\nAnswer billing questions.");
    }

    private static AgentDefinitionCompiler CreateCompiler(IAgentDefinitionStore? definitionStore)
        => new(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            definitionStore: definitionStore);

    private sealed class MutableTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; set; } = tenantId;
    }
}
