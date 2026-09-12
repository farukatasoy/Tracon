namespace Tracon;

/// <summary>
/// Validates the structural validity of a workflow definition.
/// </summary>
/// <remarks>
/// <para>
/// The check lives inside <c>Tracon.Core</c> because it has <strong>two
/// consumers</strong>: the HTTP layer produces a <c>400</c> at registration
/// time, and the workflow compiler throws an exception at run time. If
/// written separately in two places, one could change while the other stayed behind.
/// </para>
/// <para>
/// Only <em>structural</em> rules are checked here: name, count, duplicates,
/// pattern-field compatibility. Whether the agents actually exist in the
/// catalog is checked at compile time; the catalog can change between
/// registration and execution, and moving that check to registration time
/// would give a false guarantee.
/// </para>
/// </remarks>
public static class WorkflowDefinitionValidator
{
    /// <summary>Validates the definition.</summary>
    /// <param name="definition">The definition to validate.</param>
    /// <returns>
    /// User-facing error text; <see langword="null"/> if the definition is valid.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public static string? Validate(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            return "The workflow definition's 'name' field is required.";
        }

        if (!Enum.IsDefined(definition.Kind))
        {
            return $"Workflow '{definition.Name}' uses an unknown pattern: '{definition.Kind}'.";
        }

        HashSet<string> seen;

        // A definition either lists plain agent names (AgentNames, every
        // pattern) or an ordered mix of agent and function nodes (Nodes,
        // Sequential only, phase 71). The two are mutually exclusive: letting
        // both be set would raise the question of which one the compiler
        // should trust, and a silent "one wins" rule invites the exact class
        // of surprise K1 exists to rule out.
        if (definition.Nodes.Count > 0)
        {
            if (definition.AgentNames.Count > 0)
            {
                return $"Workflow '{definition.Name}' sets both 'agentNames' and 'nodes'. " +
                       "A definition that uses function nodes must list every node - agents included - " +
                       "in 'nodes'; 'agentNames' must stay empty.";
            }

            if (definition.Kind != WorkflowKind.Sequential)
            {
                return $"Workflow '{definition.Name}' uses 'nodes' with the '{definition.Kind}' pattern. " +
                       "Function nodes are only supported in the 'Sequential' pattern: Microsoft Agent " +
                       "Framework's ready-made builders for the other patterns accept only agents.";
            }

            seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in definition.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Name))
                {
                    return $"Workflow '{definition.Name}' has an empty name in its node list.";
                }

                if (node.Kind is not (WorkflowNodeKind.Agent or WorkflowNodeKind.Function))
                {
                    return $"Node '{node.Name}' in workflow '{definition.Name}' uses kind '{node.Kind}'. " +
                           "A definition's node list may only reference an 'Agent' or a 'Function' node; " +
                           "the other kinds are added by the compiler, never named directly.";
                }

                // Same rationale as the AgentNames duplicate check below: a
                // repeated node name would produce two indistinguishable
                // nodes in the UI and the event stream.
                if (!seen.Add(node.Name))
                {
                    return $"Node '{node.Name}' appears more than once in workflow '{definition.Name}'. " +
                           "A workflow's node list must have no duplicates.";
                }
            }
        }
        else
        {
            if (definition.AgentNames.Count == 0)
            {
                return $"Workflow '{definition.Name}' has no agents. " +
                       "'agentNames' must carry at least one name.";
            }

            seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var agentName in definition.AgentNames)
            {
                if (string.IsNullOrWhiteSpace(agentName))
                {
                    return $"Workflow '{definition.Name}' has an empty name in its agent list.";
                }

                // A duplicate name is deliberately rejected. Microsoft Agent
                // Framework derives executor identities FROM the agent INSTANCE;
                // the same name appearing twice would produce two nodes that
                // cannot be distinguished in the UI or the event stream. If a
                // role repeats, a second agent must be defined.
                if (!seen.Add(agentName))
                {
                    return $"Agent '{agentName}' appears more than once in workflow '{definition.Name}'. " +
                           "A workflow's agent list must have no duplicates; define a second agent if the " +
                           "same role is needed twice.";
                }
            }

            if (definition.Kind is WorkflowKind.Concurrent or WorkflowKind.Handoff or WorkflowKind.GroupChat &&
                definition.AgentNames.Count < 2)
            {
                return $"Workflow '{definition.Name}' uses the '{definition.Kind}' pattern, which requires " +
                       $"at least two agents; the list has {definition.AgentNames.Count}.";
            }
        }

        if (definition.Kind == WorkflowKind.Magentic)
        {
            if (string.IsNullOrWhiteSpace(definition.ManagerAgentName))
            {
                return $"Workflow '{definition.Name}' uses the 'Magentic' pattern, and 'managerAgentName' " +
                       "is required. The manager agent builds the plan, tracks progress, and replans as needed.";
            }

            if (seen.Contains(definition.ManagerAgentName))
            {
                return $"In workflow '{definition.Name}', '{definition.ManagerAgentName}' appears as both " +
                       "manager and participant. The manager directs participants; it cannot direct itself.";
            }
        }

        // GroupChat's manager is NOT an AGENT: it is the round-robin manager
        // on the code side that distributes turn order. If the field is
        // given, the user expects something from it, and silently ignoring
        // it would mislead them.
        else if (!string.IsNullOrWhiteSpace(definition.ManagerAgentName))
        {
            return $"Workflow '{definition.Name}' does not use 'managerAgentName' in the " +
                   $"'{definition.Kind}' pattern. This field belongs only to the 'Magentic' pattern; " +
                   "'GroupChat' distributes turn order with a round-robin manager on the code side.";
        }

        if (definition.Kind != WorkflowKind.Handoff && !string.IsNullOrWhiteSpace(definition.HandoffInstructions))
        {
            return $"Workflow '{definition.Name}' does not use 'handoffInstructions' in the " +
                   $"'{definition.Kind}' pattern. This field belongs only to the 'Handoff' pattern.";
        }

        // Plan approval is a Magentic-only concept: it shows the human the
        // plan built by the manager agent. Other patterns have no such thing
        // as a plan, and silently ignoring the field would hide the fact
        // that the approval step the user expected never occurred - the same
        // rule as managerAgentName.
        if (definition.RequirePlanApproval && definition.Kind != WorkflowKind.Magentic)
        {
            return $"Workflow '{definition.Name}' does not use 'requirePlanApproval' in the " +
                   $"'{definition.Kind}' pattern. Plan approval belongs only to the 'Magentic' pattern; " +
                   "the manager agent that builds a plan exists only in that pattern.";
        }

        if (definition.MaxIterations is <= 0)
        {
            return $"Workflow '{definition.Name}''s 'maxIterations' value must be positive.";
        }

        return null;
    }

    /// <summary>Validates the definition and throws if it is invalid.</summary>
    /// <param name="definition">The definition to validate.</param>
    /// <exception cref="TraconException">The definition is invalid.</exception>
    public static void Require(WorkflowDefinition definition)
    {
        if (Validate(definition) is { } message)
        {
            throw new TraconException(message);
        }
    }
}
