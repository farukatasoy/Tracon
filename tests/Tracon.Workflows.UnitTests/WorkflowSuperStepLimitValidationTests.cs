using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="TraconWorkflowOptions.MaxSuperSteps"/> must fail at startup when no run could take a single step.
/// </summary>
/// <remarks>
/// The runner counts the super-steps of a run and stops it when the count passes the limit. With a limit of zero or
/// less every run stops at its first step with a message about a loop that does not exist, and the configuration is
/// never named. Found by the configuration permutation probe, which writes <c>0</c> and <c>-1</c> into every key.
/// </remarks>
public sealed class WorkflowSuperStepLimitValidationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_super_step_limit_below_one_is_checked_at_startup(string value)
    {
        using var provider = Build(("Tracon:Workflows:MaxSuperSteps", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconWorkflowOptions.MaxSuperSteps));
    }

    [Fact]
    public void The_super_step_limit_is_not_checked_while_workflows_are_off()
    {
        using var provider = Build(("Tracon:Workflows:Enabled", "false"), ("Tracon:Workflows:MaxSuperSteps", "0"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void One_super_step_is_the_smallest_limit_that_starts()
    {
        using var provider = Build(("Tracon:Workflows:MaxSuperSteps", "1"));

        provider.GetRequiredService<IStartupValidator>().Validate();
        provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value.MaxSuperSteps.ShouldBe(1);
    }

    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseWorkflows();

        return services.BuildServiceProvider();
    }
}
