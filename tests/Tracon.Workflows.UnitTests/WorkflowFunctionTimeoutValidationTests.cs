using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The ranges a node timeout, a definition's run timeout and the host's ceiling accept
/// (F-303, F-405, failure mode 9, the F-280 rule): a timer refuses a value outside one
/// millisecond to about 49.7 days where it is ARMED, which for these values is in the
/// middle of a run. Each value is refused where it is written instead.
/// </summary>
/// <remarks>
/// The HTTP half (<c>PUT</c> → <c>400</c>) is <c>WorkflowTimeoutSaveTests</c> in the
/// functional tests.
/// </remarks>
public sealed class WorkflowFunctionTimeoutValidationTests
{
    private static readonly TimeSpan TooLong = TimeSpan.FromMilliseconds(uint.MaxValue);

    public static TheoryData<TimeSpan> OutOfRange =>
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(-1),
        TimeSpan.FromTicks(1),
        TooLong,
    ];

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void A_function_nodes_timeout_outside_the_timer_range_is_refused(TimeSpan timeout)
    {
        var problem = WorkflowDefinitionValidator.Validate(Chain(new WorkflowNodeReference
        {
            Name = "render",
            Kind = WorkflowNodeKind.Function,
            Timeout = timeout,
        }));

        problem.ShouldNotBeNull();
        problem.ShouldContain("'render'");
        problem.ShouldContain("'timeout'");
    }

    [Fact]
    public void An_agent_nodes_timeout_is_accepted_open_question_9_A()
    {
        // The plan's first draft refused a timeout on an agent node; F-405 made it
        // the agent step's own ChildDeadline.
        WorkflowDefinitionValidator.Validate(Chain(new WorkflowNodeReference
        {
            Name = "writer",
            Kind = WorkflowNodeKind.Agent,
            Timeout = TimeSpan.FromMinutes(15),
        })).ShouldBeNull();
    }

    [Fact]
    public void An_agent_nodes_timeout_leaves_room_for_its_hard_cutoff()
    {
        // The hard cutoff is the timeout plus 30 seconds, and it must fit a timer too.
        var atTheLimit = WorkflowDefinitionValidator.MaxAgentNodeTimeout;

        WorkflowDefinitionValidator.Validate(Chain(new WorkflowNodeReference
        {
            Name = "writer",
            Kind = WorkflowNodeKind.Agent,
            Timeout = atTheLimit,
        })).ShouldBeNull();

        var problem = WorkflowDefinitionValidator.Validate(Chain(new WorkflowNodeReference
        {
            Name = "writer",
            Kind = WorkflowNodeKind.Agent,
            Timeout = atTheLimit + TimeSpan.FromMilliseconds(1),
        }));

        problem.ShouldNotBeNull();
        problem.ShouldContain("hard cutoff");
    }

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void A_definitions_run_timeout_outside_the_timer_range_is_refused(TimeSpan runTimeout)
    {
        var definition = Chain(new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent });
        var problem = WorkflowDefinitionValidator.Validate(definition with { RunTimeout = runTimeout });

        problem.ShouldNotBeNull();
        problem.ShouldContain("'runTimeout'");
    }

    [Fact]
    public void A_node_timeout_is_not_checked_against_the_runs_timeout_open_question_12_B()
    {
        var definition = Chain(new WorkflowNodeReference
        {
            Name = "render",
            Kind = WorkflowNodeKind.Function,
            Timeout = TimeSpan.FromHours(2),
        });

        WorkflowDefinitionValidator.Validate(definition with { RunTimeout = TimeSpan.FromMinutes(5) }).ShouldBeNull();
    }

    [Fact]
    public void Without_a_ceiling_a_definition_may_only_shorten_the_installations_run_timeout()
    {
        var installation = TimeSpan.FromMinutes(10);

        WorkflowDefinitionValidator.RunTimeoutCeilingProblem(WithRunTimeout(TimeSpan.FromMinutes(10)), installation, maxRunTimeout: null)
            .ShouldBeNull();
        WorkflowDefinitionValidator.RunTimeoutCeilingProblem(WithRunTimeout(TimeSpan.FromMinutes(3)), installation, maxRunTimeout: null)
            .ShouldBeNull();

        var problem = WorkflowDefinitionValidator.RunTimeoutCeilingProblem(WithRunTimeout(TimeSpan.FromMinutes(30)), installation, maxRunTimeout: null);

        problem.ShouldNotBeNull();
        problem.ShouldContain("Tracon:Workflows:MaxRunTimeout");
    }

    [Fact]
    public void With_a_ceiling_a_definition_may_extend_up_to_it_and_not_past_it()
    {
        var installation = TimeSpan.FromMinutes(10);
        var ceiling = TimeSpan.FromHours(1);

        WorkflowDefinitionValidator.RunTimeoutCeilingProblem(WithRunTimeout(TimeSpan.FromMinutes(30)), installation, ceiling)
            .ShouldBeNull();
        WorkflowDefinitionValidator.RunTimeoutCeilingProblem(WithRunTimeout(ceiling), installation, ceiling)
            .ShouldBeNull();
        WorkflowDefinitionValidator.RunTimeoutCeilingProblem(WithRunTimeout(ceiling + TimeSpan.FromSeconds(1)), installation, ceiling)
            .ShouldNotBeNull()
            .ShouldContain("01:00:00");
    }

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void A_registrations_timeout_outside_the_timer_range_throws_when_the_function_is_registered(TimeSpan timeout)
    {
        var services = new ServiceCollection();
        var builder = new Builder(services);

        Should.Throw<ArgumentOutOfRangeException>(() => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(
            "render",
            static _ => static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages),
            options => options.Timeout = timeout));

        Should.Throw<ArgumentOutOfRangeException>(() => builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, Settings>(
            "render",
            static _ => static (messages, _, _, _) => new ValueTask<List<ChatMessage>>(messages),
            options => options.Timeout = timeout));
    }

    [Fact]
    public void A_ceiling_below_the_installations_run_timeout_fails_at_startup()
    {
        var result = new TraconWorkflowOptionsValidator().Validate(
            name: null,
            new TraconWorkflowOptions { RunTimeout = TimeSpan.FromMinutes(10), MaxRunTimeout = TimeSpan.FromMinutes(5) });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconWorkflowOptions.MaxRunTimeout));
    }

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void A_ceiling_outside_the_timer_range_fails_at_startup(TimeSpan ceiling)
    {
        var result = new TraconWorkflowOptionsValidator().Validate(
            name: null,
            new TraconWorkflowOptions { RunTimeout = TimeSpan.FromMilliseconds(1), MaxRunTimeout = ceiling });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain(nameof(TraconWorkflowOptions.MaxRunTimeout));
    }

    [Fact]
    public void The_ceiling_is_bound_from_configuration_checked_at_startup_and_read_by_the_save_seam()
    {
        using (var refused = Build(("Tracon:Workflows:RunTimeout", "00:10:00"), ("Tracon:Workflows:MaxRunTimeout", "00:01:00")))
        {
            Should.Throw<OptionsValidationException>(() => refused.GetRequiredService<IStartupValidator>().Validate())
                .Message.ShouldContain(nameof(TraconWorkflowOptions.MaxRunTimeout));
        }

        using var provider = Build(("Tracon:Workflows:MaxRunTimeout", "01:00:00"));

        provider.GetRequiredService<IStartupValidator>().Validate();

        var policy = provider.GetRequiredService<IWorkflowRunTimeoutPolicy>();

        policy.RunTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        policy.MaxRunTimeout.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Without_a_ceiling_the_seam_reports_none()
    {
        using var provider = Build();

        provider.GetRequiredService<IStartupValidator>().Validate();
        provider.GetRequiredService<IWorkflowRunTimeoutPolicy>().MaxRunTimeout.ShouldBeNull();
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

    private static WorkflowDefinition Chain(WorkflowNodeReference node)
        => new() { Name = "chain", Kind = WorkflowKind.Sequential, Nodes = [node] };

    private static WorkflowDefinition WithRunTimeout(TimeSpan runTimeout)
        => Chain(new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent }) with { RunTimeout = runTimeout };

    private sealed class Builder(IServiceCollection services) : ITraconBuilder
    {
        public IServiceCollection Services { get; } = services;
    }

    private sealed record Settings
    {
        public string? Voice { get; init; }
    }
}
