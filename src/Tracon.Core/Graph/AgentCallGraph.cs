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
    /// Validates a definition's call graph.
    /// </summary>
    /// <param name="agentName">Name of the agent being validated.</param>
    /// <param name="callableAgentNames">Names of the agents this agent wants to call.</param>
    /// <param name="descriptors">All agent summaries in the catalog.</param>
    /// <returns>
    /// A user-displayable description when there is a problem; <see langword="null"/>
    /// when the graph is valid.
    /// </returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static string? Validate(
        string agentName,
        IReadOnlyList<string> callableAgentNames,
        IReadOnlyList<AgentDescriptor> descriptors)
        => ValidateDetailed(agentName, callableAgentNames, descriptors)?.Message;

    /// <summary>
    /// Validates a definition's call graph and returns a result carrying a
    /// machine-readable code.
    /// </summary>
    /// <param name="agentName">Name of the agent being validated.</param>
    /// <param name="callableAgentNames">Names of the agents this agent wants to call.</param>
    /// <param name="descriptors">All agent summaries in the catalog.</param>
    /// <returns>
    /// The code and description when there is a problem; <see langword="null"/>
    /// when the graph is valid.
    /// </returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// Performs <strong>the same validation</strong> as <see cref="Validate"/>;
    /// The validation endpoint takes its code (<c>unknown_agent</c>/<c>cycle</c>)
    /// from here, while the save-time <c>400</c> response only uses
    /// <see cref="AgentCallGraphProblem.Message"/>.
    /// </remarks>
    public static AgentCallGraphProblem? ValidateDetailed(
        string agentName,
        IReadOnlyList<string> callableAgentNames,
        IReadOnlyList<AgentDescriptor> descriptors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(callableAgentNames);
        ArgumentNullException.ThrowIfNull(descriptors);

        if (callableAgentNames.Count == 0)
        {
            return null;
        }

        // The definition being validated has not been saved yet, so its
        // catalog entry may be stale. The graph is built with the NEW version
        // of the definition; otherwise, whether a newly added edge creates a
        // cycle would never be seen.
        var edges = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            known.Add(descriptor.Name);
            edges[descriptor.Name] = descriptor.CallableAgentNames;
        }

        known.Add(agentName);
        edges[agentName] = callableAgentNames;

        foreach (var target in callableAgentNames)
        {
            if (string.Equals(target, agentName, StringComparison.Ordinal))
            {
                return new AgentCallGraphProblem(
                    "cycle",
                    $"'{agentName}' cannot call itself. An agent calling itself is infinite " +
                    "recursion and generates cost until the depth counter runs out.");
            }

            if (!known.Contains(target))
            {
                return new AgentCallGraphProblem(
                    "unknown_agent",
                    $"Agent '{agentName}' wants to call an agent named '{target}', but " +
                    "no such agent exists in the catalog. Create that agent first.");
            }
        }

        return FindCycle(agentName, edges) is { } cycle
            ? new AgentCallGraphProblem(
                "cycle",
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
    private static List<string>? FindCycle(string start, Dictionary<string, IReadOnlyList<string>> edges)
    {
        var path = new List<string>();
        var onPath = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<Frame>();

        stack.Push(new Frame(start, 0));
        path.Add(start);
        onPath.Add(start);

        while (stack.Count > 0)
        {
            var frame = stack.Pop();
            var children = edges.TryGetValue(frame.Node, out var list) ? list : [];

            if (frame.Index >= children.Count)
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

            var child = children[frame.Index];

            if (onPath.Contains(child))
            {
                var cycle = new List<string>(path.Count + 1);
                cycle.AddRange(path);
                cycle.Add(child);

                return cycle;
            }

            if (visited.Contains(child))
            {
                continue;
            }

            stack.Push(new Frame(child, 0));
            path.Add(child);
            onPath.Add(child);
        }

        return null;
    }

    private readonly record struct Frame(string Node, int Index);
}

/// <summary>The machine-readable result of a call graph validation.</summary>
/// <param name="Code">Stable code: <c>unknown_agent</c> or <c>cycle</c>.</param>
/// <param name="Message">Human-readable description.</param>
internal readonly record struct AgentCallGraphProblem(string Code, string Message);
