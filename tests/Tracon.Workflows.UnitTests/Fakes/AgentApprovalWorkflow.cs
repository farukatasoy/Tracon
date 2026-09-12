using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// A graph that waits for human input, but whose entry node is NOT a plain
/// <c>BindAsExecutor</c> like <see cref="ApprovalWorkflow"/> — it is an
/// <see cref="AIAgentBinding"/>, the same way a real-world
/// <c>summarize-and-approve</c> workflow would be.
/// </summary>
/// <remarks>
/// MT-WF-062 (defect): a <c>respond</c> call re-triggered the entry node FROM
/// SCRATCH. The plain executor in <see cref="ApprovalWorkflow"/> never catches
/// this — only an agent-host node triggered by a <c>TurnToken</c> (this class)
/// makes the root cause visible.
/// </remarks>
internal static class AgentApprovalWorkflow
{
    /// <summary>The id of the external request port.</summary>
    public const string PortId = "publish-approval";

    /// <summary>Builds the graph. <paramref name="summarizer"/> is the entry node.</summary>
    public static Workflow Build(AIAgent summarizer)
    {
        var port = RequestPort.Create<string, bool>(PortId);
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        var ask = ExecutorBindingExtensions.BindAsExecutor(
            static (List<ChatMessage> messages) =>
                "Should this summary be published?" + Environment.NewLine + Environment.NewLine +
                (messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                 ?? string.Empty),
            id: "approval-question");

        var publish = ExecutorBindingExtensions.BindAsExecutor(
            static (bool approved) => approved
                ? "approved"
                : "rejected",
            id: "publish");

        var summarizeBinding = new AIAgentBinding(
            summarizer,
            new AIAgentHostOptions
            {
                EmitAgentResponseEvents = true,
                EmitAgentUpdateEvents = true,
                ForwardIncomingMessages = false,
            });

        return new WorkflowBuilder(summarizeBinding)
            .AddEdge(summarizeBinding, ask)
            .AddEdge(ask, portBinding)
            .AddEdge(portBinding, publish)
            .WithOutputFrom(publish)
            .WithName("summarizer-approval-flow")
            .Build();
    }

    /// <summary>The code registration to hand to the runner.</summary>
    public static CodeWorkflowRegistration Registration(AIAgent summarizer, string name = "summarizer-approval-flow")
        => new(name, "Summarizes, then waits for human approval.", _ => Build(summarizer));
}
