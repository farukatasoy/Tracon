using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// The smallest workflow that waits for human input: it asks a question,
/// waits for yes/no, and produces an output based on the result.
/// </summary>
/// <remarks>
/// <para>
/// There is <strong>no</strong> model call. The place where the human-in-the-loop
/// flow is tested is the execution engine itself; putting a model in between
/// would slow the test down and blur where a failure comes from.
/// </para>
/// <para>
/// 🚨 <c>WithOutputFrom</c> is required. If it is not called, the graph runs but
/// the executor that tries to produce output fails with <c>Cannot output object
/// of type String. Expecting one of []</c> — measured (Phase 16).
/// </para>
/// </remarks>
internal static class ApprovalWorkflow
{
    /// <summary>The id of the external request port. Also appears under this name as the graph node.</summary>
    public const string PortId = "approval-port";

    /// <summary>Builds the graph.</summary>
    public static Workflow Build()
    {
        var port = RequestPort.Create<string, bool>(PortId);
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var start = ExecutorBindingExtensions.BindAsExecutor<List<ChatMessage>, string>(
            static messages => $"Approval requested: {messages.LastOrDefault()?.Text ?? string.Empty}",
            id: "start");

        // 🚨 The output handler is inferred from its RETURN TYPE. A handler whose
        // body calls YieldOutputAsync but has no return type declares no output
        // type at all, and fails at run time with "Cannot output object of type
        // String. Expecting one of []" — measured (Phase 16).
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

    /// <summary>The code registration to hand to the runner.</summary>
    public static CodeWorkflowRegistration Registration(string name = "approval-flow")
        => new(name, "Flow that waits for human approval.", _ => Build());
}
