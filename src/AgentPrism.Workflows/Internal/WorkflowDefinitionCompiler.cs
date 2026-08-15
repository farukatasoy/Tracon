using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
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

    /// <summary>Creates a new compiler.</summary>
    /// <param name="resolver">The resolver that resolves agents from the catalog.</param>
    /// <param name="agents">The cache of agent wrappers with a stable identity.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public WorkflowDefinitionCompiler(CallableAgentResolver resolver, WorkflowAgentCache agents)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(agents);

        _resolver = resolver;
        _agents = agents;
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
