using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Catalog;

/// <summary>
/// 101.5: a source that resolves a name it does not report from <c>ListAsync</c> is a
/// contract violation, but a legitimate race exists — the agent can be deleted between
/// the winning source's <c>ListAsync</c> and its <c>ResolveAsync</c> (<c>DELETE
/// /api/agents/{name}</c>). The catalog must not throw for this; it must not FABRICATE a
/// descriptor either (the old behavior defaulted to <c>Origin = Code</c> and
/// <c>Model = null</c>, attributing the run to the wrong source). It records a real,
/// honestly-attributed descriptor and logs the inconsistency instead.
/// </summary>
public sealed class AgentSourceConsistencyTests
{
    [Fact]
    public async Task Resolving_a_name_the_source_does_not_list_does_not_throw()
    {
        var source = new UnlistedResolveSource("ghost", "phantom-agent");
        var catalog = new CompositeAgentCatalog([source], [], NullLogger<CompositeAgentCatalog>.Instance);

        var agent = await catalog.ResolveAsync("phantom-agent", culture: null, CancellationToken.None);

        agent.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_synthesized_descriptor_names_the_real_source_and_does_not_fabricate_Code_origin()
    {
        var source = new UnlistedResolveSource("ghost", "phantom-agent");
        var decorator = new SpyDecorator();
        var catalog = new CompositeAgentCatalog([source], [decorator], NullLogger<CompositeAgentCatalog>.Instance);

        await catalog.ResolveAsync("phantom-agent", culture: null, CancellationToken.None);

        var descriptor = decorator.LastDescriptor.ShouldNotBeNull();
        descriptor.Name.ShouldBe("phantom-agent");
        descriptor.SourceName.ShouldBe("ghost");
        // The old behavior defaulted a synthesized descriptor to Origin.Code, which is
        // never correct for a source that is not the built-in code source.
        descriptor.Origin.ShouldBe(AgentDefinitionOrigin.Custom);
        descriptor.Model.ShouldBeNull();
    }

    [Fact]
    public async Task The_inconsistency_is_logged_and_recorded_as_a_metric()
    {
        var source = new UnlistedResolveSource("ghost", "phantom-agent");
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        var catalog = new CompositeAgentCatalog([source], [], NullLogger<CompositeAgentCatalog>.Instance, metrics);

        await catalog.ResolveAsync("phantom-agent", culture: null, CancellationToken.None);

        collector.LongValues(AgentPrismDiagnostics.AgentSourceFailureCounterName).ShouldBe([1]);
    }

    /// <summary>A source that resolves a name its own <c>ListAsync</c> never reports — the contract violation under test.</summary>
    private sealed class UnlistedResolveSource(string name, string resolvableName) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority => 0;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
        {
            if (!string.Equals(agentName, resolvableName, StringComparison.Ordinal))
            {
                return new ValueTask<AIAgent?>((AIAgent?)null);
            }

            var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
            return new ValueTask<AIAgent?>(compiler.Compile(TestData.Definition(agentName)));
        }
    }

    private sealed class SpyDecorator : IAgentDecorator
    {
        public int Order => 0;

        public AgentDescriptor? LastDescriptor { get; private set; }

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        {
            LastDescriptor = descriptor;
            return agent;
        }
    }
}
