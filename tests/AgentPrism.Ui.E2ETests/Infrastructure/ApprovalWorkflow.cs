using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism.Ui.E2ETests.Infrastructure;

/// <summary>
/// The smallest workflow that waits on human input: it asks a question, waits
/// for yes/no, and produces output based on the result.
/// </summary>
/// <remarks>
/// There is no model call. What is being tested is whether the UI can show the
/// pending request card; putting a model in the middle would slow the test
/// down and blur where a failure actually comes from.
/// </remarks>
internal static class ApprovalWorkflow
{
    /// <summary>The external request port's id. Shows up as a node in the graph.</summary>
    public const string PortId = "approval-port";

    /// <summary>Builds the graph.</summary>
    public static Workflow Build()
    {
        var port = RequestPort.Create<string, bool>(PortId);
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Requests approval: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        // 🚨 The output type is inferred FROM THE HANDLER'S RETURN TYPE; a
        // void handler that calls YieldOutputAsync in its body declares no
        // output type and fails at run time (measured in Phase 16).
        var end = ExecutorBindingExtensions.BindAsExecutor<bool, string>(
            static approved => approved ? "approved" : "rejected",
            id: "end");

        return new WorkflowBuilder(start)
            .AddEdge(start, portBinding)
            .AddEdge(portBinding, end)
            .WithOutputFrom(end)
            .WithName("approval-flow")
            .Build();
    }
}
