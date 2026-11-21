using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// A caller that has to know what an agent expects before it runs one asks the catalog to describe it: whether the name
/// exists, and which parameters the definition declares. Nothing is compiled and no run is written.
/// </summary>
public sealed class CatalogDescribeTests
{
    private static readonly AgentParameter[] Declared =
    [
        new() { Name = "articleContent", Kind = AgentParameterKind.Text, Required = true, Description = "The article." },
        new() { Name = "level", Kind = AgentParameterKind.Number, DefaultValue = "2" },
    ];

    [Fact]
    public void A_descriptor_built_from_a_definition_carries_the_parameters_it_declares()
    {
        var definition = TestData.Definition("summarizer") with { Parameters = Declared };

        var descriptor = AgentDescriptorProjection.FromDefinition(definition, AgentDefinitionOrigin.Database, "database");

        descriptor.Parameters.ShouldBe(Declared);
    }

    [Fact]
    public async Task Describing_a_name_returns_the_descriptor_of_the_source_that_wins_it_with_its_parameters()
    {
        var catalog = CreateCatalog(
            new StubSource("database", priority: 100, "shared", parameters: []),
            new StubSource("code", priority: 0, "shared", parameters: Declared));

        var descriptor = (await catalog.DescribeAsync("shared", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        descriptor.SourceName.ShouldBe("code");
        descriptor.Parameters.Select(static parameter => parameter.Name).ShouldBe(["articleContent", "level"]);
        descriptor.Parameters[0].Required.ShouldBeTrue();
        descriptor.Parameters[1].DefaultValue.ShouldBe("2");
    }

    [Fact]
    public async Task Describing_a_name_no_source_holds_returns_null()
    {
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha", parameters: []));

        (await catalog.DescribeAsync("missing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task Describing_a_name_does_not_resolve_the_agent()
    {
        var source = new StubSource("code", priority: 0, "alpha", parameters: Declared);
        var catalog = CreateCatalog(source);

        await catalog.DescribeAsync("alpha", TestContext.Current.CancellationToken);

        source.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task A_source_that_can_describe_one_name_is_not_asked_for_its_whole_listing()
    {
        var named = new NamedSource("database", priority: 0, "alpha", Declared);
        var catalog = CreateCatalog(named);

        var descriptor = (await catalog.DescribeAsync("alpha", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        descriptor.Parameters.Count.ShouldBe(2);
        named.ListCalls.ShouldBe(0);
        named.DescribeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task The_descriptor_a_caller_holds_does_not_change_when_the_source_changes_its_parameters()
    {
        var parameters = new List<AgentParameter>(Declared);
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha", parameters));

        var descriptor = (await catalog.DescribeAsync("alpha", TestContext.Current.CancellationToken)).ShouldNotBeNull();

        parameters.Add(new AgentParameter { Name = "added", Kind = AgentParameterKind.Text });

        descriptor.Parameters.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_catalog_that_only_lists_describes_a_name_by_searching_its_listing()
    {
        IAgentCatalog catalog = new ListOnlyCatalog(
            new AgentDescriptor { Name = "alpha", Origin = AgentDefinitionOrigin.Code, SourceName = "code", Parameters = Declared });

        (await catalog.DescribeAsync("alpha", TestContext.Current.CancellationToken)).ShouldNotBeNull().Parameters.Count.ShouldBe(2);
        (await catalog.DescribeAsync("missing", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_source_that_fails_is_skipped_and_the_next_source_still_answers()
    {
        var catalog = CreateCatalog(
            new FailingSource("broken", priority: 0, new InvalidOperationException("the store is down")),
            new StubSource("code", priority: 10, "alpha", parameters: Declared));

        (await catalog.DescribeAsync("alpha", TestContext.Current.CancellationToken)).ShouldNotBeNull().SourceName.ShouldBe("code");
    }

    [Fact]
    public async Task A_name_only_a_failing_source_holds_is_not_found()
    {
        var catalog = CreateCatalog(new FailingSource("broken", priority: 0, new InvalidOperationException("the store is down")));

        (await catalog.DescribeAsync("alpha", TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task A_canceled_call_throws_and_is_not_taken_for_a_failing_source()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        var catalog = CreateCatalog(new FailingSource("slow", priority: 0, new OperationCanceledException(canceled.Token)));

        await Should.ThrowAsync<OperationCanceledException>(async () => await catalog.DescribeAsync("alpha", canceled.Token));
    }

    [Fact]
    public async Task A_blank_name_is_refused()
    {
        var catalog = CreateCatalog(new StubSource("code", priority: 0, "alpha", parameters: []));

        await Should.ThrowAsync<ArgumentException>(async () => await catalog.DescribeAsync(" ", TestContext.Current.CancellationToken));
    }

    private static CompositeAgentCatalog CreateCatalog(params IAgentSource[] sources)
        => new(sources, [], NullLogger<CompositeAgentCatalog>.Instance);

    private class StubSource(string name, int priority, string heldName, IReadOnlyList<AgentParameter> parameters) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public int ResolveCalls { get; private set; }

        public int ListCalls { get; private set; }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;

            return new((IReadOnlyList<AgentDescriptor>)[Describe()]);
        }

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
        {
            ResolveCalls++;

            var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());

            return new ValueTask<AIAgent?>(compiler.Compile(TestData.Definition(agentName)));
        }

        protected AgentDescriptor Describe()
            => new()
            {
                Name = heldName,
                Origin = AgentDefinitionOrigin.Code,
                SourceName = Name,
                Parameters = parameters,
            };

        protected bool Holds(string agentName) => string.Equals(agentName, heldName, StringComparison.Ordinal);
    }

    private sealed class NamedSource(string name, int priority, string heldName, IReadOnlyList<AgentParameter> parameters)
        : StubSource(name, priority, heldName, parameters), INamedDescriptorSource
    {
        public int DescribeCalls { get; private set; }

        public ValueTask<AgentDescriptor?> DescribeAsync(string agentName, CancellationToken cancellationToken)
        {
            DescribeCalls++;

            return new(Holds(agentName) ? Describe() : null);
        }
    }

    private sealed class FailingSource(string name, int priority, Exception failure) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw failure;

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => throw failure;
    }

    private sealed class ListOnlyCatalog(params AgentDescriptor[] descriptors) : IAgentCatalog
    {
        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new(descriptors);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, AgentResolveOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
