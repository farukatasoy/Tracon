using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="TraconWorkflowOptions.MaxNodeSettingsLength"/> must fail at startup when no
/// settings could ever fit under it.
/// </summary>
/// <remarks>
/// A limit of zero or less would refuse every function node that carries settings with a
/// message that blames the operator, and nothing would say the configuration is the cause.
/// The test goes through <c>UseWorkflows</c> to <see cref="IStartupValidator"/>, like
/// <see cref="WorkflowRunTimeoutValidationTests"/>.
/// </remarks>
public sealed class WorkflowNodeSettingsLimitValidationTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_settings_limit_below_one_byte_is_checked_at_startup(string value)
    {
        using var provider = Build(("Tracon:Workflows:MaxNodeSettingsLength", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconWorkflowOptions.MaxNodeSettingsLength));
    }

    [Fact]
    public void The_settings_limit_is_checked_while_workflows_are_off_because_a_save_does_not_need_the_engine()
    {
        using var provider = Build(("Tracon:Workflows:Enabled", "false"), ("Tracon:Workflows:MaxNodeSettingsLength", "0"));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconWorkflowOptions.MaxNodeSettingsLength));
    }

    [Fact]
    public void One_byte_is_the_smallest_limit_that_starts_and_the_default_is_sixteen_kibibytes()
    {
        using var smallest = Build(("Tracon:Workflows:MaxNodeSettingsLength", "1"));

        smallest.GetRequiredService<IStartupValidator>().Validate();
        smallest.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value.MaxNodeSettingsLength.ShouldBe(1);

        using var defaults = Build();

        defaults.GetRequiredService<IStartupValidator>().Validate();
        defaults.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value.MaxNodeSettingsLength.ShouldBe(16 * 1024);
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
