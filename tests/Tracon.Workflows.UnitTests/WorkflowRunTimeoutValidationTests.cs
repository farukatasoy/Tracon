using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="TraconWorkflowOptions.RunTimeout"/> must fail at startup when the run's
/// deadline cannot take it (F-280).
/// </summary>
/// <remarks>
/// Every run arms the value with a <see cref="CancellationTokenSource"/>, which refuses
/// a negative value or more than about 49.7 days; out of range, every run threw when it
/// started. The options had no validator. The test goes through <c>UseWorkflows</c> to
/// <see cref="IStartupValidator"/>.
/// </remarks>
public sealed class WorkflowRunTimeoutValidationTests
{
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("50.00:00:00")]
    public void A_run_timeout_the_deadline_cannot_take_is_checked_at_startup(string value)
    {
        using var provider = Build(("Tracon:Workflows:RunTimeout", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconWorkflowOptions.RunTimeout));
    }

    [Fact]
    public void The_run_timeout_is_not_checked_while_workflows_are_off()
    {
        using var provider = Build(("Tracon:Workflows:Enabled", "false"), ("Tracon:Workflows:RunTimeout", "50.00:00:00"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void The_default_run_timeout_starts()
    {
        using var provider = Build();

        provider.GetRequiredService<IStartupValidator>().Validate();
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
