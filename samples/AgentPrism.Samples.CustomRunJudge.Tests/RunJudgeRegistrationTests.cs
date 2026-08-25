using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Samples.CustomRunJudge.Tests;

/// <summary>Verifies all three public judge registration paths at the DI boundary.</summary>
public sealed class RunJudgeRegistrationTests
{
    [Fact]
    public void Generic_registration_is_singleton_and_duplicate_safe()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism()
            .AddRunJudge<ResponseQualityJudge>()
            .AddRunJudge<ResponseQualityJudge>();

        using var provider = services.BuildServiceProvider();
        var judges = provider.GetServices<IRunJudge>().ToArray();

        judges.ShouldHaveSingleItem();
        provider.GetServices<IRunJudge>().Single().ShouldBeSameAs(judges[0]);
    }

    [Fact]
    public void Instance_registration_returns_the_supplied_instance()
    {
        var judge = new ResponseQualityJudge();
        var services = new ServiceCollection();
        services.AddAgentPrism().AddRunJudge(judge);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRunJudge>().ShouldBeSameAs(judge);
    }

    [Fact]
    public void Factory_registration_runs_once_for_the_singleton()
    {
        var calls = 0;
        var services = new ServiceCollection();
        services.AddAgentPrism().AddRunJudge(_ =>
        {
            calls++;
            return new ResponseQualityJudge();
        });

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IRunJudge>();
        var second = provider.GetRequiredService<IRunJudge>();

        second.ShouldBeSameAs(first);
        calls.ShouldBe(1);
    }
}
