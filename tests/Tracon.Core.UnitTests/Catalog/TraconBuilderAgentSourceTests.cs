using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Catalog;

/// <summary>101.8: the three <c>AddAgentSource</c> registration overloads.</summary>
public sealed class TraconBuilderAgentSourceTests
{
    [Fact]
    public void AddAgentSource_of_T_registers_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddAgentSource<CountingSource>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetServices<IAgentSource>().OfType<CountingSource>().ShouldHaveSingleItem();
        var second = provider.GetServices<IAgentSource>().OfType<CountingSource>().ShouldHaveSingleItem();

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void AddAgentSource_of_T_registered_twice_does_not_duplicate_the_type()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddAgentSource<CountingSource>()
            .AddAgentSource<CountingSource>();
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IAgentSource>().OfType<CountingSource>().Count().ShouldBe(1);
    }

    [Fact]
    public void AddAgentSource_by_instance_allows_two_configurations_of_the_same_type()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddAgentSource(new NamedSource("first"))
            .AddAgentSource(new NamedSource("second"));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IAgentSource>().OfType<NamedSource>()
            .Select(static source => source.Name)
            .ShouldBe(["first", "second"], ignoreOrder: true);
    }

    [Fact]
    public void AddAgentSource_by_instance_is_the_same_instance_every_resolve()
    {
        var instance = new NamedSource("only");
        var services = new ServiceCollection();
        services.AddTracon().AddAgentSource(instance);
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetServices<IAgentSource>().OfType<NamedSource>().ShouldHaveSingleItem();

        ReferenceEquals(instance, resolved).ShouldBeTrue();
    }

    [Fact]
    public void AddAgentSource_by_factory_runs_the_factory_once_across_repeated_resolves()
    {
        var factoryCalls = 0;
        var services = new ServiceCollection();
        services.AddTracon().AddAgentSource(_ =>
        {
            factoryCalls++;
            return new NamedSource("factory-made");
        });
        using var provider = services.BuildServiceProvider();

        var first = provider.GetServices<IAgentSource>().OfType<NamedSource>().ShouldHaveSingleItem();
        var second = provider.GetServices<IAgentSource>().OfType<NamedSource>().ShouldHaveSingleItem();

        factoryCalls.ShouldBe(1);
        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void AddAgentSource_by_factory_allows_two_configurations_of_the_same_type()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddAgentSource(_ => new NamedSource("first"))
            .AddAgentSource(_ => new NamedSource("second"));
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IAgentSource>().OfType<NamedSource>()
            .Select(static source => source.Name)
            .ShouldBe(["first", "second"], ignoreOrder: true);
    }

    private sealed class CountingSource : IAgentSource
    {
        public string Name => "counting";

        public int Priority => 1;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }

    private sealed class NamedSource(string name) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority => 1;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)[]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }
}
