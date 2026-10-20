using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Graph;

/// <summary>
/// The name a callable agent gets as a tool in <see cref="SubAgentCallMode.Tool"/> mode, and the
/// definitions whose tools would share one (Phase 228).
/// </summary>
public sealed class SubAgentToolNamesTests
{
    private static readonly SubAgentSettings ToolMode = new() { CallMode = SubAgentCallMode.Tool };

    [Theory]
    [InlineData("support", "support")]
    [InlineData("support-agent", "support_agent")]
    [InlineData("support_agent", "support_agent")]
    [InlineData("a--b  c", "a_b_c")]
    [InlineData("café-bot", "caf_bot")]
    public void A_name_keeps_letters_and_digits_and_turns_each_other_run_into_one_underscore(string agentName, string toolName)
        => SubAgentToolNames.NameOf(agentName).ShouldBe(toolName);

    [Theory]
    [InlineData("support-agent")]
    [InlineData("a--b  c")]
    [InlineData("café-bot")]
    public void The_name_is_the_one_the_framework_gives_an_agent_tool(string agentName)
    {
        // The rule below is Tracon's own, and the names it checks must be the names the model sees.
        // This pins today's framework rule, so a framework change shows here instead of as a
        // collision the validator cannot see.
        var agent = new FakeChatClient().AsAIAgent(new ChatClientAgentOptions { Name = agentName });

        agent.AsAIFunction().Name.ShouldBe(SubAgentToolNames.NameOf(agentName));
    }

    [Fact]
    public void The_tools_made_for_the_wrappers_carry_the_rule_name_in_order()
    {
        var children = new[] { "support-agent", "billing" }
            .Select(static name => new FakeChatClient().AsAIAgent(new ChatClientAgentOptions { Name = name }))
            .ToList();

        SubAgentToolNames.CreateFunctions(children).Select(static function => function.Name)
            .ShouldBe(["support_agent", "billing"]);
    }

    [Fact]
    public void Free_names_have_no_problem()
        => SubAgentToolNames.FindProblems(Definition(callable: ["support-agent", "billing"], toolNames: ["lookup"]))
            .ShouldBeEmpty();

    [Fact]
    public void Background_mode_never_has_a_problem()
        => SubAgentToolNames.FindProblems(Definition(callable: ["lookup"], toolNames: ["lookup"]) with { SubAgents = null })
            .ShouldBeEmpty();

    [Fact]
    public void A_name_a_registered_tool_has_is_a_problem_that_names_both()
    {
        var problem = SubAgentToolNames.FindProblems(Definition(callable: ["billing", "support-agent"], toolNames: ["support_agent"]))
            .ShouldHaveSingleItem();

        problem.Code.ShouldBe("sub_agent_tool_name_conflict");
        problem.Path.ShouldBe("callableAgentNames[1]");
        problem.Message.ShouldContain("'support-agent'");
        problem.Message.ShouldContain("'support_agent'");
        problem.Message.ShouldContain("toolNames");
    }

    [Fact]
    public void Two_callable_agents_that_get_one_name_are_a_problem_at_the_second()
    {
        var problem = SubAgentToolNames.FindProblems(Definition(callable: ["a-b", "a_b"])).ShouldHaveSingleItem();

        problem.Path.ShouldBe("callableAgentNames[1]");
        problem.Message.ShouldContain("'a_b'");
        problem.Message.ShouldContain("'a-b'");
    }

    [Theory]
    [InlineData("load_skill")]
    [InlineData("read_skill_resource")]
    [InlineData("run_skill_script")]
    public void A_skill_tool_name_is_taken_only_while_the_definition_names_a_skill(string callable)
    {
        SubAgentToolNames.FindProblems(Definition(callable: [callable])).ShouldBeEmpty();

        SubAgentToolNames.FindProblems(Definition(callable: [callable]) with { SkillNames = ["any-skill"] })
            .ShouldHaveSingleItem().Message.ShouldContain($"'{callable}'");
    }

    [Fact]
    public void The_semantic_search_tool_name_is_taken_only_while_semantic_search_is_on()
    {
        SubAgentToolNames.FindProblems(Definition(callable: ["search-knowledge"])).ShouldBeEmpty();

        var withSearch = Definition(callable: ["search-knowledge"]) with
        {
            Memory = new MemorySettings { EnableVectorSearch = true },
        };

        SubAgentToolNames.FindProblems(withSearch).ShouldHaveSingleItem().Message.ShouldContain("'search_knowledge'");
    }

    [Fact]
    public async Task A_tracon_exception_of_the_sub_agent_becomes_the_tool_result()
    {
        var inner = AIFunctionFactory.Create(
            (Func<string>)(() => throw new TraconException("The model provider request failed.")),
            "support");

        var result = await new SubAgentToolFunction(inner, "support").InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal));

        result.ShouldBe("Agent 'support' could not complete: The model provider request failed.");
    }

    [Fact]
    public async Task An_exception_that_is_not_Tracons_own_keeps_the_frameworks_behavior()
    {
        // Its message can carry a connection string or a key, so it is not shown to the model.
        var inner = AIFunctionFactory.Create(
            (Func<string>)(() => throw new InvalidOperationException("a message that carries connection details")),
            "support");

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await new SubAgentToolFunction(inner, "support").InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)));
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_turned_into_text()
    {
        using var cancelled = new CancellationTokenSource();
        var inner = AIFunctionFactory.Create(
            (Func<string>)(() =>
            {
                cancelled.Cancel();

                throw new TraconException("raised after the caller cancelled");
            }),
            "support");

        await Should.ThrowAsync<TraconException>(
            async () => await new SubAgentToolFunction(inner, "support").InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal), cancelled.Token));
    }

    private static AgentDefinition Definition(string[] callable, string[]? toolNames = null)
        => TestData.Definition(toolNames: toolNames) with
        {
            CallableAgentNames = callable,
            SubAgents = ToolMode,
        };
}
