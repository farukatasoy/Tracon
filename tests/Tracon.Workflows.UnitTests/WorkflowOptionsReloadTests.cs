using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Pins what a configuration reload does to the workflow options: nothing, for every reader.
/// </summary>
/// <remarks>
/// <para>
/// Tracon reads its options once, when the host starts, and a change to the configuration needs a restart. The workflow
/// options used to be the exception: <c>UseWorkflows</c> bound the section through the framework's configuration
/// binder, which registers a change token source on its own, so a reload reached the readers that look the value up
/// through <see cref="IOptionsMonitor{TOptions}"/> (the run limits and the recovery policy) and not the runner, which
/// reads <see cref="IOptions{TOptions}"/> and keeps the value it built at start. The save of a workflow checked the new
/// limits while the runner applied the old ones.
/// </para>
/// <para>
/// The section is bound by hand now, like every other, and no reader follows a reload. These tests keep the two
/// readers that used to differ in step: if a change token source comes back, the monitor moves and the options do not,
/// and the first test names which reader.
/// </para>
/// </remarks>
public sealed class WorkflowOptionsReloadTests
{
    [Fact]
    public void A_reload_reaches_no_reader()
    {
        var (provider, configuration) = Compose();
        using var host = provider;

        var limits = provider.GetRequiredService<IWorkflowRunLimits>();
        var policy = provider.GetRequiredService<IWorkflowRecoveryPolicy>();
        var options = provider.GetRequiredService<IOptions<TraconWorkflowOptions>>();
        var monitor = provider.GetRequiredService<IOptionsMonitor<TraconWorkflowOptions>>();

        limits.MaxSuperSteps.ShouldBe(100);
        limits.RunTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        limits.MaxRunTimeout.ShouldBeNull();
        policy.ResumeInterruptedRuns.ShouldBeFalse();
        policy.MaxResumeAttempts.ShouldBe(1);
        options.Value.MaxSuperSteps.ShouldBe(100);
        monitor.CurrentValue.MaxSuperSteps.ShouldBe(100);

        configuration["Tracon:Workflows:MaxSuperSteps"] = "7";
        configuration["Tracon:Workflows:RunTimeout"] = "00:05:00";
        configuration["Tracon:Workflows:MaxRunTimeout"] = "01:00:00";
        configuration["Tracon:Workflows:ResumeInterruptedRuns"] = "true";
        configuration["Tracon:Workflows:MaxResumeAttempts"] = "3";
        configuration.Reload();

        limits.MaxSuperSteps.ShouldBe(100, "the run limits read the monitor, which has no change token source to follow");
        limits.RunTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        limits.MaxRunTimeout.ShouldBeNull();
        policy.ResumeInterruptedRuns.ShouldBeFalse();
        policy.MaxResumeAttempts.ShouldBe(1);
        monitor.CurrentValue.MaxSuperSteps.ShouldBe(100, "the monitor keeps the value it built at start");
        options.Value.MaxSuperSteps.ShouldBe(100, "IOptions<T> keeps the value it built first; the runner reads it");
    }

    [Fact]
    public void A_reloaded_value_that_would_fail_validation_throws_nowhere()
    {
        var (provider, configuration) = Compose();
        using var host = provider;

        var limits = provider.GetRequiredService<IWorkflowRunLimits>();

        limits.MaxSuperSteps.ShouldBe(100);

        configuration["Tracon:Workflows:MaxSuperSteps"] = "0";

        // Nothing recomputes the options, so the reload does not throw and no later read does either.
        Should.NotThrow(configuration.Reload);
        Should.NotThrow(() => _ = limits.MaxSuperSteps);
        limits.MaxSuperSteps.ShouldBe(100);
    }

    [Fact]
    public void No_change_token_source_is_registered_for_the_workflow_options()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTracon().UseWorkflows();

        services
            .Where(static descriptor => descriptor.ServiceType == typeof(IOptionsChangeTokenSource<TraconWorkflowOptions>))
            .ShouldBeEmpty("a source would make a reload reach the monitor and not the runner again");
    }

    private static (ServiceProvider Provider, IConfigurationRoot Configuration) Compose()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseWorkflows();

        return (services.BuildServiceProvider(), configuration);
    }
}
