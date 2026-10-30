namespace Tracon.Workflows.UnitTests;

/// <summary>
/// The shape of an agent version pin, judged by <see cref="WorkflowDefinitionValidator"/> at save
/// and again when the workflow is compiled (failure mode 16): only an agent node may
/// carry one, and a version is at least 1.
/// </summary>
/// <remarks>
/// Whether the agent has the version is the catalog's to say (<see cref="AgentVersionPin"/>,
/// tested in the Core project and over HTTP); this is the part that needs no catalog.
/// </remarks>
public sealed class WorkflowNodeAgentVersionValidationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public void A_pin_of_one_or_more_on_an_agent_node_is_valid(int version)
        => WorkflowDefinitionValidator.Validate(Chain(Agent("writer", version))).ShouldBeNull();

    [Fact]
    public void A_node_with_no_pin_is_valid()
        => WorkflowDefinitionValidator.Validate(Chain(Agent("writer", null), Function("tts"))).ShouldBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_pin_below_one_is_refused_and_the_message_names_the_node_the_workflow_and_the_value(int version)
    {
        var problem = WorkflowDefinitionValidator.Validate(Chain(Agent("writer", version)));

        problem.ShouldNotBeNull();
        problem.ShouldContain("Node 'writer' in workflow 'chain'", Case.Sensitive);
        problem.ShouldContain($"'agentVersion' of {version}", Case.Sensitive);
    }

    [Fact]
    public void A_function_node_that_carries_a_pin_is_refused_by_name()
    {
        var problem = WorkflowDefinitionValidator.Validate(Chain(
            Agent("writer", null),
            new WorkflowNodeReference { Name = "tts", Kind = WorkflowNodeKind.Function, AgentVersion = 2 }));

        problem.ShouldNotBeNull();
        problem.ShouldContain("Node 'tts' in workflow 'chain' is a function node but carries an 'agentVersion'", Case.Sensitive);
    }

    [Fact]
    public void The_compiler_refuses_the_same_shapes_so_a_definition_saved_by_other_means_cannot_run()
    {
        // A definition can reach the store without passing the endpoint (a consumer's own code, a
        // restored backup): the compiler repeats the shape check through Require.
        Should.Throw<TraconException>(() => WorkflowDefinitionValidator.Require(Chain(Agent("writer", 0))))
            .Message.ShouldContain("'agentVersion' of 0", Case.Sensitive);
    }

    [Fact]
    public void A_pin_on_an_agent_names_list_is_not_expressible_so_the_pin_needs_the_nodes_form()
    {
        // AgentNames carries names only; there is nowhere to put a version. The console writes the
        // nodes form as soon as a step is pinned, and this is why.
        var definition = new WorkflowDefinition { Name = "chain", Kind = WorkflowKind.Sequential, AgentNames = ["writer"] };

        WorkflowDefinitionValidator.Validate(definition).ShouldBeNull();
        definition.Nodes.ShouldBeEmpty();
    }

    private static WorkflowDefinition Chain(params WorkflowNodeReference[] nodes)
        => new() { Name = "chain", Kind = WorkflowKind.Sequential, Nodes = nodes };

    private static WorkflowNodeReference Agent(string name, int? version)
        => new() { Name = name, Kind = WorkflowNodeKind.Agent, AgentVersion = version };

    private static WorkflowNodeReference Function(string name)
        => new() { Name = name, Kind = WorkflowNodeKind.Function };
}
