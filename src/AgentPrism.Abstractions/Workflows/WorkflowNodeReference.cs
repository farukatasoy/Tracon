namespace AgentPrism;

/// <summary>
/// Points to a single node entering a workflow's graph: an agent from the
/// catalog, or a function registered in code.
/// </summary>
/// <remarks>
/// <para>
/// Used only by <see cref="WorkflowKind.Sequential"/> definitions that mix
/// agent and function nodes. A definition that uses only agents keeps using
/// <see cref="WorkflowDefinition.AgentNames"/>; the two fields are mutually
/// exclusive (the validator in <c>AgentPrism.Core</c> enforces this).
/// </para>
/// <para>
/// <see cref="Kind"/> reuses <see cref="WorkflowNodeKind"/>, the same enum
/// the compiled graph reports. Only <see cref="WorkflowNodeKind.Agent"/> and
/// <see cref="WorkflowNodeKind.Function"/> are valid here — the other members
/// (<c>Orchestration</c>, <c>RequestPort</c>, <c>Output</c>) describe nodes
/// the compiler adds on its own; a definition never names one directly.
/// </para>
/// </remarks>
public sealed record WorkflowNodeReference
{
    /// <summary>
    /// Gets the referenced name: an agent name from the catalog when
    /// <see cref="Kind"/> is <see cref="WorkflowNodeKind.Agent"/>, or a
    /// function name from the code registry when it is
    /// <see cref="WorkflowNodeKind.Function"/>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Gets which registry <c>Name</c> is looked up in.</summary>
    public required WorkflowNodeKind Kind { get; init; }
}
