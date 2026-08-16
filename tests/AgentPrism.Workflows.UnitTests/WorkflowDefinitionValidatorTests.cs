namespace AgentPrism.Workflows.UnitTests;

/// <summary>
/// Definition validation. The same rules run both at the HTTP registration
/// endpoint and in the compiler; that is why the rule lives in one place
/// (AgentPrism.Core).
/// </summary>
public sealed class WorkflowDefinitionValidatorTests
{
    [Fact]
    public void Valid_definition_is_accepted()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Sequential, ["a", "b"]))
            .ShouldBeNull();

    [Fact]
    public void Empty_agent_list_is_rejected()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Sequential, []))!
            .ShouldContain("has no agents", Case.Sensitive);

    [Fact]
    public void Repeated_agent_name_is_rejected()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Sequential, ["a", "b", "a"]))!
            .ShouldContain("appears more than once", Case.Sensitive);

    [Theory]
    [InlineData(WorkflowKind.Concurrent)]
    [InlineData(WorkflowKind.Handoff)]
    [InlineData(WorkflowKind.GroupChat)]
    public void Patterns_that_need_two_agents_are_rejected_with_a_single_agent(WorkflowKind kind)
        => WorkflowDefinitionValidator
            .Validate(Definition(kind, ["a"]))!
            .ShouldContain("at least two agents", Case.Sensitive);

    [Fact]
    public void Magentic_requires_a_manager_agent()
        => WorkflowDefinitionValidator
            .Validate(Definition(WorkflowKind.Magentic, ["a"]))!
            .ShouldContain("'managerAgentName' is required", Case.Sensitive);

    [Fact]
    public void Magentic_manager_cannot_also_be_a_participant()
    {
        var definition = Definition(WorkflowKind.Magentic, ["a", "b"]) with { ManagerAgentName = "a" };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("both manager and participant", Case.Sensitive);
    }

    [Fact]
    public void GroupChat_does_not_accept_a_manager_agent()
    {
        // GroupChat's manager is NOT an agent: it is the round-robin manager on
        // the code side that distributes turn order. Silently ignoring the field
        // would hide the fact that the behavior the user expected never happened.
        var definition = Definition(WorkflowKind.GroupChat, ["a", "b"]) with { ManagerAgentName = "c" };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("belongs only to the 'Magentic' pattern", Case.Sensitive);
    }

    [Fact]
    public void Handoff_instructions_outside_Handoff_are_rejected()
    {
        var definition = Definition(WorkflowKind.Sequential, ["a", "b"]) with
        {
            HandoffInstructions = "hand off if needed",
        };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("'handoffInstructions'", Case.Sensitive);
    }

    [Fact]
    public void Zero_turn_limit_is_rejected()
    {
        var definition = Definition(WorkflowKind.GroupChat, ["a", "b"]) with { MaxIterations = 0 };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("must be positive", Case.Sensitive);
    }

    [Fact]
    public void Unknown_pattern_is_rejected()
    {
        var definition = Definition((WorkflowKind)99, ["a", "b"]);

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("uses an unknown pattern", Case.Sensitive);
    }

    [Fact]
    public void Require_throws_on_an_invalid_definition()
        => Should.Throw<AgentPrismException>(
            () => WorkflowDefinitionValidator.Require(Definition(WorkflowKind.Sequential, [])));

    private static WorkflowDefinition Definition(WorkflowKind kind, IReadOnlyList<string> agentNames)
        => new()
        {
            Name = "test-workflow",
            Kind = kind,
            AgentNames = agentNames,
        };
}
