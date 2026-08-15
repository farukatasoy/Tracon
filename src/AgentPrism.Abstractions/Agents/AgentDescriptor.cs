namespace AgentPrism;

/// <summary>
/// A summary view of an agent listed in the catalog. It carries everything the user
/// interface needs to draw the agent list, without having to build the agent.
/// </summary>
public sealed record AgentDescriptor
{
    /// <summary>Gets the unique name of the agent.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the name shown in the user interface.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the short description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the origin of the definition.</summary>
    public required AgentDefinitionOrigin Origin { get; init; }

    /// <summary>
    /// Gets the name of the source that supplies this agent, for example <c>code</c>
    /// or <c>database</c>. Several sources can share the same <see cref="Origin"/> value.
    /// </summary>
    public required string SourceName { get; init; }

    /// <summary>Gets the definition version. It is always 1 for code agents.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets the bound model. It can be unknown for code agents.</summary>
    public ModelBinding? Model { get; init; }

    /// <summary>Gets the names of the tools this agent may use.</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];

    /// <summary>Gets the names of the skills this agent may load at run time.</summary>
    public IReadOnlyList<string> SkillNames { get; init; } = [];

    /// <summary>
    /// Gets the names of the other agents this agent may call.
    /// </summary>
    /// <remarks>
    /// An agent registered in code with a <em>factory</em> carries no declarative
    /// definition; the list is empty for such an agent. That means the graph can be
    /// incomplete, which is why the static cycle check is not enough on its own.
    /// </remarks>
    public IReadOnlyList<string> CallableAgentNames { get; init; } = [];

    /// <summary>Gets whether the harness capabilities are enabled.</summary>
    public bool UsesHarness { get; init; }

    /// <summary>Gets the time of the last change (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
