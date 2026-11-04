using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Pins what a configuration reload does to the workflow options: the one set of options in Tracon that follows it,
/// and the readers that do not.
/// </summary>
/// <remarks>
/// <para>
/// <c>UseWorkflows</c> binds <c>Tracon:Workflows</c> through the framework's configuration binder, which registers a
/// change token source on its own. A configuration provider that reloads (a file with <c>reloadOnChange</c>, a vault, a
/// configuration server) therefore reaches <see cref="IOptionsMonitor{TOptions}"/>, and the readers that look the value
/// up there on every call see the new value: the run limits and the recovery policy the HTTP layer and the job handlers
/// use. A reader that takes <see cref="IOptions{TOptions}"/> built its value once and keeps it: the runner, which applies
/// the run timeout, the super-step limit and the concurrency, and the tenant slot cap.
/// </para>
/// <para>
/// So a reload splits the settings: the save of a workflow checks the new limits, and the runner keeps applying the old
/// ones until the host restarts. The documentation says that a change needs a restart, and names these options as the
/// exception; the day the runner reads the monitor too, this class changes with it.
/// </para>
/// </remarks>
public sealed class WorkflowOptionsReloadTests
{
    [Fact]
    public void A_reload_reaches_the_run_limits_and_the_recovery_policy()
    {
        var (provider, configuration) = Compose();
        using var host = provider;

        var limits = provider.GetRequiredService<IWorkflowRunLimits>();
        var policy = provider.GetRequiredService<IWorkflowRecoveryPolicy>();

        limits.MaxSuperSteps.ShouldBe(100);
        limits.RunTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        limits.MaxRunTimeout.ShouldBeNull();
        policy.ResumeInterruptedRuns.ShouldBeFalse();
        policy.MaxResumeAttempts.ShouldBe(1);

        configuration["Tracon:Workflows:MaxSuperSteps"] = "7";
        configuration["Tracon:Workflows:RunTimeout"] = "00:05:00";
        configuration["Tracon:Workflows:MaxRunTimeout"] = "01:00:00";
        configuration["Tracon:Workflows:ResumeInterruptedRuns"] = "true";
        configuration["Tracon:Workflows:MaxResumeAttempts"] = "3";
        configuration.Reload();

        limits.MaxSuperSteps.ShouldBe(7);
        limits.RunTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        limits.MaxRunTimeout.ShouldBe(TimeSpan.FromHours(1));
        policy.ResumeInterruptedRuns.ShouldBeTrue();
        policy.MaxResumeAttempts.ShouldBe(3);
    }

    [Fact]
    public void A_reload_does_not_reach_the_readers_that_took_the_options_once()
    {
        var (provider, configuration) = Compose();
        using var host = provider;

        // IOptions<T> builds its value on the first read, as the runner does when the host starts.
        var options = provider.GetRequiredService<IOptions<TraconWorkflowOptions>>();
        var monitor = provider.GetRequiredService<IOptionsMonitor<TraconWorkflowOptions>>();

        options.Value.MaxSuperSteps.ShouldBe(100);
        monitor.CurrentValue.MaxSuperSteps.ShouldBe(100);

        configuration["Tracon:Workflows:MaxSuperSteps"] = "7";
        configuration.Reload();

        monitor.CurrentValue.MaxSuperSteps.ShouldBe(7, "the monitor follows the reload");
        options.Value.MaxSuperSteps.ShouldBe(100, "IOptions<T> keeps the value it built first; the runner reads it");
    }

    [Fact]
    public void A_reloaded_value_that_fails_validation_makes_the_run_limits_throw()
    {
        var (provider, configuration) = Compose();
        using var host = provider;

        var limits = provider.GetRequiredService<IWorkflowRunLimits>();

        limits.MaxSuperSteps.ShouldBe(100);

        configuration["Tracon:Workflows:MaxSuperSteps"] = "0";

        // The monitor recomputes on the token and the new value fails validation, so the reload itself throws ...
        Should.Throw<AggregateException>(configuration.Reload);

        // ... and so does every later read, until a good value replaces it.
        Should.Throw<OptionsValidationException>(() => _ = limits.MaxSuperSteps);
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
