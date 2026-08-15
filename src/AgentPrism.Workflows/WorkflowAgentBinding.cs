using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Binds catalog agents to a workflow graph defined in code.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Do NOT put an agent taken from the catalog DIRECTLY into the
/// graph.</strong> <c>IAgentCatalog.ResolveAsync</c> returns an agent carrying
/// the run-recording wrapper, but Microsoft Agent Framework calls it with
/// <c>options = null</c>; the wrapper cannot read tree info from the incoming
/// settings and opens ITS OWN root row instead. Result: the workflow run
/// looks empty, the agents are independent roots in the list, and the
/// waterfall is drawn wrong. Measured (phase 15): in the sample app, the tree
/// came back as one row instead of three.
/// </para>
/// <para>
/// This helper wraps the agent with <see cref="ChildAgentInvoker"/>. The
/// wrapper reads tree info from the ambient scope, applies the depth/budget/
/// tenant limits, and attaches the sub-run under the workflow row. For
/// workflows defined from the UI, the compiler does the same wrapping.
/// </para>
/// <example>
/// <code>
/// agentPrism.AddWorkflow("summarize-and-translate", services =>
///     AgentWorkflowBuilder.BuildSequential("summarize-and-translate",
///     [
///         services.GetWorkflowAgent("summarize-and-translate", "summarizer"),
///         services.GetWorkflowAgent("summarize-and-translate", "translator"),
///     ]));
/// </code>
/// </example>
/// </remarks>
public static class WorkflowAgentBinding
{
    /// <summary>
    /// Wraps a catalog agent in a form that can be placed in a workflow graph.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="workflowName">The name of the enclosing workflow. Appears in error messages.</param>
    /// <param name="agentName">The name of the agent to bind.</param>
    /// <param name="description">
    /// A description of what the agent does. In the <c>GroupChat</c> and
    /// <c>Magentic</c> patterns it reaches the participant list sent to the
    /// model; leaving it empty makes it harder for the manager agent to know
    /// when to call whom.
    /// </param>
    /// <returns>An agent that can be placed in the graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of the names is empty.</exception>
    /// <remarks>
    /// <para>
    /// The agent is resolved <strong>lazily</strong>: this call does not read
    /// the catalog, it only builds a wrapper. This way, when an agent
    /// definition changes, the compiled workflow does not go stale, and the
    /// factory being synchronous causes no problem.
    /// </para>
    /// <para>
    /// The <strong>same instance</strong> is returned for the same
    /// <c>(workflowName, agentName)</c> pair. This is deliberate: Microsoft
    /// Agent Framework derives executor ids from the agent instance's
    /// identity, and returning a new instance on every call would make
    /// checkpoints incompatible.
    /// </para>
    /// </remarks>
    public static AIAgent GetWorkflowAgent(
        this IServiceProvider services,
        string workflowName,
        string agentName,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        return services.GetRequiredService<WorkflowAgentCache>().Get(workflowName, agentName, description);
    }
}
