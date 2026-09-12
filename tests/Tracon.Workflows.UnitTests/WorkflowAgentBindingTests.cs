using Tracon.Workflows.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Workflows.UnitTests;

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

    /// <summary>
    /// 🚨 Two tenants may hold a workflow and an agent with the SAME name: both
    /// names are unique only WITHIN a tenant (<c>UNIQUE (tenant_id, name)</c>).
    /// The cache key must therefore carry the tenant. When it did not, the second
    /// tenant received the first tenant's wrapper, and with it the first tenant's
    /// description - a description that reaches the participant list sent to the
    /// model in the <c>GroupChat</c> and <c>Magentic</c> patterns.
    /// </summary>
    [Fact]
    public void Description_does_not_leak_between_tenants()
    {
        var host = new WorkflowTestHost("support");
        var services = BuildServices(host);

        host.TenantContext.TenantId = "tenant-a";
        var first = services.GetWorkflowAgent("triage", "support", "Tenant A's private note.");

        host.TenantContext.TenantId = "tenant-b";
        var second = services.GetWorkflowAgent("triage", "support", "Tenant B's own note.");

        second.Description.ShouldBe(
            "Tenant B's own note.",
            "the second tenant must not receive the first tenant's description.");

        ReferenceEquals(first, second).ShouldBeFalse(
            "each tenant must get its own wrapper; a shared instance crosses the tenant boundary.");
    }

    /// <summary>
    /// The executor id stays derived from the <c>(workflow, agent)</c> pair ONLY.
    /// Adding the tenant to the cache key must not reach the identity: checkpoints
    /// written before the fix have to stay readable (Phase 16).
    /// </summary>
    [Fact]
    public void Tenant_does_not_change_the_executor_id()
    {
        var host = new WorkflowTestHost("support");
        var services = BuildServices(host);

        host.TenantContext.TenantId = "tenant-a";
        var first = services.GetWorkflowAgent("triage", "support");

        host.TenantContext.TenantId = "tenant-b";
        var second = services.GetWorkflowAgent("triage", "support");

        second.Id.ShouldBe(first.Id, "the executor id anchors existing checkpoints.");
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
