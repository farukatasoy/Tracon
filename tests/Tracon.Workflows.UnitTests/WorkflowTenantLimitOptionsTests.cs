using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Phase 240: <see cref="TraconWorkflowOptions.MaxConcurrentRunsPerTenant"/> is off by default, is a share of
/// <see cref="TraconWorkflowOptions.MaxConcurrentRuns"/>, and reaches the job worker through the seam
/// <c>UseWorkflows</c> registers.
/// </summary>
/// <remarks>
/// A value the engine cannot honour is refused at startup: a limit above the shared one limits nothing,
/// and one below one would hold every run of every tenant back for good. The seam cases pin the other
/// half: the worker leases under a ceiling only when the setting is on, over the two handler keys that
/// carry a workflow run.
/// </remarks>
public sealed class WorkflowTenantLimitOptionsTests
{
    [Fact]
    public void The_tenant_limit_is_off_by_default_and_the_worker_leases_with_no_ceiling()
    {
        using var provider = Build();

        provider.GetRequiredService<IStartupValidator>().Validate();

        provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value.MaxConcurrentRunsPerTenant.ShouldBeNull();
        provider.GetRequiredService<IJobTenantCapSource>().Current.ShouldBeNull();
    }

    [Fact]
    public void A_configured_limit_becomes_a_ceiling_over_both_workflow_handler_keys()
    {
        using var provider = Build(("Tracon:Workflows:MaxConcurrentRunsPerTenant", "2"));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var cap = provider.GetRequiredService<IJobTenantCapSource>().Current.ShouldNotBeNull();

        cap.MaxLeasedPerTenant.ShouldBe(2);
        cap.HandlerKeys.ShouldBe([JobHandlerKeys.Workflow, JobHandlerKeys.WorkflowRun], ignoreOrder: true);
    }

    [Fact]
    public void The_limit_may_equal_the_shared_limit()
    {
        using var provider = Build(
            ("Tracon:Workflows:MaxConcurrentRuns", "6"),
            ("Tracon:Workflows:MaxConcurrentRunsPerTenant", "6"));

        provider.GetRequiredService<IStartupValidator>().Validate();

        provider.GetRequiredService<IJobTenantCapSource>().Current.ShouldNotBeNull().MaxLeasedPerTenant.ShouldBe(6);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("5")]
    public void A_limit_below_one_or_above_the_shared_limit_fails_at_startup(string value)
    {
        using var provider = Build(
            ("Tracon:Workflows:MaxConcurrentRuns", "4"),
            ("Tracon:Workflows:MaxConcurrentRunsPerTenant", value));

        var message = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate()).Message;

        message.ShouldContain(nameof(TraconWorkflowOptions.MaxConcurrentRunsPerTenant));
        message.ShouldContain(nameof(TraconWorkflowOptions.MaxConcurrentRuns));
    }

    [Fact]
    public void A_value_that_is_not_a_number_fails_at_startup_instead_of_being_ignored()
    {
        using var provider = Build(("Tracon:Workflows:MaxConcurrentRunsPerTenant", "plenty"));

        Should.Throw<Exception>(() => provider.GetRequiredService<IStartupValidator>().Validate());
    }

    [Fact]
    public void With_the_engine_off_the_worker_leases_with_no_ceiling()
    {
        using var provider = Build(
            ("Tracon:Workflows:Enabled", "false"),
            ("Tracon:Workflows:MaxConcurrentRunsPerTenant", "2"));

        provider.GetRequiredService<IJobTenantCapSource>().Current.ShouldBeNull();
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
