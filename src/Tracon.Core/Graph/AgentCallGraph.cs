namespace Tracon;

/// <summary>
/// Validates an agent call graph at save time.
/// </summary>
/// <remarks>
/// <para>
/// Validation is done with a depth-first search and separates three errors:
/// unknown name, self-call, indirect cycle. All three break the save
/// operation (<c>400 Bad Request</c>); if left to run time, the user only
/// sees the error when they run the agent, in a message that is hard to
/// diagnose.
/// </para>
/// <para>
/// A reference that pins a version (<c>"b"</c> at version 1) is a node of its own: its edges are the callable agents
/// of THAT stored version, not of the agent's current one. Reading the current version for a pinned reference
/// accepted a cycle that exists only through the pin (the run then spent tokens up to the depth limit) and refused
/// a graph whose cycle exists only through the current version the pin steps around.
/// </para>
/// <para>
/// <strong>Static validation alone is not enough.</strong> An agent registered
/// via a code-side factory does not carry a declarative definition and
/// appears as a leaf in the graph; in reality it may still be calling other
/// agents. The second line of defense is the run-time depth counter
/// (<see cref="AgentRunBudget.MaxDepth"/>).
/// </para>
/// </remarks>
internal static class AgentCallGraph
{
    /// <summary>
    /// Validates a definition's call graph and returns a result carrying a machine-readable code.
    /// </summary>
    /// <param name="agentName">Name of the agent being validated.</param>
    /// <param name="callableAgents">The agents this agent wants to call, with their pinned versions.</param>
    /// <param name="descriptors">All agent summaries in the catalog.</param>
    /// <param name="definitions">
    /// The store that keeps stored agents' versions. <see langword="null"/> when the host has none, in which case a
    /// pinned reference has no edges to follow and an unpinned one follows the names the catalog reports.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The code and description when there is a problem; <see langword="null"/> when the graph is valid.
    /// </returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// The validation endpoint takes its code (<c>unknown_agent</c>/<c>cycle</c>) from here, while the save-time
    /// <c>400</c> response only uses <see cref="AgentCallGraphProblem.Message"/>.
    /// </remarks>
    public static async ValueTask<AgentCallGraphProblem?> ValidateAsync(
        string agentName,
        IReadOnlyList<CallableAgentReference> callableAgents,
        IReadOnlyList<AgentDescriptor> descriptors,
        IAgentDefinitionStore? definitions,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(callableAgents);
        ArgumentNullException.ThrowIfNull(descriptors);

        if (callableAgents.Count == 0)
        {
            return null;
        }

        // The definition being validated has not been saved yet, so its
        // catalog entry may be stale. The graph is built with the NEW version
        // of the definition; otherwise, whether a newly added edge creates a
        // cycle would never be seen.
        var byName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            byName[descriptor.Name] = descriptor;
        }

        foreach (var target in callableAgents.Select(static agent => agent.Name))
        {
            if (string.Equals(target, agentName, StringComparison.Ordinal))
            {
                return new AgentCallGraphProblem(
                    ValidationCodes.Cycle,
                    $"'{agentName}' cannot call itself. An agent calling itself is infinite " +
                    "recursion and generates cost until the depth counter runs out.");
            }

            if (!byName.ContainsKey(target))
            {
                return new AgentCallGraphProblem(
                    ValidationCodes.UnknownAgent,
                    $"Agent '{agentName}' wants to call an agent named '{target}', but " +
                    "no such agent exists in the catalog. Create that agent first.");
            }
        }

        var root = new CallNode(agentName, null);
        var edges = new EdgeReader(root, ToNodes(callableAgents), byName, definitions);

        return await FindCycleAsync(root, edges, cancellationToken).ConfigureAwait(false) is { } cycle
            ? new AgentCallGraphProblem(
                ValidationCodes.Cycle,
                $"There is a cycle in the call graph: {string.Join(" -> ", cycle)}. " +
                "A cyclic graph causes the run to continue until it hits the depth limit.")
            : null;
    }

    /// <summary>
    /// Searches the graph for a cycle starting from the given node.
    /// </summary>
    /// <returns>The node sequence of the cycle found; <see langword="null"/> when there is no cycle.</returns>
    /// <remarks>
    /// An explicit stack is used instead of recursion: the call graph is user
    /// data with unbounded depth; a recursive traversal would kill the
    /// process with a <c>StackOverflowException</c> on a sufficiently long chain.
    /// </remarks>
    private static async ValueTask<List<CallNode>?> FindCycleAsync(
        CallNode start,
        EdgeReader edges,
        CancellationToken cancellationToken)
    {
        var path = new List<CallNode>();
        var onPath = new HashSet<CallNode>();
        var visited = new HashSet<CallNode>();
        var stack = new Stack<Frame>();

        stack.Push(new Frame(start, await edges.ReadAsync(start, cancellationToken).ConfigureAwait(false), 0));
        path.Add(start);
        onPath.Add(start);

        while (stack.Count > 0)
        {
            var frame = stack.Pop();

            if (frame.Index >= frame.Children.Count)
            {
                onPath.Remove(frame.Node);
                visited.Add(frame.Node);

                if (path.Count > 0)
                {
                    path.RemoveAt(path.Count - 1);
                }

                continue;
            }

            stack.Push(frame with { Index = frame.Index + 1 });

            var child = frame.Children[frame.Index];

            if (onPath.Contains(child))
            {
                var cycle = new List<CallNode>(path.Count + 1);
                cycle.AddRange(path);
                cycle.Add(child);

                return cycle;
            }

            if (visited.Contains(child))
            {
                continue;
            }

            stack.Push(new Frame(child, await edges.ReadAsync(child, cancellationToken).ConfigureAwait(false), 0));
            path.Add(child);
            onPath.Add(child);
        }

        return null;
    }

    private static List<CallNode> ToNodes(IReadOnlyList<CallableAgentReference> references)
        => [.. references.Select(static reference => new CallNode(reference.Name, reference.Version))];

    private readonly record struct Frame(CallNode Node, IReadOnlyList<CallNode> Children, int Index);

    /// <summary>One agent at one version; a null version is the agent's current one.</summary>
    private readonly record struct CallNode(string Name, int? Version)
    {
        public override string ToString() => Version is { } version ? $"{Name}@{version}" : Name;
    }

    /// <summary>Reads the callable agents of a node: the definition being validated, a stored version, or the current one.</summary>
    private sealed class EdgeReader(
        CallNode root,
        IReadOnlyList<CallNode> rootEdges,
        Dictionary<string, AgentDescriptor> descriptors,
        IAgentDefinitionStore? definitions)
    {
        public async ValueTask<IReadOnlyList<CallNode>> ReadAsync(CallNode node, CancellationToken cancellationToken)
        {
            if (node == root)
            {
                return rootEdges;
            }

            if (node.Version is { } version)
            {
                // A version that does not exist is CallableAgentPins' finding, and a host without a store keeps no
                // versions to read.
                return definitions is not null
                    && await definitions.GetVersionAsync(node.Name, version, cancellationToken).ConfigureAwait(false) is { } stored
                        ? ToNodes(stored.CallableAgents)
                        : [];
            }

            if (!descriptors.TryGetValue(node.Name, out var descriptor) || descriptor.CallableAgentNames.Count == 0)
            {
                return [];
            }

            // The descriptor reports names only; the stored current definition also carries the pins of its own callees.
            // A code-defined agent has no stored definition and keeps the names.
            return definitions is not null
                && await definitions.GetAsync(node.Name, cancellationToken).ConfigureAwait(false) is { } current
                    ? ToNodes(current.CallableAgents)
                    : [.. descriptor.CallableAgentNames.Select(static name => new CallNode(name, null))];
        }
    }
}

/// <summary>The machine-readable result of a call graph validation.</summary>
/// <param name="Code">Stable code: <c>unknown_agent</c> or <c>cycle</c>.</param>
/// <param name="Message">Human-readable description.</param>
internal readonly record struct AgentCallGraphProblem(string Code, string Message);
