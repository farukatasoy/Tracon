using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// The definition a replay, a continuation and the reconciler compile again: the one the catalog runs, and only when
/// Tracon compiles it.
/// </summary>
public sealed class CompilableDefinitionReaderTests
{
    private const string Name = "read-agent";

    [Fact]
    public async Task Without_a_factory_the_store_is_read_and_a_pinned_version_is_honoured()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition(Name) with { Instructions = "one" }, TestContext.Current.CancellationToken);
        await store.SaveAsync(TestData.Definition(Name) with { Instructions = "two" }, TestContext.Current.CancellationToken);

        var current = await CompilableDefinitionReader.ReadAsync(store, factory: null, Name, version: null, TestContext.Current.CancellationToken);
        var pinned = await CompilableDefinitionReader.ReadAsync(store, factory: null, Name, version: 1, TestContext.Current.CancellationToken);

        current.Definition.ShouldNotBeNull().Instructions.ShouldBe("two");
        pinned.Definition.ShouldNotBeNull().Instructions.ShouldBe("one");
        current.Origin.ShouldBe(AgentDefinitionOrigin.Database);
    }

    [Fact]
    public async Task A_source_that_outranks_the_store_decides_and_the_stored_copy_is_ignored()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition(Name) with { Instructions = "stored" }, TestContext.Current.CancellationToken);

        var source = new InMemoryDefinitionSource("definitions", priority: AgentSourcePriority.Database - 50)
            .Add(TestData.Definition(Name) with { Instructions = "from the source" });

        var read = await ReadAsync(store, [DefinitionSource(source)]);

        read.Definition.ShouldNotBeNull().Instructions.ShouldBe("from the source");
        read.Origin.ShouldBe(AgentDefinitionOrigin.Custom);
    }

    [Fact]
    public async Task The_store_decides_when_no_source_outranks_it()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition(Name) with { Instructions = "stored" }, TestContext.Current.CancellationToken);

        var source = new InMemoryDefinitionSource("definitions", priority: AgentSourcePriority.Database + 50)
            .Add(TestData.Definition(Name) with { Instructions = "from the source" });

        var read = await ReadAsync(store, [DefinitionSource(source), StoreSource(store)]);

        read.Definition.ShouldNotBeNull().Instructions.ShouldBe("stored");
        read.Origin.ShouldBe(AgentDefinitionOrigin.Database);
    }

    [Fact]
    public async Task An_opaque_source_that_outranks_the_store_leaves_no_definition_to_compile()
    {
        var store = new InMemoryAgentDefinitionStore();

        await store.SaveAsync(TestData.Definition(Name) with { Instructions = "stored" }, TestContext.Current.CancellationToken);

        var read = await ReadAsync(store, [new OpaqueSource(priority: 10), StoreSource(store)]);

        read.Definition.ShouldBeNull("the catalog serves the opaque source's agent, which has no definition");
        read.Held.ShouldBeTrue();
    }

    [Fact]
    public async Task A_source_that_keeps_no_history_is_read_for_the_version_a_run_recorded_as_its_current_definition()
    {
        // The catalog stamps every run with the version number its definition carries, whether or not the source keeps a
        // history. That number is not a pin: the source holds one definition, and it is the one to compile.
        var store = new InMemoryAgentDefinitionStore();
        var source = new InMemoryDefinitionSource("definitions", priority: AgentSourcePriority.Database - 50)
            .Add(TestData.Definition(Name) with { Instructions = "from the source" });

        var read = await ReadAsync(store, [DefinitionSource(source)], version: 1);

        read.Definition.ShouldNotBeNull().Instructions.ShouldBe("from the source");
    }

    [Fact]
    public async Task A_source_that_keeps_a_history_is_asked_for_exactly_the_version_a_run_recorded()
    {
        var store = new InMemoryAgentDefinitionStore();
        var source = new VersionedInMemoryDefinitionSource("definitions", priority: AgentSourcePriority.Database - 50)
            .Add(TestData.Definition(Name) with { Instructions = "first" })
            .Add(TestData.Definition(Name) with { Instructions = "second" });

        var first = await ReadAsync(store, [DefinitionSource(source)], version: 1);
        var gone = await ReadAsync(store, [DefinitionSource(source)], version: 9);

        first.Definition.ShouldNotBeNull().Instructions.ShouldBe("first");
        gone.Definition.ShouldBeNull("a pin the source cannot meet is not answered with another version");
        gone.Held.ShouldBeTrue();
    }

    [Fact]
    public async Task A_code_registered_definition_is_not_compiled_again()
    {
        // A code agent is built by the host's own factory; compiling its definition would skip that code.
        var store = new InMemoryAgentDefinitionStore();
        var read = await ReadAsync(store, [new CodeLikeSource(TestData.Definition(Name))]);

        read.Definition.ShouldBeNull();
        read.Origin.ShouldBe(AgentDefinitionOrigin.Code);
    }

    [Fact]
    public async Task A_source_that_fails_while_the_name_is_looked_up_raises_a_tracon_error()
    {
        var store = new InMemoryAgentDefinitionStore();
        var failing = new InMemoryDefinitionSource("definitions", priority: AgentSourcePriority.Database - 50)
        {
            Failure = new InvalidOperationException("raw upstream failure"),
        };

        await Should.ThrowAsync<TraconException>(
            async () => await ReadAsync(store, [DefinitionSource(failing)]));
    }

    private static async Task<FoundDefinition> ReadAsync(IAgentDefinitionStore store, IAgentSource[] sources, int? version = null)
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var lookup = new AgentDefinitionLookup(sources);
        var factory = new ParameterizedAgentFactory(lookup, compiler, [], TestData.DefaultOptionsMonitor());

        return await CompilableDefinitionReader.ReadAsync(store, factory, Name, version, TestContext.Current.CancellationToken);
    }

    private static IAgentSource DefinitionSource(InMemoryDefinitionSource source)
    {
        var services = new ServiceCollection()
            .AddSingleton(new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()))
            .AddSingleton(new CompiledAgentCache())
            .AddSingleton<ITenantContext>(new FixedTenantContext())
            .BuildServiceProvider();

        return DefinitionSourceAgentSource.Create(source, services);
    }

    private static DefinitionStoreAgentSource StoreSource(IAgentDefinitionStore store)
        => new DefinitionStoreAgentSource(
            store,
            new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()),
            new CompiledAgentCache(),
            new FixedTenantContext());

    private sealed class OpaqueSource(int priority) : IAgentSource
    {
        public string Name => "opaque";

        public int Priority => priority;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[new AgentDescriptor { Name = CompilableDefinitionReaderTests.Name, Origin = AgentDefinitionOrigin.Custom, SourceName = Name }]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }

    /// <summary>A source of the shape a code registration has: it holds a definition and its origin is code.</summary>
    private sealed class CodeLikeSource(AgentDefinition definition) : IAgentSource, IDefinitionBackedSource
    {
        public string Name => "code-like";

        public int Priority => AgentSourcePriority.Code;

        public AgentDefinitionOrigin Origin => AgentDefinitionOrigin.Code;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[new AgentDescriptor { Name = definition.Name, Origin = AgentDefinitionOrigin.Code, SourceName = Name }]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);

        public ValueTask<HeldDefinition?> FindDefinitionAsync(string agentName, int? version, CancellationToken cancellationToken)
            => new(string.Equals(agentName, definition.Name, StringComparison.Ordinal) ? new HeldDefinition(definition) : null);
    }
}
