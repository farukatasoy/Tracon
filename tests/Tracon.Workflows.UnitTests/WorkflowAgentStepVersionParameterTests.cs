using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// An agent step of a graph built in code that reads its version from a run parameter (
/// <see cref="WorkflowAgentStepOptions.AgentVersionParameter"/>): the two ways to choose a version exclude each
/// other, and the cache holds the parameter's name, never a run's value.
/// </summary>
public sealed class WorkflowAgentStepVersionParameterTests
{
    private const string Flow = "episode";
    private const string Parameter = "writerVersion";

    [Fact]
    public void A_fixed_version_and_a_version_parameter_together_are_refused_when_the_graph_is_built()
    {
        var services = BuildServices(new WorkflowTestHost("one"));

        var failure = Should.Throw<ArgumentException>(() => services.GetWorkflowAgentStep(
            Flow,
            "one",
            static step =>
            {
                step.AgentVersion = 2;
                step.AgentVersionParameter = Parameter;
            }));

        failure.GetType().ShouldBe(typeof(ArgumentException));
        failure.Message.ShouldContain("'one'", Case.Sensitive);
        failure.Message.ShouldContain($"'{Flow}'", Case.Sensitive);
        failure.Message.ShouldContain("not both", Case.Sensitive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_version_parameter_with_no_name_is_refused_when_the_graph_is_built(string name)
    {
        var services = BuildServices(new WorkflowTestHost("one"));

        Should.Throw<ArgumentException>(() => services.GetWorkflowAgentStep(
                Flow,
                "one",
                step => step.AgentVersionParameter = name))
            .Message.ShouldContain("is empty", Case.Sensitive);
    }

    [Fact]
    public void The_cache_key_holds_the_name_of_the_parameter_and_never_a_value_so_every_run_shares_one_wrapper()
    {
        var host = new WorkflowTestHost("one");

        var current = host.AgentCache.Get("wf", "one", null);
        var first = host.AgentCache.Get("wf", "one", null, agentVersionParameter: Parameter);
        var other = host.AgentCache.Get("wf", "one", null, agentVersionParameter: "otherVersion");

        // The same step built again (the factory runs on every run) is the same wrapper, whatever version the run asks for.
        host.AgentCache.Get("wf", "one", null, agentVersionParameter: Parameter).ShouldBeSameAs(first);

        first.ShouldNotBeSameAs(current);
        first.ShouldNotBeSameAs(other);

        // Like the timeout and the fixed version, it never reaches the identity: a waiting run's checkpoint stays readable.
        first.Id.ShouldBe(current.Id);
        other.Id.ShouldBe(current.Id);
    }

    [Fact]
    public async Task Each_run_asks_the_catalog_for_the_version_its_own_value_names()
    {
        var host = new WorkflowTestHost("one");

        host.AddAgentVersion("one", 1, new EchoAgent("one"));
        host.AddAgentVersion("one", 2, new EchoAgent("one"));

        var runner = host.CreateRunner(configure: null, BuildServices(host), Registration());

        await RunAsync(runner, Values((Parameter, "1")));
        await RunAsync(runner, Values((Parameter, "2")));
        await RunAsync(runner, null);

        host.ResolveCalls.ToArray().ShouldBe([("one", (int?)1), ("one", (int?)2), ("one", (int?)null)]);

        var versions = (await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Where(static run => run.Kind == RunKind.Agent)
            .OrderBy(static run => run.StartedAt)
            .Select(static run => run.AgentVersion)
            .ToList();

        versions.Take(2).ShouldBe([(int?)1, (int?)2]);
    }

    private static CodeWorkflowRegistration Registration()
        => new(
            Flow,
            "A step whose version a run chooses.",
            static services =>
            {
                var step = services.GetWorkflowAgentStep(Flow, "one", static options => options.AgentVersionParameter = Parameter);

                return new WorkflowBuilder(step).WithOutputFrom(step).WithName(Flow).Build();
            },
            Parameters: [new AgentParameter { Name = Parameter, Kind = AgentParameterKind.Number }]);

    private static async Task RunAsync(WorkflowRunner runner, Dictionary<string, string>? parameters)
    {
        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = Flow,
            Message = "go",
            Parameters = parameters,
        }))
        {
            // The events are not the subject: the catalog calls and the child rows are.
        }
    }

    private static Dictionary<string, string> Values(params (string Name, string Value)[] values)
        => values.ToDictionary(static value => value.Name, static value => value.Value, StringComparer.Ordinal);

    private static ServiceProvider BuildServices(WorkflowTestHost host)
        => new ServiceCollection()
            .AddSingleton(host.Resolver)
            .AddSingleton(host.AgentCache)
            .AddSingleton<ITenantContext>(host.TenantContext)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
}
