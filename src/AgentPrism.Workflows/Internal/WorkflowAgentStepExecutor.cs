using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Runs a tree-attached agent as a plain node inside a hand-built graph.
/// </summary>
/// <remarks>
/// <para>
/// A distinct runtime type, not just a <see cref="FunctionExecutor{TInput,TOutput}"/>
/// constructed inline: <see cref="WorkflowGraphReader"/> tells an agent step
/// apart from a genuine function node by this type name alone (the graph is
/// read from the compiled <see cref="Workflow"/>, never the definition - see
/// the remarks on <c>WorkflowGraph</c>). Its id is always the agent's own
/// catalog name, set by <see cref="WorkflowDefinitionCompiler"/>; that is also
/// how <c>WorkflowGraphReader.AgentNameOf</c> recovers it with no extra state.
/// </para>
/// <para>
/// See <c>WorkflowDefinitionCompiler.BuildMixedSequentialAsync</c> for why a
/// mixed Sequential chain binds agent steps this way instead of
/// <see cref="AIAgentBinding"/>.
/// </para>
/// </remarks>
internal sealed class WorkflowAgentStepExecutor : FunctionExecutor<List<ChatMessage>, List<ChatMessage>>
{
    public WorkflowAgentStepExecutor(string id, AIAgent agent)
        : base(id, (messages, _, cancellationToken) => RunAsync(agent, messages, cancellationToken))
    {
    }

    private static async ValueTask<List<ChatMessage>> RunAsync(
        AIAgent agent,
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var response = await AgentResponseExtensions.ToAgentResponseAsync(
            agent.RunStreamingAsync(messages, cancellationToken: cancellationToken),
            cancellationToken).ConfigureAwait(false);

        return [.. response.Messages];
    }
}
