using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// What a workflow defined in code may declare about its run time and its parameters (phase 211): a registration is
/// checked when it is made, and its run timeout is held to the host's ceiling when the host starts.
/// </summary>
/// <remarks>
/// <para>
/// A stored definition meets the ceiling at its save, which is an HTTP call someone can answer with a <c>400</c>. A
/// registration is written at build time and meets the host's settings only at startup, so the startup validator is
/// where a value the deployment cannot honour stops the host - instead of cutting every run in silence.
/// </para>
/// </remarks>
public sealed class CodeWorkflowRegistrationValidationTests
{
    [Fact]
    public void A_run_timeout_above_the_installations_value_stops_the_host_and_names_the_workflow()
    {
        using var provider = Build(
            static options => options.RunTimeout = TimeSpan.FromHours(2),
            ("Tracon:Workflows:RunTimeout", "00:10:00"));

        var message = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate()).Message;

        message.ShouldContain("Workflow 'long-job'", Case.Sensitive);
        message.ShouldContain(nameof(WorkflowRegistrationOptions.RunTimeout), Case.Sensitive);
        message.ShouldContain("Tracon:Workflows:MaxRunTimeout", Case.Sensitive);
    }

    [Fact]
    public void A_run_timeout_within_the_ceiling_the_host_set_starts()
    {
        using var provider = Build(
            static options => options.RunTimeout = TimeSpan.FromHours(2),
            ("Tracon:Workflows:RunTimeout", "00:10:00"),
            ("Tracon:Workflows:MaxRunTimeout", "03:00:00"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void A_run_timeout_below_the_installations_value_starts()
    {
        using var provider = Build(
            static options => options.RunTimeout = TimeSpan.FromMinutes(1),
            ("Tracon:Workflows:RunTimeout", "00:10:00"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void A_run_timeout_above_the_ceiling_is_not_checked_while_workflows_are_off()
    {
        using var provider = Build(
            static options => options.RunTimeout = TimeSpan.FromHours(2),
            ("Tracon:Workflows:Enabled", "false"));

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Every_workflow_over_the_ceiling_is_named()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddTracon().UseWorkflows()
            .AddWorkflow("first", static _ => Graph("first"), static options => options.RunTimeout = TimeSpan.FromHours(2))
            .AddWorkflow("second", static _ => Graph("second"), static options => options.RunTimeout = TimeSpan.FromHours(3));

        using var provider = services.BuildServiceProvider();

        var message = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate()).Message;

        message.ShouldContain("Workflow 'first'", Case.Sensitive);
        message.ShouldContain("Workflow 'second'", Case.Sensitive);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("50.00:00:00")]
    public void A_run_timeout_the_timer_cannot_take_is_refused_when_the_workflow_is_registered(string value)
    {
        var services = new ServiceCollection();
        var builder = services.AddTracon().UseWorkflows();

        var problem = Should.Throw<ArgumentException>(
            () => builder.AddWorkflow("job", static _ => Graph("job"), options => options.RunTimeout = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));

        problem.Message.ShouldContain("Workflow 'job' has a run timeout of", Case.Sensitive);
    }

    [Fact]
    public void A_parameter_name_that_is_not_an_identifier_is_refused_when_the_workflow_is_registered()
    {
        var builder = new ServiceCollection().AddTracon().UseWorkflows();

        var problem = Should.Throw<ArgumentException>(
            () => builder.AddWorkflow(
                "job",
                static _ => Graph("job"),
                static options => options.Parameters = [new AgentParameter { Name = "production id", Kind = AgentParameterKind.Text }]));

        problem.Message.ShouldContain("Workflow 'job' has an invalid parameter schema", Case.Sensitive);
        problem.Message.ShouldContain("'production id'", Case.Sensitive);
    }

    [Fact]
    public void A_parameter_name_used_twice_is_refused_when_the_workflow_is_registered()
    {
        var builder = new ServiceCollection().AddTracon().UseWorkflows();

        var problem = Should.Throw<ArgumentException>(
            () => builder.AddWorkflow(
                "job",
                static _ => Graph("job"),
                static options => options.Parameters = [new AgentParameter { Name = "id", Kind = AgentParameterKind.Text }, new AgentParameter { Name = "id", Kind = AgentParameterKind.Text }]));

        problem.Message.ShouldContain("invalid parameter schema", Case.Sensitive);
    }

    [Fact]
    public void The_registration_keeps_a_copy_of_the_parameters_it_was_given()
    {
        var declared = new List<AgentParameter> { new() { Name = "productionId", Kind = AgentParameterKind.Text } };
        var services = new ServiceCollection();

        services.AddTracon().UseWorkflows().AddWorkflow("job", static _ => Graph("job"), options => options.Parameters = declared);
        declared.Add(new AgentParameter { Name = "later", Kind = AgentParameterKind.Text });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<CodeWorkflowRegistration>().Single().Parameters.ShouldNotBeNull().Select(static parameter => parameter.Name).ShouldBe(["productionId"]);
    }

    private static ServiceProvider Build(Action<WorkflowRegistrationOptions> configure, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddTracon().UseWorkflows().AddWorkflow("long-job", static _ => Graph("long-job"), configure);

        return services.BuildServiceProvider();
    }

    private static Workflow Graph(string name)
    {
        var step = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(static _ => "done", id: "step");

        return new WorkflowBuilder(step).WithOutputFrom(step).WithName(name).Build();
    }
}
