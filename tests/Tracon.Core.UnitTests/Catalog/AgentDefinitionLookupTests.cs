using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// The walk the parameter schema is read with: the catalog's own source order, and the first source that holds a name
/// decides.
/// </summary>
public sealed class AgentDefinitionLookupTests
{
    private const string Name = "looked-up";

    [Fact]
    public async Task The_source_with_the_lower_priority_value_decides_even_when_a_later_one_has_a_definition()
    {
        var outranking = new OpaqueSource("outranking", priority: 10, holds: [Name]);
        var definitions = Definitions(new InMemoryDefinitionSource("definitions", priority: 100).Add(TestData.Definition(Name)));

        var found = await new AgentDefinitionLookup([definitions, outranking]).FindAsync(Name, version: null, TestContext.Current.CancellationToken);

        found.Held.ShouldBeTrue();
        found.Source.ShouldBeSameAs(outranking);
        found.Definition.ShouldBeNull();
        found.Origin.ShouldBe(AgentDefinitionOrigin.Custom);
    }

    [Fact]
    public async Task An_opaque_source_that_does_not_list_the_name_lets_the_next_source_decide()
    {
        var opaque = new OpaqueSource("empty", priority: 10, holds: []);
        var definitions = Definitions(new InMemoryDefinitionSource("definitions", priority: 100).Add(TestData.Definition(Name)));

        var found = await new AgentDefinitionLookup([opaque, definitions]).FindAsync(Name, version: null, TestContext.Current.CancellationToken);

        found.Source.ShouldBeSameAs(definitions);
        found.Definition.ShouldNotBeNull().Name.ShouldBe(Name);
    }

    [Fact]
    public async Task An_opaque_source_whose_listing_fails_is_taken_to_hold_the_name_and_the_failure_is_recorded()
    {
        // The lookup never reads another source's schema over the one that failed: the catalog would raise the failure
        // on resolve, so a definition behind it is not the agent the catalog runs.
        var failing = new OpaqueSource("failing", priority: 10, holds: [], failure: new InvalidOperationException("raw upstream failure"));
        var definitions = Definitions(new InMemoryDefinitionSource("definitions", priority: 100).Add(TestData.Definition(Name)));
        using var meterFactory = new TestMeterFactory();
        using var metrics = new TraconMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);
        var lookup = new AgentDefinitionLookup([failing, definitions], new AgentSourceFailures(NullLogger.Instance, metrics));

        var found = await lookup.FindAsync(Name, version: null, TestContext.Current.CancellationToken);

        found.Source.ShouldBeSameAs(failing);
        found.Definition.ShouldBeNull();
        collector.Values(TraconDiagnostics.AgentSourceFailureCounterName).ShouldBe([1]);
    }

    [Fact]
    public async Task A_cancelled_listing_is_not_taken_as_holding_the_name()
    {
        var cancelling = new OpaqueSource("cancelling", priority: 10, holds: [], failure: new OperationCanceledException());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await new AgentDefinitionLookup([cancelling]).FindAsync(Name, version: null, cancelled.Token));
    }

    [Fact]
    public async Task A_name_no_source_holds_is_not_held()
    {
        var found = await new AgentDefinitionLookup([new OpaqueSource("empty", priority: 10, holds: [])])
            .FindAsync(Name, version: null, TestContext.Current.CancellationToken);

        found.Held.ShouldBeFalse();
        found.Source.ShouldBeNull();
    }

    private static IAgentSource Definitions(InMemoryDefinitionSource source)
    {
        var services = new ServiceCollection()
            .AddSingleton(new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry()))
            .AddSingleton(new CompiledAgentCache())
            .AddSingleton<ITenantContext>(new FixedTenantContext())
            .BuildServiceProvider();

        return DefinitionSourceAgentSource.Create(source, services);
    }

    private sealed class OpaqueSource(string name, int priority, string[] holds, Exception? failure = null) : IAgentSource
    {
        public string Name => name;

        public int Priority => priority;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => failure is not null
                ? throw failure
                : new((IReadOnlyList<AgentDescriptor>)
                [
                    .. holds.Select(held => new AgentDescriptor { Name = held, Origin = AgentDefinitionOrigin.Custom, SourceName = name }),
                ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }
}
