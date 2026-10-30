using System.Text.Json;
using Microsoft.Extensions.AI;

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
/// <para>
/// <see cref="ValidateNodeTypes"/> adds the rules that need the function
/// registry: each node of a node list must take what the node before it
/// passes on, and the first node must not take <see cref="object"/>. The registry is fixed for the process lifetime, so the
/// save-time check is an honest guarantee, and the compiler repeats it.
/// </para>
/// </remarks>
internal static class WorkflowDefinitionValidator
{
    /// <summary>
    /// The workflow name a fixed management route already uses:
    /// <c>GET /api/workflows/functions</c> matches before <c>/api/workflows/{name}</c>,
    /// in any letter case, so a workflow saved under it could never be read back.
    /// </summary>
    public const string ReservedFunctionListName = "functions";

    /// <summary>
    /// The second fixed management route: <c>GET /api/workflows/requests</c> lists the
    /// tenant's human requests and matches before <c>/api/workflows/{name}</c> the same way.
    /// </summary>
    public const string ReservedRequestListName = "requests";

    /// <summary>Refuses a name a fixed management route already uses.</summary>
    /// <param name="name">The workflow name.</param>
    /// <returns>User-facing error text; <see langword="null"/> when the name is free.</returns>
    /// <remarks>
    /// Asked where a workflow is SAVED (the HTTP endpoint and <c>AddWorkflow</c>), not by
    /// the compiler: a workflow stored under the name before the rule existed can still
    /// be run by name; it only cannot be read back.
    /// </remarks>
    public static string? ReservedNameProblem(string name)
    {
        if (string.Equals(name, ReservedFunctionListName, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{name}' is reserved: GET /api/workflows/{ReservedFunctionListName} lists the registered " +
                   "function nodes, so a workflow under that name could not be read back. Choose another name.";
        }

        return string.Equals(name, ReservedRequestListName, StringComparison.OrdinalIgnoreCase)
            ? $"'{name}' is reserved: GET /api/workflows/{ReservedRequestListName} lists the human requests the " +
              "tenant's workflows wait on, so a workflow under that name could not be read back. Choose another name."
            : null;
    }

    /// <summary>The shortest time a human request may be given to wait: one minute.</summary>
    public static readonly TimeSpan MinRequestTimeout = TimeSpan.FromMinutes(1);

    /// <summary>The longest time a human request may be given to wait: 365 days.</summary>
    public static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromDays(365);

    /// <summary>Reports whether a request timeout is within the range a workflow may set.</summary>
    /// <param name="value">The timeout.</param>
    /// <returns><see langword="true"/> from <see cref="MinRequestTimeout"/> to <see cref="MaxRequestTimeout"/>.</returns>
    public static bool IsValidRequestTimeout(TimeSpan value)
        => value >= MinRequestTimeout && value <= MaxRequestTimeout;

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

        // Phase 208: every kind may choose a response mode (a workflow's requests can come from
        // a plan approval or, in code, from any port), so only a value outside the enum is
        // refused - a number sent on the wire would otherwise be stored as a mode no rule reads.
        if (!Enum.IsDefined(definition.ResponseMode))
        {
            return $"Workflow '{definition.Name}' uses an unknown response mode: '{definition.ResponseMode}'. " +
                   "Use 'Branch' or 'SingleAnswer'.";
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

                // Settings belong to a function node (F-295). An agent's behavior
                // lives in its own definition; letting a node carry a value that
                // nothing reads would look like a setting that works.
                if (node.Settings is { } settings)
                {
                    if (node.Kind != WorkflowNodeKind.Function)
                    {
                        return $"Node '{node.Name}' in workflow '{definition.Name}' is an agent node but carries " +
                               "'settings'. Settings belong to function nodes only; an agent's behavior is set " +
                               "in the agent's own definition.";
                    }

                    // Whether the object fits the function's settings type is checked
                    // against the function registry (IWorkflowFunctionCatalog), which
                    // Core cannot see. Only the shape every function shares is here.
                    if (settings.ValueKind != JsonValueKind.Object)
                    {
                        return $"Node '{node.Name}' in workflow '{definition.Name}' has 'settings' that are " +
                               $"a JSON {settings.ValueKind.ToString().ToLowerInvariant()}. " +
                               "Settings must be a JSON object, for example {\"voice\": \"alloy\"}.";
                    }
                }

                if (NodeTimeoutProblem(definition.Name, node) is { } timeoutProblem)
                {
                    return timeoutProblem;
                }

                if (NodeAgentVersionProblem(definition.Name, node) is { } versionProblem)
                {
                    return versionProblem;
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

        // Phase 270. The topology and its limit belong to the Handoff pattern, the two Magentic limits to Magentic. A
        // field that reached another pattern would look like a setting that works while nothing reads it, the same
        // rule as handoffInstructions above.
        if (definition.Kind != WorkflowKind.Handoff)
        {
            if (definition.Handoffs.Count > 0)
            {
                return $"Workflow '{definition.Name}' does not use 'handoffs' in the '{definition.Kind}' pattern. " +
                       "This field belongs only to the 'Handoff' pattern.";
            }

            if (definition.MaxHandoffs is not null)
            {
                return $"Workflow '{definition.Name}' does not use 'maxHandoffs' in the '{definition.Kind}' pattern. " +
                       "This field belongs only to the 'Handoff' pattern.";
            }
        }

        if (definition.Kind != WorkflowKind.Magentic)
        {
            if (definition.MaxStalls is not null)
            {
                return $"Workflow '{definition.Name}' does not use 'maxStalls' in the '{definition.Kind}' pattern. " +
                       "This field belongs only to the 'Magentic' pattern.";
            }

            if (definition.MaxResets is not null)
            {
                return $"Workflow '{definition.Name}' does not use 'maxResets' in the '{definition.Kind}' pattern. " +
                       "This field belongs only to the 'Magentic' pattern.";
            }
        }

        if (definition.MaxHandoffs is <= 0)
        {
            return $"Workflow '{definition.Name}''s 'maxHandoffs' value must be positive.";
        }

        if (definition.MaxStalls is <= 0)
        {
            return $"Workflow '{definition.Name}''s 'maxStalls' value must be positive.";
        }

        // Measured (MAF 1.26.0): the orchestrator compares the limit with the count of replans at the START of every
        // round, so a limit of 0 stops the task before its first round with a message instead of a result. A limit of 1
        // lets the manager replan once and stops at the round after.
        if (definition.MaxResets is <= 0)
        {
            return $"Workflow '{definition.Name}''s 'maxResets' value must be positive.";
        }

        if (definition.Kind == WorkflowKind.Handoff && HandoffProblem(definition, seen) is { } handoffProblem)
        {
            return handoffProblem;
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

        // F-405: the run's deadline is armed with this value, and a timer refuses a
        // value outside its range where it is armed - in a run, long after the save.
        // The ceiling (MaxRunTimeout) is the host's setting, so it is checked where the
        // setting is known: ValidateRunTimeoutCeiling.
        if (definition.RunTimeout is { } runTimeout && !TimerPeriod.IsValid(runTimeout))
        {
            return $"Workflow '{definition.Name}' has a 'runTimeout' of {runTimeout}. It must be from 1 to " +
                   $"{TimerPeriod.MaxMilliseconds} milliseconds (about 49.7 days); leave it out to use the " +
                   "installation's 'Tracon:Workflows:RunTimeout'.";
        }

        // Phase 209: the limit a human request may wait. It arms no timer (the expiry service
        // compares times), so the bound is a product rule, not a timer range: an unbounded value
        // would reopen the growth of a waiting run's rows that the limit exists to close (K-888).
        if (definition.RequestTimeout is { } requestTimeout && !IsValidRequestTimeout(requestTimeout))
        {
            return $"Workflow '{definition.Name}' has a 'requestTimeout' of {requestTimeout}. It must be from " +
                   $"{MinRequestTimeout} to {MaxRequestTimeout.TotalDays:0} days; leave it out and a request waits without limit.";
        }

        // Structure only (F-294): valid identifiers, no repeats. Whether the
        // participants' required parameters are covered is checked when a run
        // STARTS - the participants live in the agent catalog, which can change
        // after this definition is saved, so a save-time check would be a false
        // guarantee (the same rule as the agent names above). Kind is a label
        // for the console; every value travels as a string, so there is no
        // default-versus-kind rule, exactly as for an agent's schema.
        if (AgentParameterValidator.ValidateNames(definition.Parameters) is { Count: > 0 } parameterErrors)
        {
            return $"Workflow '{definition.Name}' has an invalid parameter schema: {string.Join(" ", parameterErrors)}";
        }

        return null;
    }

    /// <summary>Checks the handoff edge list of a Handoff definition.</summary>
    /// <param name="definition">The definition. Its <see cref="WorkflowDefinition.AgentNames"/> were already checked.</param>
    /// <param name="agentNames">The agent names of the definition.</param>
    /// <returns>User-facing error text; <see langword="null"/> when the list is empty or valid.</returns>
    /// <remarks>
    /// A message names the field and the edge by its place in the list, <c>handoffs[2]</c>, so a client
    /// that sent a long list sees which entry to change. A pair twice is refused here because Microsoft
    /// Agent Framework throws for it when the graph is built, in a run, long after the save.
    /// </remarks>
    private static string? HandoffProblem(WorkflowDefinition definition, HashSet<string> agentNames)
    {
        if (definition.Handoffs.Count == 0)
        {
            return null;
        }

        var pairs = new HashSet<(string From, string To)>();
        var targets = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        for (var index = 0; index < definition.Handoffs.Count; index++)
        {
            var edge = definition.Handoffs[index];

            if (edge is null)
            {
                return $"Workflow '{definition.Name}': handoffs[{index}] is empty. Remove it or give it a 'from' and a 'to'.";
            }

            foreach (var (field, name) in new[] { ("from", edge.From), ("to", edge.To) })
            {
                if (name is null || !agentNames.Contains(name))
                {
                    return $"Workflow '{definition.Name}': handoffs[{index}] {field} '{name}' is not in agentNames. " +
                           "An edge may only connect agents the workflow lists.";
                }
            }

            if (string.Equals(edge.From, edge.To, StringComparison.Ordinal))
            {
                return $"Workflow '{definition.Name}': handoffs[{index}] hands '{edge.From}' off to itself. " +
                       "An edge must connect two different agents.";
            }

            if (!pairs.Add((edge.From, edge.To)))
            {
                return $"Workflow '{definition.Name}': handoffs[{index}] repeats the edge from '{edge.From}' to '{edge.To}'. " +
                       "Each pair may appear once.";
            }

            if (edge.Reason is not null && string.IsNullOrWhiteSpace(edge.Reason))
            {
                return $"Workflow '{definition.Name}': handoffs[{index}] has a blank 'reason'. " +
                       "Leave it out to use the description of the target agent.";
            }

            if (!targets.TryGetValue(edge.From, out var list))
            {
                targets[edge.From] = list = [];
            }

            list.Add(edge.To);
        }

        // The first agent receives the user's message. An edge list it has no part in would leave the
        // coordinator a dead end for every other agent.
        var first = definition.AgentNames[0];

        if (!targets.ContainsKey(first))
        {
            return $"Workflow '{definition.Name}': the first agent '{first}' (the one that receives the user's message) " +
                   "has no handoff out of it, so the conversation could never leave it. Add an edge from it.";
        }

        var reached = new HashSet<string>(StringComparer.Ordinal) { first };
        var pending = new Queue<string>();
        pending.Enqueue(first);

        while (pending.TryDequeue(out var current))
        {
            if (!targets.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var target in next)
            {
                if (reached.Add(target))
                {
                    pending.Enqueue(target);
                }
            }
        }

        foreach (var name in definition.AgentNames)
        {
            if (!reached.Contains(name))
            {
                return $"Workflow '{definition.Name}': agent '{name}' cannot be reached from the first agent '{first}' " +
                       "by following the handoffs, so it would never run. Add an edge that leads to it, or remove it from agentNames.";
            }
        }

        return null;
    }

    /// <summary>
    /// The longest timeout an agent node may carry: its hard cutoff is the timeout plus
    /// <see cref="TraconAgentGraphOptions.WaitTimeoutPad"/>, and that sum must still fit
    /// a timer.
    /// </summary>
    public static TimeSpan MaxAgentNodeTimeout { get; } =
        TimeSpan.FromMilliseconds(TimerPeriod.MaxMilliseconds) - TraconAgentGraphOptions.WaitTimeoutPad;

    /// <summary>Checks one node's timeout against the range its kind allows.</summary>
    /// <param name="workflowName">The workflow the node belongs to, for the message.</param>
    /// <param name="node">The node.</param>
    /// <returns>User-facing error text; <see langword="null"/> when the node has no timeout or a valid one.</returns>
    /// <remarks>
    /// Not checked against the run's timeout: whichever ends first stops the node. A
    /// function node's timeout arms a delay of exactly this value; an agent node's arms
    /// the child deadline and, 30 seconds later, the hard cutoff.
    /// </remarks>
    // F-303 / F-405, open question 12-B: the two timeouts answer different questions.
    public static string? NodeTimeoutProblem(string workflowName, WorkflowNodeReference node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Timeout is not { } timeout)
        {
            return null;
        }

        if (node.Kind == WorkflowNodeKind.Agent)
        {
            return TimerPeriod.IsValid(timeout) && timeout <= MaxAgentNodeTimeout
                ? null
                : $"Node '{node.Name}' in workflow '{workflowName}' has a 'timeout' of {timeout}. An agent node's " +
                  $"timeout must be from 1 to {(long)MaxAgentNodeTimeout.TotalMilliseconds} milliseconds: its hard " +
                  "cutoff is the timeout plus 30 seconds, and both must fit a timer. Leave it out to use " +
                  "'Tracon:AgentGraph:ChildDeadline'.";
        }

        return TimerPeriod.IsValid(timeout)
            ? null
            : $"Node '{node.Name}' in workflow '{workflowName}' has a 'timeout' of {timeout}. A function node's " +
              $"timeout must be from 1 to {TimerPeriod.MaxMilliseconds} milliseconds (about 49.7 days). Leave it " +
              "out to use the timeout the function was registered with.";
    }

    /// <summary>Checks one node's agent version pin against the kind that may carry it.</summary>
    /// <param name="workflowName">The workflow the node belongs to, for the message.</param>
    /// <param name="node">The node.</param>
    /// <returns>User-facing error text; <see langword="null"/> when the node has no pin or a valid one.</returns>
    /// <remarks>
    /// Only the shape every pin shares is here. Whether the agent has that version is
    /// judged against the agent catalog (<see cref="AgentVersionPin"/>), which a pure
    /// function of the definition cannot see.
    /// </remarks>
    public static string? NodeAgentVersionProblem(string workflowName, WorkflowNodeReference node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.AgentVersion is not { } version)
        {
            return null;
        }

        if (node.Kind != WorkflowNodeKind.Agent)
        {
            return $"Node '{node.Name}' in workflow '{workflowName}' is a function node but carries an " +
                   "'agentVersion'. A version pin belongs to agent nodes only; a function is code and has no versions.";
        }

        return version >= 1
            ? null
            : $"Node '{node.Name}' in workflow '{workflowName}' has an 'agentVersion' of {version}. " +
              "Versions are numbered from 1. Leave it out to run the agent's current version.";
    }

    /// <summary>
    /// Checks a definition's run timeout against the host's ceiling, at save time.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="installationRunTimeout"><c>Tracon:Workflows:RunTimeout</c>.</param>
    /// <param name="maxRunTimeout"><c>Tracon:Workflows:MaxRunTimeout</c>; <see langword="null"/> when not set.</param>
    /// <returns>User-facing error text; <see langword="null"/> when the value fits.</returns>
    /// <remarks>
    /// The range is checked by <see cref="Validate"/>; this is the ceiling only, which
    /// needs the host's settings. With no ceiling, a definition may only shorten the
    /// installation's run timeout.
    /// </remarks>
    // F-405, open question 11-B: a running workflow holds a slot every tenant shares.
    public static string? RunTimeoutCeilingProblem(
        WorkflowDefinition definition,
        TimeSpan installationRunTimeout,
        TimeSpan? maxRunTimeout)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.RunTimeout is not { } runTimeout)
        {
            return null;
        }

        var ceiling = RunTimeoutCeiling(installationRunTimeout, maxRunTimeout);

        if (runTimeout <= ceiling)
        {
            return null;
        }

        return maxRunTimeout is null
            ? $"Workflow '{definition.Name}' has a 'runTimeout' of {runTimeout}, above the installation's run timeout " +
              $"({installationRunTimeout}). A definition may only shorten 'Tracon:Workflows:RunTimeout' unless the host " +
              "sets 'Tracon:Workflows:MaxRunTimeout': a running workflow holds a slot every tenant shares."
            : $"Workflow '{definition.Name}' has a 'runTimeout' of {runTimeout}, above the host's ceiling " +
              $"'Tracon:Workflows:MaxRunTimeout' ({ceiling}).";
    }

    /// <summary>Gets the longest run timeout a definition may set.</summary>
    /// <param name="installationRunTimeout"><c>Tracon:Workflows:RunTimeout</c>.</param>
    /// <param name="maxRunTimeout"><c>Tracon:Workflows:MaxRunTimeout</c>; <see langword="null"/> when not set.</param>
    /// <returns>The ceiling: the host's, or the installation's run timeout when the host set none.</returns>
    public static TimeSpan RunTimeoutCeiling(TimeSpan installationRunTimeout, TimeSpan? maxRunTimeout)
        => maxRunTimeout is { } ceiling && ceiling > installationRunTimeout ? ceiling : installationRunTimeout;

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

    /// <summary>
    /// The type of the message a workflow run sends to its first node: the user's
    /// message as a chat message list (<c>WorkflowRunner</c> builds it).
    /// </summary>
    public static Type RunInputType { get; } = typeof(List<ChatMessage>);

    /// <summary>
    /// The type an agent node of a node list takes and passes on
    /// (<c>WorkflowAgentStepExecutor</c>).
    /// </summary>
    public static Type AgentNodeMessageType { get; } = typeof(List<ChatMessage>);

    /// <summary>
    /// Validates that every node of a node list can take what the node before it
    /// passes on, and that the first node can take the run's message and nothing else.
    /// </summary>
    /// <param name="definition">The definition. Only its <see cref="WorkflowDefinition.Nodes"/> are read.</param>
    /// <param name="functions">The registered functions; <see langword="null"/> when none are registered.</param>
    /// <returns>
    /// User-facing error text that names both nodes and both types;
    /// <see langword="null"/> when the chain fits or the definition lists no nodes.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// <strong>Microsoft Agent Framework drops a message that no handler of the next
    /// node takes, with no error</strong> (measured on 1.26.0, decompiled 2026-10-28; the delivery code is what the version before had): the next node
    /// never runs and the run ends <c>Completed</c> with no output. A first node that
    /// cannot take <see cref="RunInputType"/> runs nothing at all. Both shapes passed
    /// every check, so the save said <c>200</c> to a chain that could not run.
    /// </para>
    /// <para>
    /// The rule follows how the framework delivers. A message reaches a node whose input
    /// type it can be assigned to: a node that takes <c>IEnumerable&lt;ChatMessage&gt;</c>
    /// or <see cref="object"/> receives a <c>List&lt;ChatMessage&gt;</c>. A node sends
    /// exactly the type it declares (a function that declares an interface and returns a
    /// class fails at run time, loudly), except a node that declares <see cref="object"/>:
    /// it may send any type, so the node after it is not checked.
    /// </para>
    /// <para>
    /// <strong>The first node must not take <see cref="object"/></strong> (seen
    /// on 1.26.0). The engine sends Microsoft Agent Framework's turn token to the first
    /// node after the run's message, on every start and every resume: a resume without it
    /// waits for its deadline when the checkpoint has nothing left to run. A node that
    /// takes <see cref="object"/> receives the token as a second input and runs twice, so
    /// a node that writes to an outside system or calls a paid service does that work
    /// twice. No other input type takes the token: it derives from <see cref="object"/>
    /// only and implements no interface (a test in <c>Tracon.Workflows.UnitTests</c> pins
    /// that for every upgrade).
    /// </para>
    /// <para>
    /// The HTTP save calls this with the function registry, and so does the compiler
    /// when a run starts: a function's types can change between deployments. A function
    /// that is not registered is skipped here, because the name check reports it.
    /// </para>
    /// </remarks>
    public static string? ValidateNodeTypes(WorkflowDefinition definition, IWorkflowFunctionCatalog? functions)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Nodes.Count == 0)
        {
            return null;
        }

        var descriptors = (functions?.List() ?? [])
            .ToDictionary(static descriptor => descriptor.Name, StringComparer.Ordinal);

        WorkflowNodeReference? previous = null;
        var passedOn = RunInputType;

        foreach (var node in definition.Nodes)
        {
            Type takes;
            Type sends;

            if (node.Kind == WorkflowNodeKind.Agent)
            {
                takes = AgentNodeMessageType;
                sends = AgentNodeMessageType;
            }
            else if (descriptors.TryGetValue(node.Name, out var descriptor))
            {
                takes = descriptor.InputType;
                sends = descriptor.OutputType;
            }
            else
            {
                // Not registered: the name check reports it, and nothing is known
                // about what it would pass on.
                previous = node;
                passedOn = typeof(object);

                continue;
            }

            if (previous is null && takes == typeof(object))
            {
                return $"Node '{node.Name}' is the first node of workflow '{definition.Name}' and takes " +
                       $"{Display(takes)}. A run sends the first node the user's message and then Microsoft Agent " +
                       "Framework's turn token, on every start and every resume, and a node that takes " +
                       $"{Display(takes)} receives both: '{node.Name}' would run twice. The first node must take " +
                       $"{Display(RunInputType)} or {Display(typeof(IEnumerable<ChatMessage>))}; an agent node does.";
            }

            if (passedOn != typeof(object) && !takes.IsAssignableFrom(passedOn))
            {
                return previous is null
                    ? $"Node '{node.Name}' is the first node of workflow '{definition.Name}' and takes " +
                      $"{Display(takes)}, but a run starts by sending the user's message as {Display(RunInputType)}. " +
                      "Microsoft Agent Framework drops a message no handler takes, so nothing would run and the " +
                      $"run would end with no output. The first node must take {Display(RunInputType)}; an agent node does."
                    : $"Node '{node.Name}' in workflow '{definition.Name}' takes {Display(takes)}, but node " +
                      $"'{previous.Name}' before it passes on {Display(passedOn)}. Microsoft Agent Framework drops a " +
                      $"message no handler takes, so '{node.Name}' would never run and the run would end with no " +
                      $"output. Put a function between them that turns {Display(passedOn)} into {Display(takes)}, " +
                      "or change one of the two types.";
            }

            previous = node;
            passedOn = sends;
        }

        return null;
    }

    /// <summary>A type name an operator can read: generic arguments in angle brackets, no assembly names.</summary>
    private static string Display(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var name = type.GetGenericTypeDefinition().FullName ?? type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);

        return $"{(tick < 0 ? name : name[..tick])}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>";
    }
}
