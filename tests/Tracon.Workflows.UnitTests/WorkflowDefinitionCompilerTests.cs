using Microsoft.Agents.AI.Workflows;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Compiling a graph from a definition. All five patterns must actually build;
/// the Microsoft Agent Framework builder API is unpredictable, and a pattern
/// that fails to build would otherwise only surface at run time.
/// </summary>
public sealed class WorkflowDefinitionCompilerTests
{
    [Fact]
    public async Task Sequential_compiles()
    {
        var host = new WorkflowTestHost("writer", "editor", "reviewer");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor", "reviewer"],
        });

        workflow.Name.ShouldBe("chain");

        // Three agents + an output collector. MAF appends its own collector
        // executor to the end of the prebuilt pattern; that is why the count
        // is greater than the agent count.
        workflow.ReflectExecutors().Count.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Concurrent_compiles()
    {
        var host = new WorkflowTestHost("writer", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "parallel",
            Kind = WorkflowKind.Concurrent,
            AgentNames = ["writer", "editor"],
        });

        workflow.Name.ShouldBe("parallel");
    }

    [Fact]
    public async Task Handoff_compiles()
    {
        var host = new WorkflowTestHost("support", "expert");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "handoff",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["support", "expert"],
            HandoffInstructions = "Hand off to the expert for technical questions.",
            MaxIterations = 4,
        });

        workflow.Name.ShouldBe("handoff");
    }

    [Fact]
    public async Task Handoff_without_an_edge_list_keeps_the_star_the_first_agent_hands_off_to_every_other()
    {
        var host = new WorkflowTestHost("frontline", "billing", "technical");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "star",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["frontline", "billing", "technical"],
        });

        AgentEdges(workflow, "star").ShouldBe(["frontline->billing", "frontline->technical"], ignoreOrder: true);
    }

    [Fact]
    public async Task Handoff_edges_are_the_ones_the_definition_names_and_a_specialist_can_hand_back()
    {
        var host = new WorkflowTestHost("frontline", "billing", "technical");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "triage",
            Kind = WorkflowKind.Handoff,
            AgentNames = ["frontline", "billing", "technical"],
            Handoffs =
            [
                new WorkflowHandoff { From = "frontline", To = "billing" },
                new WorkflowHandoff { From = "frontline", To = "technical", Reason = "Technical questions." },
                new WorkflowHandoff { From = "billing", To = "frontline" },
                new WorkflowHandoff { From = "billing", To = "technical" },
            ],
        });

        AgentEdges(workflow, "triage").ShouldBe(
            ["frontline->billing", "frontline->technical", "billing->frontline", "billing->technical"],
            ignoreOrder: true);
    }

    [Fact]
    public void The_handoff_tool_prefix_the_limit_counts_is_the_frameworks_own()
        => HandoffLimit.ToolPrefix.ShouldBe(HandoffWorkflowBuilder.FunctionPrefix);

    private static List<string> AgentEdges(Workflow workflow, string name)
    {
        var graph = WorkflowGraphReader.Read(name, workflow);
        var agents = graph.Nodes
            .Where(static node => node.AgentName is not null)
            .ToDictionary(static node => node.Id, static node => node.AgentName!, StringComparer.Ordinal);

        return graph.Edges
            .Where(edge => agents.ContainsKey(edge.From) && agents.ContainsKey(edge.To))
            .Select(edge => $"{agents[edge.From]}->{agents[edge.To]}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public async Task GroupChat_compiles()
    {
        var host = new WorkflowTestHost("writer", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "chat",
            Kind = WorkflowKind.GroupChat,
            AgentNames = ["writer", "editor"],
            MaxIterations = 2,
        });

        workflow.Name.ShouldBe("chat");
    }

    [Fact]
    public async Task Magentic_compiles()
    {
        var host = new WorkflowTestHost("manager", "writer", "editor");

        var workflow = await host.Compiler.CompileAsync(new WorkflowDefinition
        {
            Name = "magentic",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["writer", "editor"],
            ManagerAgentName = "manager",
            MaxIterations = 2,
        });

        workflow.Name.ShouldBe("magentic");
    }

    [Fact]
    public async Task Compiling_the_same_definition_twice_KEEPS_EXECUTOR_IDS_THE_SAME()
    {
        // 🚨 Resuming from a checkpoint depends on this. Microsoft Agent
        // Framework derives executor ids from the agent INSTANCE; if the ids
        // change on every compile, MAF rejects the checkpoint ("The specified
        // checkpoint is not compatible with the workflow"). Measured
        // (Phase 15): a graph built with fresh agent instances came out incompatible.
        var host = new WorkflowTestHost("writer", "editor");

        var definition = new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            AgentNames = ["writer", "editor"],
        };

        var first = await host.Compiler.CompileAsync(definition);
        var second = await host.Compiler.CompileAsync(definition);

        first.ReflectExecutors().Keys.Order(StringComparer.Ordinal)
            .ShouldBe(second.ReflectExecutors().Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Unknown_agent_name_fails_with_a_clear_error()
    {
        var host = new WorkflowTestHost("writer");

        var exception = await Should.ThrowAsync<TraconException>(async () =>
            await host.Compiler.CompileAsync(new WorkflowDefinition
            {
                Name = "chain",
                Kind = WorkflowKind.Sequential,
                AgentNames = ["writer", "missing-agent"],
            }));

        exception.Message.ShouldContain("'missing-agent'", Case.Sensitive);
        exception.Message.ShouldContain("no such agent exists in the catalog", Case.Sensitive);
    }

    [Fact]
    public async Task Invalid_definition_is_rejected_before_compiling()
    {
        var host = new WorkflowTestHost("writer");

        var exception = await Should.ThrowAsync<TraconException>(async () =>
            await host.Compiler.CompileAsync(new WorkflowDefinition
            {
                Name = "chain",
                Kind = WorkflowKind.Sequential,
                AgentNames = [],
            }));

        exception.Message.ShouldContain("has no agents", Case.Sensitive);
    }
}
