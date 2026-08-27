using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// <c>AddModelProvider&lt;T&gt;()</c> (🟢 BL-016, phase 122) fills the last gap in the
/// generic/instance/factory triple every other multi-registration seam already had
/// (<c>AddAgentSource</c>, <c>AddRunJudge</c>). The instance and factory overloads
/// predate this phase and are exercised by dozens of functional tests already; this
/// covers only the new generic overload's DI contract.
/// </summary>
public sealed class ModelProviderRegistrationTests
{
    [Fact]
    public void Generic_registration_is_singleton_and_idempotent()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddModelProvider<CountingProvider>()
            .AddModelProvider<CountingProvider>();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetServices<IModelProvider>().OfType<CountingProvider>().ShouldHaveSingleItem();
        var second = provider.GetServices<IModelProvider>().OfType<CountingProvider>().ShouldHaveSingleItem();

        ReferenceEquals(first, second).ShouldBeTrue();
    }

    [Fact]
    public void Generic_registration_coexists_with_an_instance_registration()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddModelProvider<CountingProvider>()
            .AddModelProvider(new FakeModelProvider());
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IModelProvider>().OfType<CountingProvider>().ShouldHaveSingleItem();
        provider.GetServices<IModelProvider>().OfType<FakeModelProvider>().ShouldHaveSingleItem();
    }

    private sealed class CountingProvider : IModelProvider
    {
        public string Name => "counting";

        public IReadOnlyList<ModelDescriptor> Models { get; } = [];

        public Microsoft.Extensions.AI.IChatClient CreateChatClient(ModelBinding binding)
            => throw new NotSupportedException("Not used by this test.");
    }
}
