using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The settings of the request limit (phase 209): how long a request may be given, how often the
/// expiry looks, and the names the new list route takes.
/// </summary>
/// <remarks>
/// The expiry scan runs on a <see cref="PeriodicTimer"/>, which refuses a period outside one
/// millisecond to about 49.7 days from inside a hosted service, where the default host turns it
/// into a stopped host after "Application started" (F-278). The interval is therefore checked at
/// startup like every other background interval.
/// </remarks>
public sealed class WorkflowRequestTimeoutValidationTests
{
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("50.00:00:00")]
    public void A_scan_interval_the_timer_cannot_take_is_checked_at_startup(string value)
    {
        using var provider = Build(("Tracon:Workflows:RequestExpirationScanInterval", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconWorkflowOptions.RequestExpirationScanInterval));
    }

    [Fact]
    public void The_scan_interval_is_not_checked_while_workflows_are_off()
    {
        using var provider = Build(
            ("Tracon:Workflows:Enabled", "false"),
            ("Tracon:Workflows:RequestExpirationScanInterval", "00:00:00"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void A_round_must_close_at_least_one_request(string value)
    {
        using var provider = Build(("Tracon:Workflows:MaxRequestExpirationsPerScan", value));

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate())
            .Message.ShouldContain(nameof(TraconWorkflowOptions.MaxRequestExpirationsPerScan));
    }

    [Fact]
    public void The_defaults_start_and_the_service_is_registered_with_the_engine()
    {
        using var provider = Build();

        provider.GetRequiredService<IStartupValidator>().Validate();

        var options = provider.GetRequiredService<IOptions<TraconWorkflowOptions>>().Value;

        options.RequestExpirationScanInterval.ShouldBe(TimeSpan.FromMinutes(1));
        options.MaxRequestExpirationsPerScan.ShouldBe(100);

        provider.GetServices<IHostedService>().ShouldContain(static service => service is WorkflowRequestExpirationService);
    }

    [Theory]
    [InlineData("00:00:30", false)]
    [InlineData("00:00:59", false)]
    [InlineData("00:01:00", true)]
    [InlineData("1.00:00:00", true)]
    [InlineData("365.00:00:00", true)]
    [InlineData("365.00:00:01", false)]
    [InlineData("00:00:00", false)]
    [InlineData("-1.00:00:00", false)]
    public void A_definition_may_give_a_request_from_one_minute_to_365_days(string value, bool accepted)
    {
        var problem = WorkflowDefinitionValidator.Validate(new WorkflowDefinition
        {
            Name = "approval",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer"],
            RequestTimeout = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture),
        });

        if (accepted)
        {
            problem.ShouldBeNull();
        }
        else
        {
            problem.ShouldNotBeNull().ShouldContain("requestTimeout", Case.Sensitive);
        }
    }

    [Fact]
    public void A_definition_without_a_limit_is_valid()
        => WorkflowDefinitionValidator.Validate(new WorkflowDefinition
        {
            Name = "approval",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer"],
        }).ShouldBeNull();

    [Fact]
    public void A_code_registration_carries_its_limit_and_refuses_one_outside_the_range()
    {
        var services = new ServiceCollection();
        var builder = new TestBuilder(services);

        builder.AddWorkflow("approval", static _ => ApprovalWorkflow.Build(), static options =>
            options.RequestTimeout = TimeSpan.FromDays(7));

        services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<CodeWorkflowRegistration>()
            .ShouldHaveSingleItem()
            .RequestTimeout.ShouldBe(TimeSpan.FromDays(7));

        Should.Throw<ArgumentException>(() => builder.AddWorkflow(
            "too-short", static _ => ApprovalWorkflow.Build(), static options => options.RequestTimeout = TimeSpan.FromSeconds(10)));
        Should.Throw<ArgumentException>(() => builder.AddWorkflow(
            "too-long", static _ => ApprovalWorkflow.Build(), static options => options.RequestTimeout = TimeSpan.FromDays(366)));
    }

    [Fact]
    public async Task The_catalog_describes_a_workflow_with_its_limit()
    {
        var host = new WorkflowTestHost();

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "stored",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer"],
            RequestTimeout = TimeSpan.FromHours(6),
        });

        var runner = host.CreateRunner(
            configure: null,
            services: null,
            ApprovalWorkflow.Registration() with { RequestTimeout = TimeSpan.FromDays(2) });

        (await runner.GetAsync("approval-flow")).ShouldNotBeNull().RequestTimeout.ShouldBe(TimeSpan.FromDays(2));
        (await runner.GetAsync("stored")).ShouldNotBeNull().RequestTimeout.ShouldBe(TimeSpan.FromHours(6));
        (await runner.ListAsync()).Single(static workflow => string.Equals(workflow.Name, "stored", StringComparison.Ordinal))
            .RequestTimeout.ShouldBe(TimeSpan.FromHours(6));
    }

    [Theory]
    [InlineData("requests")]
    [InlineData("Requests")]
    [InlineData("REQUESTS")]
    [InlineData("functions")]
    public void The_names_of_the_two_fixed_list_routes_cannot_be_a_workflow_name(string name)
    {
        // GET /api/workflows/requests and /functions match before /api/workflows/{name}, so a
        // workflow saved under either could never be read back.
        WorkflowDefinitionValidator.ReservedNameProblem(name).ShouldNotBeNull().ShouldContain("reserved", Case.Sensitive);

        Should.Throw<ArgumentException>(() => new TestBuilder(new ServiceCollection())
            .AddWorkflow(name, static _ => ApprovalWorkflow.Build()));
    }

    [Fact]
    public void A_name_that_only_starts_like_a_fixed_route_is_free()
    {
        WorkflowDefinitionValidator.ReservedNameProblem("requests-review").ShouldBeNull();
        WorkflowDefinitionValidator.ReservedNameProblem("my-requests").ShouldBeNull();
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

    private sealed class TestBuilder(IServiceCollection services) : ITraconBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
