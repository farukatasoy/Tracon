using Tracon.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Reads the four public compile paths (sync <c>Compile</c>, <c>CompileAsync</c>,
/// <c>CompileCachedAsync</c>, <c>CompileParameterizedAsync</c>) against a matrix of
/// culture resolution, callable-agent resolution, shared instructions, and BYOK/cache
/// bypass (phase 106, 106.3). The matrix cells with dedicated coverage elsewhere
/// (culture resolution itself: <see cref="InstructionCultureResolutionTests"/>;
/// shared instructions: <see cref="SharedInstructionsTests"/>; cache-key fingerprints and
/// BYOK cache bypass: <see cref="CompileCachedAsyncTests"/> and
/// <c>DefinitionStoreAgentSourceTenantCredentialTests</c>) are not repeated here. This
/// file exists to catch body drift across the <c>partial</c> split, not to add product
/// behavior.
/// </summary>
public sealed class AgentDefinitionCompilerPathTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public void Sync_full_overload_resolves_the_requested_culture()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var definition = TestData.Definition() with
        {
            InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tr"] = "Turkish-culture instructions.",
            },
        };

        var agent = compiler.Compile(definition, ResolvedCallableAgents.Empty, toolTransform: null, culture: "tr");
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Instructions.ShouldBe("Turkish-culture instructions.");
    }

    [Fact]
    public async Task Async_CompileAsync_resolves_the_requested_culture()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var definition = TestData.Definition() with
        {
            InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tr"] = "Turkish-culture instructions.",
            },
        };

        var agent = await compiler.CompileAsync(definition, ResolvedCallableAgents.Empty, culture: "tr", CancellationToken.None);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Instructions.ShouldBe("Turkish-culture instructions.");
    }

    [Fact]
    public async Task Sync_full_overload_wires_up_resolved_callable_agents()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var catalog = new SingleAgentCatalog(new AgentDescriptor
        {
            Name = "sub",
            Origin = AgentDefinitionOrigin.Code,
            SourceName = "code",
            Version = 1,
        });
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            callableAgents: new CallableAgentResolver(catalog),
            tenantContext: tenantContext);
        var definition = TestData.Definition() with { CallableAgentNames = ["sub"] };
        var callableAgents = await compiler.ResolveCallableAgentsAsync(definition, CancellationToken.None);

        var agent = compiler.Compile(definition, callableAgents);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders.ShouldNotBeNull();
#pragma warning disable MAAI001 // BackgroundAgentsProvider is "evaluation purposes only" — for assertion only.
        options.AIContextProviders!.ShouldContain(static provider => provider is BackgroundAgentsProvider);
#pragma warning restore MAAI001
    }

    [Fact]
    public async Task Async_CompileAsync_wires_up_resolved_callable_agents()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var catalog = new SingleAgentCatalog(new AgentDescriptor
        {
            Name = "sub",
            Origin = AgentDefinitionOrigin.Code,
            SourceName = "code",
            Version = 1,
        });
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            callableAgents: new CallableAgentResolver(catalog),
            tenantContext: tenantContext);
        var definition = TestData.Definition() with { CallableAgentNames = ["sub"] };
        var callableAgents = await compiler.ResolveCallableAgentsAsync(definition, CancellationToken.None);

        var agent = await compiler.CompileAsync(definition, callableAgents, culture: null, CancellationToken.None);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders.ShouldNotBeNull();
#pragma warning disable MAAI001 // BackgroundAgentsProvider is "evaluation purposes only" — for assertion only.
        options.AIContextProviders!.ShouldContain(static provider => provider is BackgroundAgentsProvider);
#pragma warning restore MAAI001
    }

    [Fact]
    public async Task CompileParameterizedAsync_resolves_culture_before_binding_parameters()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

        var definition = TestData.Definition() with
        {
            InstructionsByCulture = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tr"] = "Hello {{name}}.",
            },
            Parameters = [new AgentParameter { Name = "name", Kind = AgentParameterKind.Text }],
        };

        var agent = await compiler.CompileParameterizedAsync(
            definition,
            culture: "tr",
            values: new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "Fatima" },
            CancellationToken.None);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Instructions.ShouldBe("Hello Fatima.");
    }

    [Fact]
    public async Task CompileParameterizedAsync_resolves_shared_instructions()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var store = new InMemoryAgentDefinitionStore(tenantContext);
        await store.SaveAsync(TestData.Definition("shared-block") with { Instructions = "Shared preamble." });
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            definitionStore: store);

        var definition = TestData.Definition() with { SharedInstructionsName = "shared-block" };

        var agent = await compiler.CompileParameterizedAsync(definition, culture: null, values: null, CancellationToken.None);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.ChatOptions!.Instructions.ShouldStartWith("Shared preamble.");
    }

    [Fact]
    public async Task CompileParameterizedAsync_resolves_callable_agents()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var catalog = new SingleAgentCatalog(new AgentDescriptor
        {
            Name = "sub",
            Origin = AgentDefinitionOrigin.Code,
            SourceName = "code",
            Version = 1,
        });
        var compiler = new AgentDefinitionCompiler(
            TestData.Providers(new FakeModelProvider()),
            TestData.Registry(),
            callableAgents: new CallableAgentResolver(catalog),
            tenantContext: tenantContext);
        var definition = TestData.Definition() with { CallableAgentNames = ["sub"] };

        var agent = await compiler.CompileParameterizedAsync(definition, culture: null, values: null, CancellationToken.None);
        var options = agent.GetService<ChatClientAgentOptions>();

        options!.AIContextProviders.ShouldNotBeNull();
#pragma warning disable MAAI001 // BackgroundAgentsProvider is "evaluation purposes only" — for assertion only.
        options.AIContextProviders!.ShouldContain(static provider => provider is BackgroundAgentsProvider);
#pragma warning restore MAAI001
    }

    [Fact]
    public async Task CompileParameterizedAsync_rejects_a_forbidden_provider()
    {
        var tenantContext = new FixedTenantContext(TenantId);
        var egress = new InMemoryTenantEgressPolicyStore();
        await egress.UpsertAsync(TenantId, ["anthropic"]);
        var registry = new ModelProviderRegistry(
            [new FakeModelProvider(name: "openai")],
            tenantContext: tenantContext,
            tenantEgressPolicies: egress);
        var compiler = new AgentDefinitionCompiler(registry, TestData.Registry(), tenantContext: tenantContext);

        var definition = TestData.Definition() with { Model = TestData.Binding(provider: "openai") };

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await compiler.CompileParameterizedAsync(definition, culture: null, values: null, CancellationToken.None));

        exception.Message.ShouldContain("openai");
    }

    /// <summary>An <see cref="IAgentCatalog"/> that always reports the same, single descriptor.</summary>
    private sealed class SingleAgentCatalog(AgentDescriptor descriptor) : IAgentCatalog, IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IAgentCatalog) ? this : null;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[descriptor]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
            => new((AIAgent?)null);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, string? culture = null, CancellationToken cancellationToken = default)
            => ResolveAsync(agentName, culture, cancellationToken);
    }
}
