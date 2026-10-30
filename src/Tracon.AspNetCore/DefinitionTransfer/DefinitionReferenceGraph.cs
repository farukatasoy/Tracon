namespace Tracon;

/// <summary>The kind of definition another definition refers to.</summary>
internal enum DefinitionReferenceKind
{
    /// <summary>A stored agent.</summary>
    Agent,

    /// <summary>A stored skill.</summary>
    Skill,

    /// <summary>A remote MCP server.</summary>
    McpServer,
}

/// <summary>One reference from a definition to another definition.</summary>
/// <param name="Kind">What is referred to.</param>
/// <param name="Name">The name of the definition referred to.</param>
/// <param name="Path">The path of the field that refers to it, for example <c>skillNames[0]</c>.</param>
internal readonly record struct DefinitionReference(DefinitionReferenceKind Kind, string Name, string Path);

/// <summary>
/// Reads, from a definition, the other definitions it needs: the closure an export adds, the order an import
/// writes in, and the fields that carry a version pin.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 This is the only place that knows which members of a definition name another definition.
/// <c>DefinitionReferenceCoverageTests</c> walks the definition types and refuses a member whose name suggests a
/// reference unless it is listed here or excluded there with a reason. A reference this type does not know
/// is dropped from the closure without an error: the export is then missing a skill, and the import of the
/// agent that uses it fails on the target.
/// </para>
/// </remarks>
internal static class DefinitionReferenceGraph
{
    /// <summary>Gets the members that name another definition: the closure of an export follows each of them.</summary>
    public static IReadOnlyCollection<string> ReferenceMembers { get; } =
    [
        "AgentDefinition.ToolNames",
        "AgentDefinition.SkillNames",
        "AgentDefinition.SharedInstructionsName",
        "AgentDefinition.CallableAgents",
        "AgentDefinition.McpResourceUris",
        "CallableAgentReference.Name",
        "LoopCriterion.ReviewerAgent",
        "WorkflowDefinition.AgentNames",
        "WorkflowDefinition.ManagerAgentName",
        "WorkflowDefinition.Nodes",
        "WorkflowNodeReference.Name",
    ];

    /// <summary>Gets the members that carry a version of another definition: an import translates each of them.</summary>
    public static IReadOnlyCollection<string> PinMembers { get; } =
    [
        "CallableAgentReference.Version",
        "LoopCriterion.ReviewerAgentVersion",
        "WorkflowNodeReference.AgentVersion",
    ];

    /// <summary>Lists the definitions an agent needs.</summary>
    /// <param name="definition">The agent.</param>
    /// <param name="mcpServerNames">The names of the MCP servers a tool or a resource may belong to.</param>
    /// <returns>
    /// The agents it calls, reviews with and shares instructions from; the stored skills it names; and each MCP
    /// server whose name prefixes one of its tools (<c>{server}_</c>) or resources (<c>{server}:</c>). A name the
    /// caller does not know to be stored is still listed: whether it exists is the caller's question.
    /// </returns>
    /// <remarks>
    /// A tool name belongs to every server whose name is a prefix of it, so a server <c>a</c> and a server
    /// <c>a_b</c> both match the tool <c>a_b_search</c>. The closure takes both: it may bring one server too many,
    /// and never one too few.
    /// </remarks>
    public static IReadOnlyList<DefinitionReference> OfAgent(AgentDefinition definition, IReadOnlyCollection<string> mcpServerNames)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(mcpServerNames);

        var references = new List<DefinitionReference>();

        for (var index = 0; index < definition.SkillNames.Count; index++)
        {
            references.Add(new DefinitionReference(DefinitionReferenceKind.Skill, definition.SkillNames[index], $"skillNames[{index}]"));
        }

        if (definition.SharedInstructionsName is { Length: > 0 } shared)
        {
            references.Add(new DefinitionReference(DefinitionReferenceKind.Agent, shared, "sharedInstructionsName"));
        }

        for (var index = 0; index < definition.CallableAgents.Count; index++)
        {
            references.Add(new DefinitionReference(
                DefinitionReferenceKind.Agent,
                definition.CallableAgents[index].Name,
                $"callableAgents[{index}].name"));
        }

        if (definition.Harness?.Loop is { } loop)
        {
            for (var index = 0; index < loop.Criteria.Count; index++)
            {
                if (loop.Criteria[index].ReviewerAgent is { Length: > 0 } reviewer)
                {
                    references.Add(new DefinitionReference(
                        DefinitionReferenceKind.Agent,
                        reviewer,
                        $"harness.loop.criteria[{index}].reviewerAgent"));
                }
            }
        }

        for (var index = 0; index < definition.ToolNames.Count; index++)
        {
            foreach (var server in mcpServerNames)
            {
                if (definition.ToolNames[index].StartsWith(server + "_", StringComparison.Ordinal))
                {
                    references.Add(new DefinitionReference(DefinitionReferenceKind.McpServer, server, $"toolNames[{index}]"));
                }
            }
        }

        for (var index = 0; index < definition.McpResourceUris.Count; index++)
        {
            foreach (var server in mcpServerNames)
            {
                if (definition.McpResourceUris[index].StartsWith(server + ":", StringComparison.Ordinal))
                {
                    references.Add(new DefinitionReference(DefinitionReferenceKind.McpServer, server, $"mcpResourceUris[{index}]"));
                }
            }
        }

        return references;
    }

    /// <summary>Lists the agents a workflow needs.</summary>
    /// <param name="definition">The workflow.</param>
    /// <returns>The agents of its list, its manager and its agent nodes.</returns>
    public static IReadOnlyList<DefinitionReference> OfWorkflow(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var references = new List<DefinitionReference>();

        for (var index = 0; index < definition.AgentNames.Count; index++)
        {
            references.Add(new DefinitionReference(DefinitionReferenceKind.Agent, definition.AgentNames[index], $"agentNames[{index}]"));
        }

        if (definition.ManagerAgentName is { Length: > 0 } manager)
        {
            references.Add(new DefinitionReference(DefinitionReferenceKind.Agent, manager, "managerAgentName"));
        }

        for (var index = 0; index < definition.Nodes.Count; index++)
        {
            if (definition.Nodes[index].Kind == WorkflowNodeKind.Agent)
            {
                references.Add(new DefinitionReference(DefinitionReferenceKind.Agent, definition.Nodes[index].Name, $"nodes[{index}].name"));
            }
        }

        return references;
    }

    /// <summary>Orders agents so that every agent comes after the agents of the same set it needs.</summary>
    /// <param name="agents">The agents, by name.</param>
    /// <returns>
    /// The names, callees first. The order among agents that do not need each other is the order of
    /// <paramref name="agents"/>. A cycle does not loop: the graph validation of an import refuses it, and the
    /// order only has to end.
    /// </returns>
    public static IReadOnlyList<string> OrderAgents(IReadOnlyDictionary<string, AgentDefinition> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var ordered = new List<string>(agents.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in agents.Keys)
        {
            Visit(name);
        }

        return ordered;

        void Visit(string name)
        {
            if (!visited.Add(name) || !agents.TryGetValue(name, out var definition))
            {
                return;
            }

            foreach (var reference in OfAgent(definition, []))
            {
                if (reference.Kind == DefinitionReferenceKind.Agent)
                {
                    Visit(reference.Name);
                }
            }

            ordered.Add(name);
        }
    }
}
