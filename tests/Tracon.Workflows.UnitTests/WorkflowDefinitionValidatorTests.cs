using System.Text.Json;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// Definition validation. The same rules run both at the HTTP registration
/// endpoint and in the compiler; that is why the rule lives in one place
/// (Tracon.Core).
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
        => Should.Throw<TraconException>(
            () => WorkflowDefinitionValidator.Require(Definition(WorkflowKind.Sequential, [])));

    [Fact]
    public void Node_list_and_agent_list_cannot_both_be_set()
    {
        var definition = Definition(WorkflowKind.Sequential, ["a"]) with
        {
            Nodes = [new WorkflowNodeReference { Name = "b", Kind = WorkflowNodeKind.Function }],
        };

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("both 'agentNames' and 'nodes'", Case.Sensitive);
    }

    [Theory]
    [InlineData(WorkflowKind.Concurrent)]
    [InlineData(WorkflowKind.Handoff)]
    [InlineData(WorkflowKind.GroupChat)]
    [InlineData(WorkflowKind.Magentic)]
    public void Node_list_is_only_supported_by_Sequential(WorkflowKind kind)
    {
        var definition = NodeDefinition(
            kind,
            [new WorkflowNodeReference { Name = "a", Kind = WorkflowNodeKind.Agent }]);

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("only supported in the 'Sequential' pattern", Case.Sensitive);
    }

    [Fact]
    public void Node_with_an_empty_name_is_rejected()
    {
        var definition = NodeDefinition(
            WorkflowKind.Sequential,
            [new WorkflowNodeReference { Name = " ", Kind = WorkflowNodeKind.Agent }]);

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("has an empty name in its node list", Case.Sensitive);
    }

    [Theory]
    [InlineData(WorkflowNodeKind.Unknown)]
    [InlineData(WorkflowNodeKind.Orchestration)]
    [InlineData(WorkflowNodeKind.RequestPort)]
    [InlineData(WorkflowNodeKind.Output)]
    public void Node_kind_must_be_Agent_or_Function(WorkflowNodeKind kind)
    {
        var definition = NodeDefinition(
            WorkflowKind.Sequential,
            [new WorkflowNodeReference { Name = "a", Kind = kind }]);

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("may only reference an 'Agent' or a 'Function' node", Case.Sensitive);
    }

    [Fact]
    public void Repeated_node_name_is_rejected()
    {
        var definition = NodeDefinition(
            WorkflowKind.Sequential,
            [
                new WorkflowNodeReference { Name = "a", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "a", Kind = WorkflowNodeKind.Function },
            ]);

        WorkflowDefinitionValidator.Validate(definition)!
            .ShouldContain("appears more than once", Case.Sensitive);
    }

    [Fact]
    public void Mixed_agent_and_function_node_list_is_accepted()
    {
        var definition = NodeDefinition(
            WorkflowKind.Sequential,
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "uppercase", Kind = WorkflowNodeKind.Function },
                new WorkflowNodeReference { Name = "editor", Kind = WorkflowNodeKind.Agent },
            ]);

        WorkflowDefinitionValidator.Validate(definition).ShouldBeNull();
    }

    [Fact]
    public void A_function_node_may_carry_a_settings_object()
    {
        var definition = NodeDefinition(
            WorkflowKind.Sequential,
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                Function("tts", """{"voice":"alloy"}"""),
            ]);

        WorkflowDefinitionValidator.Validate(definition).ShouldBeNull();
    }

    [Fact]
    public void An_agent_node_that_carries_settings_is_rejected_and_named()
    {
        // F-295: an agent's behavior is set in its own definition. A value on its node would
        // look like a setting that works while nothing reads it.
        var definition = NodeDefinition(
            WorkflowKind.Sequential,
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent, Settings = Parse("""{"tone":"formal"}""") },
                Function("tts", null),
            ]);

        var message = WorkflowDefinitionValidator.Validate(definition);

        message.ShouldNotBeNull();
        message.ShouldContain("Node 'writer'", Case.Sensitive);
        message.ShouldContain("agent node", Case.Sensitive);
        message.ShouldContain("'settings'", Case.Sensitive);
    }

    [Theory]
    [InlineData("[1,2]", "array")]
    [InlineData("\"loud\"", "string")]
    [InlineData("3", "number")]
    [InlineData("true", "true")]
    public void Settings_that_are_not_a_JSON_object_are_rejected(string settings, string kind)
    {
        var definition = NodeDefinition(
            WorkflowKind.Sequential,
            [
                new WorkflowNodeReference { Name = "writer", Kind = WorkflowNodeKind.Agent },
                Function("tts", settings),
            ]);

        var message = WorkflowDefinitionValidator.Validate(definition);

        message.ShouldNotBeNull();
        message.ShouldContain("Node 'tts'", Case.Sensitive);
        message.ShouldContain("JSON object", Case.Sensitive);
        message.ShouldContain(kind, Case.Sensitive);
    }

    // --- Phase 270: handoff topology and the limits ----------------------------------------------------------

    [Fact]
    public void A_handoff_definition_with_an_edge_list_that_reaches_every_agent_is_accepted()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing", "technical"], Edge("frontline", "billing"), Edge("frontline", "technical"), Edge("billing", "frontline")))
            .ShouldBeNull();

    [Fact]
    public void An_empty_edge_list_is_the_star_and_is_accepted()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing"]))
            .ShouldBeNull();

    [Fact]
    public void An_edge_to_an_agent_the_workflow_does_not_list_names_the_edge_and_the_field()
    {
        var message = WorkflowDefinitionValidator.Validate(
            Handoff(["frontline", "billing"], Edge("frontline", "billing"), Edge("billing", "support")));

        message.ShouldNotBeNull();
        message.ShouldContain("handoffs[1] to 'support' is not in agentNames", Case.Sensitive);
    }

    [Fact]
    public void An_edge_from_an_agent_the_workflow_does_not_list_is_rejected()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing"], Edge("ghost", "billing"), Edge("frontline", "billing")))!
            .ShouldContain("handoffs[0] from 'ghost' is not in agentNames", Case.Sensitive);

    [Fact]
    public void An_edge_from_an_agent_to_itself_is_rejected()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing"], Edge("frontline", "billing"), Edge("billing", "billing")))!
            .ShouldContain("handoffs[1] hands 'billing' off to itself", Case.Sensitive);

    [Fact]
    public void The_same_pair_twice_is_rejected_because_the_framework_would_throw_for_it_in_a_run()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing"], Edge("frontline", "billing"), Edge("frontline", "billing")))!
            .ShouldContain("handoffs[1] repeats the edge from 'frontline' to 'billing'", Case.Sensitive);

    [Fact]
    public void The_reverse_pair_is_a_different_edge()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing"], Edge("frontline", "billing"), Edge("billing", "frontline")))
            .ShouldBeNull();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_reason_is_rejected_and_a_missing_one_is_not(string reason)
    {
        WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing"], new WorkflowHandoff { From = "frontline", To = "billing", Reason = reason }))!
            .ShouldContain("handoffs[0] has a blank 'reason'", Case.Sensitive);

        WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing"], new WorkflowHandoff { From = "frontline", To = "billing", Reason = null }))
            .ShouldBeNull();
    }

    [Fact]
    public void A_list_the_first_agent_has_no_part_in_is_rejected()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing", "technical"], Edge("billing", "technical"), Edge("technical", "billing")))!
            .ShouldContain("the first agent 'frontline'", Case.Sensitive);

    [Fact]
    public void An_agent_the_first_agent_cannot_reach_is_rejected_by_name()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing", "technical"], Edge("frontline", "billing"), Edge("technical", "billing")))!
            .ShouldContain("agent 'technical' cannot be reached from the first agent 'frontline'", Case.Sensitive);

    [Fact]
    public void An_agent_reached_through_another_agent_counts_as_reachable()
        => WorkflowDefinitionValidator
            .Validate(Handoff(["frontline", "billing", "technical"], Edge("frontline", "billing"), Edge("billing", "technical")))
            .ShouldBeNull();

    [Theory]
    [InlineData(WorkflowKind.Sequential)]
    [InlineData(WorkflowKind.Concurrent)]
    [InlineData(WorkflowKind.GroupChat)]
    public void Handoff_fields_outside_the_Handoff_pattern_are_rejected(WorkflowKind kind)
    {
        var edges = Definition(kind, ["a", "b"]) with { Handoffs = [Edge("a", "b")] };
        var limit = Definition(kind, ["a", "b"]) with { MaxHandoffs = 3 };

        WorkflowDefinitionValidator.Validate(edges)!.ShouldContain("'handoffs'", Case.Sensitive);
        WorkflowDefinitionValidator.Validate(limit)!.ShouldContain("'maxHandoffs'", Case.Sensitive);
    }

    [Theory]
    [InlineData(WorkflowKind.Sequential)]
    [InlineData(WorkflowKind.Concurrent)]
    [InlineData(WorkflowKind.GroupChat)]
    [InlineData(WorkflowKind.Handoff)]
    public void Magentic_fields_outside_the_Magentic_pattern_are_rejected(WorkflowKind kind)
    {
        var stalls = Definition(kind, ["a", "b"]) with { MaxStalls = 2 };
        var resets = Definition(kind, ["a", "b"]) with { MaxResets = 1 };

        WorkflowDefinitionValidator.Validate(stalls)!.ShouldContain("'maxStalls'", Case.Sensitive);
        WorkflowDefinitionValidator.Validate(resets)!.ShouldContain("'maxResets'", Case.Sensitive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_handoff_limit_that_is_not_positive_is_rejected(int value)
        => WorkflowDefinitionValidator
            .Validate(Handoff(["a", "b"]) with { MaxHandoffs = value })!
            .ShouldContain("'maxHandoffs' value must be positive", Case.Sensitive);

    [Fact]
    public void A_handoff_limit_of_one_is_accepted()
        => WorkflowDefinitionValidator.Validate(Handoff(["a", "b"]) with { MaxHandoffs = 1 }).ShouldBeNull();

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_stall_limit_that_is_not_positive_is_rejected(int value)
        => WorkflowDefinitionValidator
            .Validate(Magentic() with { MaxStalls = value })!
            .ShouldContain("'maxStalls' value must be positive", Case.Sensitive);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_reset_limit_that_is_not_positive_is_rejected_because_zero_stops_the_task_before_its_first_round(int value)
        => WorkflowDefinitionValidator
            .Validate(Magentic() with { MaxResets = value })!
            .ShouldContain("'maxResets' value must be positive", Case.Sensitive);

    [Fact]
    public void Both_magentic_limits_are_accepted_when_positive()
        => WorkflowDefinitionValidator.Validate(Magentic() with { MaxResets = 1, MaxStalls = 1 }).ShouldBeNull();

    private static WorkflowHandoff Edge(string from, string to) => new() { From = from, To = to };

    private static WorkflowDefinition Handoff(IReadOnlyList<string> agentNames, params WorkflowHandoff[] edges)
        => new()
        {
            Name = "triage",
            Kind = WorkflowKind.Handoff,
            AgentNames = agentNames,
            Handoffs = edges,
        };

    private static WorkflowDefinition Magentic()
        => new()
        {
            Name = "plan",
            Kind = WorkflowKind.Magentic,
            AgentNames = ["writer"],
            ManagerAgentName = "manager",
        };

    private static WorkflowNodeReference Function(string name, string? settings)
        => new() { Name = name, Kind = WorkflowNodeKind.Function, Settings = settings is null ? null : Parse(settings) };

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static WorkflowDefinition Definition(WorkflowKind kind, IReadOnlyList<string> agentNames)
        => new()
        {
            Name = "test-workflow",
            Kind = kind,
            AgentNames = agentNames,
        };

    private static WorkflowDefinition NodeDefinition(WorkflowKind kind, IReadOnlyList<WorkflowNodeReference> nodes)
        => new()
        {
            Name = "test-workflow",
            Kind = kind,
            AgentNames = [],
            Nodes = nodes,
        };
}
