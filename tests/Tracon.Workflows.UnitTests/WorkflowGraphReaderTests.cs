using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Extraction of the graph from a compiled workflow.
/// </summary>
/// <remarks>
/// The most critical contract is <strong>node ids</strong>: the UI matches
/// nodes against the <c>ExecutorInvoked</c> / <c>ExecutorCompleted</c> text in
/// run events to color them. If the ids drift, the graph still renders but
/// never gets colored - a silent breakage.
/// </remarks>
public sealed class WorkflowGraphReaderTests
{
    [Fact]
    public async Task Sequential_graph_carries_agent_nodes_and_output()
    {
        var host = new WorkflowTestHost("summarizer", "translator");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["summarizer", "translator"],
        });

        var graph = (await host.CreateRunner().GetGraphAsync("chain"))!;

        graph.Name.ShouldBe("chain");
        graph.Mermaid.ShouldNotBeNullOrWhiteSpace();

        var agents = graph.Nodes.Where(node => node.Kind == WorkflowNodeKind.Agent).ToList();

        agents.Count.ShouldBe(2);
        agents.Select(node => node.AgentName).Order(StringComparer.Ordinal)
            .ShouldBe(["summarizer", "translator"]);

        // The built-in pattern adds an output node; if this node the user
        // never wrote in the definition were missing from the graph, run
        // events would not match.
        graph.Nodes.ShouldContain(node => node.Kind == WorkflowNodeKind.Output);

        graph.StartExecutorId.ShouldNotBeNullOrWhiteSpace();
        graph.Nodes.ShouldContain(node => string.Equals(node.Id, graph.StartExecutorId, StringComparison.Ordinal));

        // Both ends of every edge must point to a node in the graph.
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        graph.Edges.ShouldNotBeEmpty();
        graph.Edges.ShouldAllBe(edge => ids.Contains(edge.From) && ids.Contains(edge.To));
    }

    [Fact]
    public async Task Node_ids_MATCH_run_events()
    {
        var host = new WorkflowTestHost("summarizer", "translator");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["summarizer", "translator"],
        });

        var runner = host.CreateRunner();
        var graph = (await runner.GetGraphAsync("chain"))!;
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);

        var invoked = new List<string>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest
        {
            WorkflowName = "chain",
            Message = "input",
        }))
        {
            if (runEvent.Type == RunEventType.ExecutorInvoked && runEvent.Text is { Length: > 0 } id)
            {
                invoked.Add(id);
            }
        }

        invoked.ShouldNotBeEmpty();
        invoked.ShouldAllBe(id => ids.Contains(id));
    }

    [Fact]
    public async Task External_request_port_is_a_separate_node_kind()
    {
        var host = new WorkflowTestHost();
        var runner = host.CreateRunner(configure: null, services: null, ApprovalWorkflow.Registration());

        var graph = (await runner.GetGraphAsync("approval-flow"))!;

        var port = graph.Nodes.Single(node => node.Kind == WorkflowNodeKind.RequestPort);

        port.Id.ShouldBe(ApprovalWorkflow.PortId);
    }

    [Fact]
    public async Task Concurrent_graph_shows_fan_out_and_fan_in_nodes()
    {
        var host = new WorkflowTestHost("one", "two");

        await host.SaveAsync(new WorkflowDefinition
        {
            Name = "concurrent",
            Kind = WorkflowKind.Concurrent,
            AgentNames = ["one", "two"],
        });

        var graph = (await host.CreateRunner().GetGraphAsync("concurrent"))!;

        graph.Nodes.Count(node => node.Kind == WorkflowNodeKind.Agent).ShouldBe(2);
        graph.Nodes.ShouldContain(node => node.Kind == WorkflowNodeKind.Orchestration);

        // Fan-out and fan-in are read from the edge kinds; the UI draws its
        // arrows accordingly.
        graph.Edges.ShouldContain(edge => edge.Kind == WorkflowEdgeKind.FanOut);
        graph.Edges.ShouldContain(edge => edge.Kind == WorkflowEdgeKind.FanIn);
    }

    [Fact]
    public async Task A_nonexistent_workflow_has_no_graph()
        => (await new WorkflowTestHost().CreateRunner().GetGraphAsync("missing")).ShouldBeNull();
}
