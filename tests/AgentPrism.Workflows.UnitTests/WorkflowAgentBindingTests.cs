using AgentPrism.Workflows.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Agent binding for workflows defined in code.
/// </summary>
/// <remarks>
/// This test exists because of a measured defect: in the sample app, agents were
/// taken DIRECTLY from the catalog and each one opened its own root <c>runs</c>
/// row; the workflow tree returned three rows instead of one. The same silent
/// breakage repeats the moment the wrapping is forgotten.
/// </remarks>
public sealed class WorkflowAgentBindingTests
{
    [Fact]
    public async Task Bound_agents_enter_the_workflow_tree()
    {
        var host = new WorkflowTestHost("summarizer", "translator");
        var services = BuildServices(host);

        var runner = host.CreateRunner(
            configure: null,
            services,
            new CodeWorkflowRegistration(
                "summarize-and-translate",
                "Chain defined in code.",
                provider => AgentWorkflowBuilder.BuildSequential(
                    "summarize-and-translate",
                    [
                        provider.GetWorkflowAgent("summarize-and-translate", "summarizer"),
                        provider.GetWorkflowAgent("summarize-and-translate", "translator"),
                    ])));

        await foreach (var _ in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "summarize-and-translate",
            Message = "input",
        }))
        {
            // Events are not the subject of this test; the tree record is checked.
        }

        var runs = await host.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 });
        var root = runs.Single(run => run.Kind == RunKind.Workflow);
        var children = runs.Where(run => run.Kind == RunKind.Agent).ToList();

        children.Count.ShouldBe(2);
        children.ShouldAllBe(run => run.ParentRunId == root.Id);
        children.ShouldAllBe(run => run.Depth == 1);

        // Stands ALONE in the root list: if child runs showed up as independent
        // roots, this query would return three rows.
        (await host.RunStore.QueryRunsAsync(new RunQuery { Take = 100 })).Count.ShouldBe(1);
    }

    [Fact]
    public void Bound_agent_name_and_description_are_preserved()
    {
        var host = new WorkflowTestHost("summarizer");
        var services = BuildServices(host);

        var agent = services.GetWorkflowAgent("chain", "summarizer", "Summarizes text.");

        agent.Name.ShouldBe("summarizer");
        agent.Description.ShouldBe("Summarizes text.");
    }

    [Fact]
    public void Empty_agent_name_is_rejected()
    {
        var host = new WorkflowTestHost("summarizer");
        var services = BuildServices(host);

        Should.Throw<ArgumentException>(() => services.GetWorkflowAgent("chain", "  "));
    }

    /// <summary>
    /// The smallest container needed for <see cref="WorkflowAgentBinding.GetWorkflowAgent"/>.
    /// </summary>
    private static ServiceProvider BuildServices(WorkflowTestHost host)
        => new ServiceCollection()
            .AddSingleton(host.Resolver)
            .AddSingleton(host.AgentCache)
            .AddSingleton<ITenantContext>(host.TenantContext)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
}
