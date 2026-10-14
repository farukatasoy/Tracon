namespace Tracon;

/// <summary>
/// Represents a summary view of a workflow listed in the catalog.
/// </summary>
/// <remarks>
/// Workflows defined in code do not carry a declarative
/// <see cref="WorkflowDefinition"/>: they come from a factory, and their
/// graph is only known after compilation. For such a record,
/// <see cref="Kind"/> and <see cref="AgentNames"/> stay empty; the catalog
/// still shows the name and description.
/// </remarks>
public sealed record WorkflowDescriptor
{
    /// <summary>Gets the workflow's unique name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the display name shown in the UI.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the short description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets the source of the definition.</summary>
    public required AgentDefinitionOrigin Origin { get; init; }

    /// <summary>
    /// Gets the built-in pattern. <see langword="null"/> for workflows
    /// defined by a factory in code: a free-form graph does not correspond
    /// to a pattern.
    /// </summary>
    public WorkflowKind? Kind { get; init; }

    /// <summary>Gets the agent names entering the graph. Can be empty for code-defined workflows.</summary>
    public IReadOnlyList<string> AgentNames { get; init; } = [];

    /// <summary>
    /// Gets the mixed agent/function node list for a Sequential workflow that
    /// uses function nodes. Empty for every other definition.
    /// </summary>
    public IReadOnlyList<WorkflowNodeReference> Nodes { get; init; } = [];

    /// <summary>
    /// Gets the run parameters the workflow accepts. Empty for a workflow that
    /// takes none, and always empty for a workflow defined in code.
    /// </summary>
    public IReadOnlyList<AgentParameter> Parameters { get; init; } = [];

    /// <summary>
    /// Gets how the workflow treats more than one answer to the same pending human request.
    /// For a stored workflow this is the current version's mode; a run that waits keeps the
    /// mode of the version it recorded.
    /// </summary>
    public WorkflowResponseMode ResponseMode { get; init; }

    /// <summary>
    /// Gets how long a human request of the workflow may wait for an answer; <see langword="null"/>
    /// when it waits without limit. For a stored workflow this is the current version's value; a run
    /// that waits keeps the limit of the version it recorded.
    /// </summary>
    public TimeSpan? RequestTimeout { get; init; }

    /// <summary>Gets the definition version. Always 1 for code-defined workflows.</summary>
    public int Version { get; init; } = 1;

    /// <summary>Gets the last modification time (UTC).</summary>
    public DateTimeOffset? UpdatedAt { get; init; }
}
