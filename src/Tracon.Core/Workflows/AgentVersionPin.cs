namespace Tracon;

/// <summary>
/// Judges the agent version pins of a workflow definition against the agents that exist.
/// </summary>
/// <remarks>
/// <para>
/// The shape of a pin (a function node carries none, a version is at least 1) is
/// <see cref="WorkflowDefinitionValidator.NodeAgentVersionProblem"/>. Whether the agent
/// has the version needs the catalog, so it lives here, and both callers ask it: the save
/// endpoint, which refuses the definition with <c>400</c>, and the compiler, which refuses
/// the run. The agent can be deleted between the two, so neither check replaces the other.
/// </para>
/// <para>
/// The probe reads descriptors and one version row, never compiles the agent: this runs
/// for every compile of a workflow, including a graph view, and compiling would connect
/// the agent's MCP servers for a question that has a cheaper answer.
/// </para>
/// </remarks>
internal static class AgentVersionPin
{
    /// <summary>Finds the first pin the agents cannot honour.</summary>
    /// <param name="definition">The definition. Only its pinned agent nodes are read.</param>
    /// <param name="catalog">The agent catalog. Not read at all when the definition pins nothing.</param>
    /// <param name="agentDefinitions">
    /// The store that keeps stored agents' versions. <see langword="null"/> when the host
    /// has none, in which case the existence of a version is not judged.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>User-facing error text; <see langword="null"/> when every pin can be honoured.</returns>
    /// <remarks>
    /// An agent the catalog does not list is skipped: a missing agent is reported by name
    /// by the compiler, and a definition may be saved before its agents exist. An agent
    /// from a custom source is skipped too; the catalog refuses the pinned step when that
    /// source keeps no history.
    /// </remarks>
    public static async ValueTask<string?> FindProblemAsync(
        WorkflowDefinition definition,
        IAgentCatalog catalog,
        IAgentDefinitionStore? agentDefinitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(catalog);

        if (!definition.Nodes.Any(static node => node is { Kind: WorkflowNodeKind.Agent, AgentVersion: not null }))
        {
            return null;
        }

        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var node in definition.Nodes)
        {
            if (node is not { Kind: WorkflowNodeKind.Agent, AgentVersion: { } version })
            {
                continue;
            }

            var descriptor = descriptors.FirstOrDefault(
                candidate => string.Equals(candidate.Name, node.Name, StringComparison.Ordinal));

            if (descriptor is null)
            {
                continue;
            }

            if (descriptor.Origin == AgentDefinitionOrigin.Code)
            {
                return $"Node '{node.Name}' in workflow '{definition.Name}' pins agent '{node.Name}' to version " +
                       $"{version}, but '{node.Name}' is defined in code and keeps no version history. Leave out " +
                       "'agentVersion', or define the agent in the console.";
            }

            if (descriptor.Origin == AgentDefinitionOrigin.Database &&
                agentDefinitions is not null &&
                await agentDefinitions.GetVersionAsync(node.Name, version, cancellationToken).ConfigureAwait(false) is null)
            {
                return $"Node '{node.Name}' in workflow '{definition.Name}' pins agent '{node.Name}' to version " +
                       $"{version}, but the agent has no version {version}. List its versions with " +
                       $"GET /api/agents/{node.Name}/versions.";
            }
        }

        return null;
    }
}
