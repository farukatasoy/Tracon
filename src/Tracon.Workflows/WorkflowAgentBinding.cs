using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Binds catalog agents to a workflow graph defined in code.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Do NOT put an agent taken from the catalog DIRECTLY into the
/// graph.</strong> <c>IAgentCatalog.ResolveAsync</c> returns an agent carrying
/// the run-recording wrapper, but Microsoft Agent Framework calls it with
/// <c>options = null</c>; the wrapper cannot read tree info from the incoming
/// settings and opens ITS OWN root row instead. Result: the workflow run
/// looks empty, the agents are independent roots in the list, and the
/// waterfall is drawn wrong. Measured: in the sample app, the tree
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
/// tracon.AddWorkflow("summarize-and-translate", services =>
///     AgentWorkflowBuilder.BuildSequential("summarize-and-translate",
///     [
///         services.GetWorkflowAgent("summarize-and-translate", "summarizer"),
///         services.GetWorkflowAgent("summarize-and-translate", "translator"),
///     ]));
/// </code>
/// </example>
/// <para>
/// To run an agent as a plain step of a graph with a revision loop or a human approval - a step that takes the
/// messages of the node before it and passes on its answer - use
/// <see cref="GetWorkflowAgentStep(IServiceProvider, string, string)"/> instead.
/// </para>
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

        // A host (a ready-made pattern or an AIAgentBinding) turns the agent's tool approval into a workflow
        // request and waits for the answer (K-1051).
        return services.GetRequiredService<WorkflowAgentCache>()
            .Get(workflowName, agentName, description, hostsApproval: true);
    }

    /// <summary>
    /// Binds a catalog agent as a plain step of a workflow graph defined in code - a node that takes
    /// the messages of the node before it and passes on the agent's answer.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="workflowName">The name of the enclosing workflow. Appears in error messages.</param>
    /// <param name="agentName">The name of the agent to run. Also the id of the step.</param>
    /// <returns>The executor binding to put in the graph.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of the names is empty.</exception>
    /// <remarks>
    /// Everything the other overload says applies here too; the step runs with no timeout of its own and the
    /// agent's current version.
    /// </remarks>
    public static ExecutorBinding GetWorkflowAgentStep(
        this IServiceProvider services,
        string workflowName,
        string agentName)
        => GetWorkflowAgentStep(services, workflowName, agentName, static _ => { });

    /// <summary>
    /// Binds a catalog agent as a plain step of a workflow graph defined in code, with the step's own
    /// timeout and version pin.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="workflowName">The name of the enclosing workflow. Appears in error messages.</param>
    /// <param name="agentName">The name of the agent to run. Also the id of the step.</param>
    /// <param name="configure">Sets the step's timeout and version pin; called once, here.</param>
    /// <returns>The executor binding to put in the graph.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">One of the names is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="WorkflowAgentStepOptions.Timeout"/> is outside the range an agent step accepts, or
    /// <see cref="WorkflowAgentStepOptions.AgentVersion"/> is less than 1.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <strong>This is not <see cref="GetWorkflowAgent"/>.</strong> That helper returns an agent that a
    /// ready-made pattern or an <c>AIAgentBinding</c> hosts; the host runs it only when it is sent a
    /// <see cref="TurnToken"/>, and nothing sends one to a node that is not the graph's entry. A step bound
    /// here is a plain function executor: it runs when a message arrives, wherever the edge comes from - a
    /// back edge included - so a revision loop works. The price is that it streams no token deltas into the
    /// workflow's event feed and keeps no Microsoft Agent Framework session; a step that revises a draft
    /// carries the draft and the feedback in the messages it is given.
    /// </para>
    /// <para>
    /// <strong>The id of the step is the agent's name</strong>, as in a stored chain, so an agent is used
    /// once in a graph. The agent runs as a child of the workflow's run: it records its own run row with the
    /// full message and usage history, and the tree's depth, budget and tenant rules apply. The step does not
    /// stream: it asks the agent for its whole answer, so the structured-response repair
    /// (<c>Tracon:StructuredResponse</c>) and a loop agent's final iteration work as they do for a plain
    /// agent run.
    /// </para>
    /// <para>
    /// The step takes and passes on a <c>List&lt;ChatMessage&gt;</c>; put a node between it and a node of
    /// another type.
    /// </para>
    /// <example>
    /// <code>
    /// tracon.AddWorkflow("review-and-revise", services =>
    /// {
    ///     var writer = services.GetWorkflowAgentStep("review-and-revise", "writer", step =>
    ///     {
    ///         step.Timeout = TimeSpan.FromMinutes(3);
    ///         step.AgentVersion = 4;
    ///     });
    ///     var check = services.GetWorkflowFunction("check");
    ///
    ///     return new WorkflowBuilder(writer)
    ///         .AddEdge(writer, check)
    ///         .WithOutputFrom(check)
    ///         .WithName("review-and-revise")
    ///         .Build();
    /// });
    /// </code>
    /// </example>
    /// </remarks>
    public static ExecutorBinding GetWorkflowAgentStep(
        this IServiceProvider services,
        string workflowName,
        string agentName,
        Action<WorkflowAgentStepOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowName);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new WorkflowAgentStepOptions();
        configure(options);

        if (options.Timeout is { } timeout &&
            (!TimerPeriod.IsValid(timeout) || timeout > WorkflowDefinitionValidator.MaxAgentNodeTimeout))
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                timeout,
                $"{nameof(WorkflowAgentStepOptions)}.{nameof(WorkflowAgentStepOptions.Timeout)} of step '{agentName}' in workflow " +
                $"'{workflowName}' must be from 1 to {(long)WorkflowDefinitionValidator.MaxAgentNodeTimeout.TotalMilliseconds} " +
                $"milliseconds: its hard cutoff is the timeout plus {TraconAgentGraphOptions.WaitTimeoutPad.TotalSeconds:0} seconds, " +
                $"and that sum must still fit the timer. Actual value: {timeout}.");
        }

        if (options.AgentVersion is < 1 and var version)
        {
            throw new ArgumentOutOfRangeException(
                nameof(configure),
                version,
                $"{nameof(WorkflowAgentStepOptions)}.{nameof(WorkflowAgentStepOptions.AgentVersion)} of step '{agentName}' in workflow " +
                $"'{workflowName}' is {version}. Versions are numbered from 1; leave it unset to run the agent's current version.");
        }

        var agent = services.GetRequiredService<WorkflowAgentCache>()
            .Get(workflowName, agentName, description: null, options.Timeout, options.AgentVersion);

        return ExecutorBindingExtensions.BindExecutor(new WorkflowAgentStepExecutor(agentName, agent));
    }
}
