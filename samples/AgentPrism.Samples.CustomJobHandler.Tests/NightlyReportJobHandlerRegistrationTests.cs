using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Samples.CustomJobHandler.Tests;

/// <summary>Verifies the sample's public registration path at the DI boundary.</summary>
public sealed class NightlyReportJobHandlerRegistrationTests
{
    [Fact]
    public void Registration_is_singleton_and_duplicate_safe()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddNightlyReportJobHandler()
            .AddNightlyReportJobHandler();

        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IJobHandler>()
            .Where(static handler => handler is NightlyReportJobHandler)
            .ToArray();

        handlers.ShouldHaveSingleItem();
        handlers[0].Kind.ShouldBe(JobKind.AgentBatch);
    }
}
