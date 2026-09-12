using Tracon.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>
/// <c>IAgentDecorator</c> is a multi-registration seam (BL-008, phase 122): unlike
/// <c>IAgentSource</c> and <c>IRunJudge</c>, it had no dedicated <c>Add*()</c> method before
/// this phase. These tests prove the new <c>AddAgentDecorator</c> overloads follow the exact
/// same DI contract as their siblings, and that the documented escape-hatch before/after
/// distinction on <c>ITraconBuilder.Services</c> is real, measured behavior.
/// </summary>
public sealed class AgentDecoratorRegistrationTests
{
    [Fact]
    public void Generic_registration_is_singleton_and_idempotent()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddAgentDecorator<CountingDecorator>()
            .AddAgentDecorator<CountingDecorator>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetServices<IAgentDecorator>().OfType<CountingDecorator>().ShouldHaveSingleItem();
        var second = provider.GetServices<IAgentDecorator>().OfType<CountingDecorator>().ShouldHaveSingleItem();

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void Instance_registration_preserves_multiple_configurations_and_identity()
    {
        var first = new NamedDecorator("first", order: 1);
        var second = new NamedDecorator("second", order: 2);
        var services = new ServiceCollection();
        services.AddTracon().AddAgentDecorator(first).AddAgentDecorator(second);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetServices<IAgentDecorator>().OfType<NamedDecorator>().ToArray();

        resolved.ShouldBe([first, second]);
    }

    [Fact]
    public void Factory_registration_runs_once_and_preserves_multiple_configurations()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddTracon()
            .AddAgentDecorator(_ =>
            {
                calls++;
                return new NamedDecorator("first", order: 1);
            })
            .AddAgentDecorator(_ => new NamedDecorator("second", order: 2));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IAgentDecorator>().OfType<NamedDecorator>()
            .Select(static decorator => decorator.Name)
            .ShouldBe(["first", "second"]);
        calls.ShouldBe(1);
    }

    /// <summary>
    /// Proves the "before" row of the table added to <c>ITraconBuilder.Services</c>'s
    /// documentation: a multi-registration seam registered through the raw escape hatch BEFORE
    /// <c>AddTracon()</c> joins the list alongside the built-in decorators, it does not
    /// replace them.
    /// </summary>
    [Fact]
    public void An_escape_hatch_registration_made_before_AddTracon_joins_the_built_ins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAgentDecorator>(new NamedDecorator("custom", order: 5));
        services.AddTracon();
        using var provider = services.BuildServiceProvider();

        var decorators = provider.GetServices<IAgentDecorator>().ToArray();

        decorators.OfType<NamedDecorator>().ShouldHaveSingleItem();
        decorators.OfType<RunRecordingAgentDecorator>().ShouldHaveSingleItem();
    }

    /// <summary>
    /// Proves the "after" row: unlike a single-instance seam, registering AFTER
    /// <c>AddTracon()</c> does not remove or shadow the built-in decorators - both run. This
    /// is the "real behavior difference from the single-instance case" the documentation calls out.
    /// </summary>
    [Fact]
    public void An_escape_hatch_registration_made_after_AddTracon_also_joins_the_built_ins()
    {
        var services = new ServiceCollection();
        services.AddTracon();
        services.AddSingleton<IAgentDecorator>(new NamedDecorator("custom", order: 5));
        using var provider = services.BuildServiceProvider();

        var decorators = provider.GetServices<IAgentDecorator>().ToArray();

        decorators.OfType<NamedDecorator>().ShouldHaveSingleItem();
        decorators.OfType<RunRecordingAgentDecorator>().ShouldHaveSingleItem();
    }

    /// <summary>
    /// The primary DoD assertion: three decorators registered through
    /// <c>AddAgentDecorator</c> all run when the catalog resolves an agent, in
    /// <see cref="IAgentDecorator.Order"/> order (highest first, so it ends up outermost).
    /// </summary>
    [Fact]
    public async Task Three_registered_decorators_all_run_in_order_order()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddAgentDecorator(new RecordingDecorator("outer", order: 0))
            .AddAgentDecorator(new RecordingDecorator("middle", order: 10))
            .AddAgentDecorator(new RecordingDecorator("inner", order: 20));
        using var provider = services.BuildServiceProvider();

        var decorators = provider.GetServices<IAgentDecorator>().OfType<RecordingDecorator>().ToArray();
        decorators.Length.ShouldBe(3);

        var applied = new List<string>();
        var source = new StubSource("code", priority: 0, "alpha");
        var catalog = new CompositeAgentCatalog(
            [source],
            decorators.Select(decorator => decorator.WithSink(applied)),
            NullLogger<CompositeAgentCatalog>.Instance);

        await catalog.ResolveAsync("alpha", culture: null, CancellationToken.None);

        // CompositeAgentCatalog sorts by descending Order and wraps the previous
        // step's result each time, so the HIGHEST Order runs FIRST and ends up
        // innermost - "inner" (20) before "middle" (10) before "outer" (0).
        applied.ShouldBe(["inner", "middle", "outer"]);
    }

    private sealed class CountingDecorator : IAgentDecorator
    {
        public int Order => 0;

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => agent;
    }

    private sealed class NamedDecorator(string name, int order) : IAgentDecorator
    {
        public string Name { get; } = name;

        public int Order => order;

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor) => agent;
    }

    private sealed class RecordingDecorator(string name, int order) : IAgentDecorator
    {
        private List<string>? _sink;

        public int Order => order;

        public RecordingDecorator WithSink(List<string> sink)
        {
            _sink = sink;
            return this;
        }

        public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
        {
            _sink?.Add(name);
            return agent;
        }
    }

    private sealed class StubSource(string name, int priority, params string[] agentNames) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<AgentDescriptor> descriptors =
            [
                .. agentNames.Select(agentName => new AgentDescriptor
                {
                    Name = agentName,
                    Origin = AgentDefinitionOrigin.Code,
                    SourceName = Name,
                }),
            ];

            return new ValueTask<IReadOnlyList<AgentDescriptor>>(descriptors);
        }

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
        {
            if (!agentNames.Contains(agentName, StringComparer.Ordinal))
            {
                return new ValueTask<AIAgent?>((AIAgent?)null);
            }

            var compiler = new AgentDefinitionCompiler(
                TestData.Providers(new FakeModelProvider()),
                TestData.Registry());

            return new ValueTask<AIAgent?>(compiler.Compile(TestData.Definition(agentName)));
        }
    }
}
