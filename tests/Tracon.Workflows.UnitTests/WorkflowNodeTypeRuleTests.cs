using Microsoft.Extensions.AI;
using Tracon.Workflows.UnitTests.Fakes;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// F-341: the type chain of a node list - what the validator assumes, and what the
/// compiler does with a chain that does not fit.
/// </summary>
/// <remarks>
/// The save-time refusal is proven over HTTP (<c>WorkflowNodeTypeChainTests</c>). These
/// cases pin the two facts the rule is built on - an agent node takes and passes on a
/// chat message list, and a run starts by sending one - against the code that makes
/// them true, so a change on either side breaks a test instead of the guarantee.
/// </remarks>
public sealed class WorkflowNodeTypeRuleTests
{
    [Fact]
    public void An_agent_node_takes_and_passes_on_the_type_the_validator_assumes()
        => typeof(WorkflowAgentStepExecutor).BaseType!.GetGenericArguments()
            .ShouldBe([WorkflowDefinitionValidator.AgentNodeMessageType, WorkflowDefinitionValidator.AgentNodeMessageType]);

    [Fact]
    public async Task A_first_function_that_takes_the_run_input_type_receives_the_run_message()
    {
        WorkflowDefinitionValidator.RunInputType.ShouldBe(typeof(List<ChatMessage>));

        var host = new WorkflowTestHost();

        host.AddFunction<List<ChatMessage>, string>(
            "echo",
            static (messages, _, _) => new ValueTask<string>($"got: {messages.LastOrDefault()?.Text}"));

        var definition = Chain("first-function", Function("echo"));

        WorkflowDefinitionValidator.ValidateNodeTypes(definition, host.Functions).ShouldBeNull();

        await host.SaveAsync(definition);

        var events = await RunAsync(host, "first-function");

        events.ShouldContain(static runEvent =>
            runEvent.Type == RunEventType.WorkflowOutput &&
            string.Equals(runEvent.Text, "got: hello", StringComparison.Ordinal));
    }

    [Fact]
    public void A_node_after_a_function_that_declares_object_is_not_checked()
    {
        // A node that declares object may send any type at run time; whether the
        // next node gets it depends on the value, not on the declaration.
        var host = new WorkflowTestHost("writer");

        host.AddFunction<List<ChatMessage>, object>("loose", static (messages, _, _) => new ValueTask<object>(messages));

        WorkflowDefinitionValidator.ValidateNodeTypes(Chain("loose", Agent("writer"), Function("loose"), Agent("writer2")), host.Functions)
            .ShouldBeNull();
    }

    [Fact]
    public void An_unregistered_function_is_left_to_the_name_check()
    {
        var host = new WorkflowTestHost("writer");

        WorkflowDefinitionValidator.ValidateNodeTypes(Chain("missing", Agent("writer"), Function("nowhere"), Agent("writer2")), host.Functions)
            .ShouldBeNull();
    }

    [Fact]
    public void A_definition_without_nodes_has_nothing_to_check()
        => WorkflowDefinitionValidator.ValidateNodeTypes(
                new WorkflowDefinition { Name = "plain", Kind = WorkflowKind.Sequential, AgentNames = ["a", "b"] },
                functions: null)
            .ShouldBeNull();

    [Fact]
    public async Task The_compiler_refuses_a_chain_that_does_not_fit_naming_both_nodes()
    {
        var host = new WorkflowTestHost("writer");

        host.AddFunction<string, string>("shout", static (input, _, _) => new ValueTask<string>(input.ToUpperInvariant()));

        var exception = await Should.ThrowAsync<TraconException>(async () =>
            await host.Compiler.CompileAsync(Chain("stale", Agent("writer"), Function("shout"))));

        exception.Message.ShouldContain("'shout'", Case.Sensitive);
        exception.Message.ShouldContain("'writer'", Case.Sensitive);
        exception.Message.ShouldContain("System.Collections.Generic.List<Microsoft.Extensions.AI.ChatMessage>", Case.Sensitive);
    }

    private static WorkflowDefinition Chain(string name, params WorkflowNodeReference[] nodes)
        => new() { Name = name, Kind = WorkflowKind.Sequential, Nodes = nodes };

    private static WorkflowNodeReference Agent(string name) => new() { Name = name, Kind = WorkflowNodeKind.Agent };

    private static WorkflowNodeReference Function(string name) => new() { Name = name, Kind = WorkflowNodeKind.Function };

    private static async Task<List<RunEvent>> RunAsync(WorkflowTestHost host, string name)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in host.CreateRunner().RunStreamingAsync(new WorkflowRunRequest { WorkflowName = name, Message = "hello" }))
        {
            events.Add(runEvent);
        }

        return events;
    }
}
