using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Turns a <see cref="WorkflowDefinition"/> into a runnable
/// <see cref="Workflow"/> graph.
/// </summary>
/// <remarks>
/// <para>
/// The compiler binds <strong>agents from the catalog</strong>. Every agent is
/// wrapped with <see cref="ChildAgentInvoker"/>; this way every agent that
/// runs inside a workflow attaches under the workflow run through phase 12's
/// tree mechanism, and depth, budget, and tenant limits are applied
/// automatically. This wrapper was written in phase 12 and is <em>reused</em>
/// here: rewriting the sub-execution rules a second time would let the two
/// copies drift apart over time.
/// </para>
/// <para>
/// The wrapper resolves the agent <em>lazily</em>: when an agent definition
/// changes, the compiled workflow does not go stale.
/// </para>
/// </remarks>
internal sealed class WorkflowDefinitionCompiler
{
    private readonly CallableAgentResolver _resolver;
    private readonly WorkflowAgentCache _agents;
    private readonly WorkflowFunctionRegistry _functions;

    /// <summary>Creates a new compiler.</summary>
    /// <param name="resolver">The resolver that resolves agents from the catalog.</param>
    /// <param name="agents">The cache of agent wrappers with a stable identity.</param>
    /// <param name="functions">The registry of code-registered function nodes.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public WorkflowDefinitionCompiler(CallableAgentResolver resolver, WorkflowAgentCache agents, WorkflowFunctionRegistry functions)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(functions);

        _resolver = resolver;
        _agents = agents;
        _functions = functions;
    }

    /// <summary>Turns a definition into a runnable graph.</summary>
    /// <param name="definition">The definition to compile.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The built graph.</returns>
    /// <exception cref="AgentPrismException">
    /// The definition is invalid, or an agent name cannot be found in the catalog.
    /// </exception>
    public async ValueTask<Workflow> CompileAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Structural validation lives in AgentPrism.Core: the HTTP layer
        // applies the same rules at registration time, and writing them twice
        // in two places would let them drift apart.
        WorkflowDefinitionValidator.Require(definition);

        // A definition that mixes agent and function nodes (phase 71) takes a
        // SEPARATE path: it is always Sequential (the validator enforces
        // this) and its graph is hand-built with WorkflowBuilder instead of
        // AgentWorkflowBuilder, which accepts only agents.
        if (definition.Nodes.Count > 0)
        {
            return await BuildMixedSequentialAsync(definition, cancellationToken).ConfigureAwait(false);
        }

        var participants = await BindAsync(definition.Name, definition.AgentNames, cancellationToken)
            .ConfigureAwait(false);

        return definition.Kind switch
        {
            WorkflowKind.Sequential => BuildSequential(definition, participants),
            WorkflowKind.Concurrent => BuildConcurrent(definition, participants),
            WorkflowKind.Handoff => BuildHandoff(definition, participants),
            WorkflowKind.GroupChat => BuildGroupChat(definition, participants),
            WorkflowKind.Magentic => await BuildMagenticAsync(definition, participants, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new AgentPrismException(
                $"Workflow '{definition.Name}' uses an unknown pattern: '{definition.Kind}'."),
        };
    }

    private static Workflow BuildSequential(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
        => AgentWorkflowBuilder.BuildSequential(definition.Name, participants);

    /// <summary>
    /// Hand-builds a Sequential graph that mixes agent and function nodes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AgentWorkflowBuilder.BuildSequential(string, IEnumerable{AIAgent})"/>
    /// only accepts agents; a graph carrying a function node must be
    /// assembled node by node with <see cref="WorkflowBuilder"/> instead.
    /// </para>
    /// <para>
    /// 🚨 <strong>An agent node is bound as a <see cref="WorkflowAgentStepExecutor"/>
    /// (a <see cref="FunctionExecutor{TInput,TOutput}"/> subtype), never as an
    /// <see cref="AIAgentBinding"/>.</strong> Measured (phase 71): an
    /// <c>AIAgentBinding</c> that is not the graph's entry point never calls
    /// its wrapped agent when wired with a plain <c>AddEdge</c> - it accepts
    /// the incoming chat messages but sits idle (no failure, no run row, no
    /// output). Only the entry node receives the <c>TurnToken</c> that
    /// Microsoft Agent Framework's agent-host protocol needs to actually run,
    /// sent once by <see cref="WorkflowRunner"/>; nothing forwards a second
    /// one to a downstream agent host in a hand-built graph, and neither a
    /// manually relayed <c>TurnToken</c> nor MAF's own
    /// <c>ChatForwardingExecutor</c> relay reproduced the ready-made pattern's
    /// internal protocol. Calling the (tree-attached) agent directly inside a
    /// function handler sidesteps the whole protocol: a <c>FunctionExecutor</c>
    /// needs only its input message to run, exactly like every other node in
    /// this graph, which is also why <c>WithOutputFrom</c> now works
    /// uniformly on whichever node is last, with no collector special-case. A
    /// dedicated subtype (rather than a plain <c>FunctionExecutor</c> built
    /// inline) exists so <see cref="WorkflowGraphReader"/> can still tell an
    /// agent step apart from a genuine function node purely from the compiled
    /// graph - the graph is never read from the definition.
    /// </para>
    /// <para>
    /// The trade-off is explicit: an agent step inside a mixed chain does not
    /// stream <c>MessageDelta</c> events into the workflow's own event feed
    /// (streaming a single node's tokens through a boundary that was not
    /// designed to carry them is a larger change than this phase's scope).
    /// The step still runs through <c>RunStreamingAsync</c> so
    /// its own child <c>runs</c> row keeps full message and usage history,
    /// queryable through the tree exactly like any other agent step; only the
    /// live top-level stream loses per-token granularity for that one node.
    /// </para>
    /// </remarks>
    private async ValueTask<Workflow> BuildMixedSequentialAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken)
    {
        var bindings = new List<ExecutorBinding>(definition.Nodes.Count);

        foreach (var node in definition.Nodes)
        {
            if (node.Kind == WorkflowNodeKind.Function)
            {
                // The node's NAME is the executor id, unchanged across every
                // compile of this definition: checkpoint compatibility needs
                // a stable id, and the validator already guarantees every
                // node name in the list is unique.
                bindings.Add(ExecutorBindingExtensions.BindExecutor(_functions.CreateExecutor(node.Name, node.Name)));

                continue;
            }

            var agent = await BindOneAsync(definition.Name, node.Name, cancellationToken).ConfigureAwait(false);

            bindings.Add(ExecutorBindingExtensions.BindExecutor(new WorkflowAgentStepExecutor(node.Name, agent)));
        }

        var builder = new WorkflowBuilder(bindings[0]).WithName(definition.Name);

        for (var index = 1; index < bindings.Count; index++)
        {
            builder = builder.AddEdge(bindings[index - 1], bindings[index]);
        }

        builder = builder.WithOutputFrom(bindings[^1]);

        if (definition.Description is { Length: > 0 } description)
        {
            builder = builder.WithDescription(description);
        }

        return builder.Build();
    }

    private static Workflow BuildConcurrent(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
        => AgentWorkflowBuilder.BuildConcurrent(definition.Name, participants, aggregator: null);

    private static Workflow BuildHandoff(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
    {
        var builder = AgentWorkflowBuilder
            .CreateHandoffBuilderWith(participants[0])
            .AddParticipants(participants.Skip(1))
            .WithName(definition.Name);

        // The first agent may hand off to ALL of the others. If a narrower
        // graph (who may hand off to whom) were definable, the definition
        // model would also have to carry an edge list; phase 15 deliberately
        // does not do this and leaves the free-form graph to code.
        builder = builder.WithHandoffs(participants[0], participants.Skip(1));

        if (definition.Description is { Length: > 0 } description)
        {
            builder = builder.WithDescription(description);
        }

        if (definition.HandoffInstructions is { Length: > 0 } instructions)
        {
            builder = builder.WithHandoffInstructions(instructions);
        }

        // In phase 15 no human intervenes; the handoff chain advances on its
        // own. Without a turn limit, MAF would stop after the first handoff
        // and the user would see the workflow as "stuck halfway".
        builder = builder.WithAutonomousMode(definition.MaxIterations ?? DefaultAutonomousTurns);

        return builder.Build();
    }

    private static Workflow BuildGroupChat(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
    {
        var maxIterations = definition.MaxIterations ?? DefaultGroupChatIterations;

        var builder = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)
            {
                MaximumIterationCount = maxIterations,
            })
            .AddParticipants(participants)
            .WithName(definition.Name);

        if (definition.Description is { Length: > 0 } description)
        {
            builder = builder.WithDescription(description);
        }

        return builder.Build();
    }

    private async ValueTask<Workflow> BuildMagenticAsync(
        WorkflowDefinition definition,
        IReadOnlyList<AIAgent> participants,
        CancellationToken cancellationToken)
    {
        var manager = await BindOneAsync(definition.Name, definition.ManagerAgentName!, cancellationToken)
            .ConfigureAwait(false);

        var builder = AgentWorkflowBuilder
            .CreateMagenticBuilderWith(manager)
            .AddParticipants(participants)
            .WithName(definition.Name)

            // While plan approval is on, MAF publishes a RequestInfoEvent at the
            // end of the first super-step and execution stays in the
            // PendingRequests state (measured in phase 15). Phase 16 satisfies
            // this request: the run closes as AwaitingInput, its state is
            // written to a checkpoint, and the /respond endpoint resumes it
            // from there. The default remains OFF - a run does not wait for a
            // human unless a definition explicitly asks for it.
            .RequirePlanSignoff(definition.RequirePlanApproval);

        if (definition.MaxIterations is { } rounds)
        {
            builder = builder.WithMaxRounds(rounds);
        }

        if (definition.Description is { Length: > 0 } description)
        {
            builder = builder.WithDescription(description);
        }

        return builder.Build();
    }

    /// <summary>The default number of turns the handoff chain advances on its own.</summary>
    private const int DefaultAutonomousTurns = 8;

    /// <summary>The default turn limit for a group chat.</summary>
    private const int DefaultGroupChatIterations = 8;

    private async ValueTask<IReadOnlyList<AIAgent>> BindAsync(
        string workflowName,
        IReadOnlyList<string> agentNames,
        CancellationToken cancellationToken)
    {
        var described = await _resolver.DescribeAsync(agentNames, cancellationToken).ConfigureAwait(false);
        var catalog = await _resolver.ListAsync(cancellationToken).ConfigureAwait(false);
        var known = new HashSet<string>(catalog.Select(static descriptor => descriptor.Name), StringComparer.Ordinal);
        var agents = new List<AIAgent>(described.Count);

        foreach (var info in described)
        {
            if (!known.Contains(info.Name))
            {
                throw new AgentPrismException(
                    $"Workflow '{workflowName}' uses agent '{info.Name}', but no such agent exists in " +
                    "the catalog. Define the agent first, then register the workflow.");
            }

            agents.Add(Wrap(workflowName, info));
        }

        return agents;
    }

    private async ValueTask<AIAgent> BindOneAsync(
        string workflowName,
        string agentName,
        CancellationToken cancellationToken)
    {
        var bound = await BindAsync(workflowName, [agentName], cancellationToken).ConfigureAwait(false);

        return bound[0];
    }

    /// <summary>
    /// Gets the agent from the cache, so the same workflow always produces the
    /// same executor id every time it is compiled, keeping checkpoints compatible.
    /// </summary>
    private ChildAgentInvoker Wrap(string workflowName, CallableAgentInfo info)
        => _agents.Get(workflowName, info.Name, info.Description);
}
