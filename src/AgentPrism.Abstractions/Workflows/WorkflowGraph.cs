namespace AgentPrism;

/// <summary>
/// Represents a workflow's compiled graph: nodes, edges, and external
/// request ports.
/// </summary>
/// <remarks>
/// <para>
/// The graph is extracted from the <strong>compiled workflow, not the
/// definition</strong>. The reason was measured: built-in patterns add
/// executors the user did not write (<c>OutputMessages</c>,
/// <c>Batcher/*</c>, <c>ConcurrentEnd</c>, <c>HandoffStart</c>,
/// <c>GroupChatHost</c>, <c>MagenticOrchestrator</c>). A graph drawn from
/// the definition would not show these nodes, and the <c>ExecutorInvoked</c>
/// events arriving during a run would not match any node.
/// </para>
/// <para>
/// The type carries no Microsoft Agent Framework type: the HTTP layer does
/// not depend on the <c>AgentPrism.Workflows</c> package.
/// </para>
/// </remarks>
public sealed record WorkflowGraph
{
    /// <summary>Gets the name of the workflow the graph belongs to.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the identifier of the node that receives the input message first.</summary>
    public required string StartExecutorId { get; init; }

    /// <summary>Gets the nodes in the graph.</summary>
    public IReadOnlyList<WorkflowGraphNode> Nodes { get; init; } = [];

    /// <summary>Gets the edges between nodes.</summary>
    public IReadOnlyList<WorkflowGraphEdge> Edges { get; init; } = [];

    /// <summary>
    /// Gets the Mermaid text produced by Microsoft Agent Framework.
    /// </summary>
    /// <remarks>
    /// The UI draws the graph itself (bundle budget); this text is
    /// for <em>export</em>. Users can copy it to the clipboard and paste it
    /// into a document - project convention requires diagrams to be written
    /// in Mermaid.
    /// </remarks>
    public required string Mermaid { get; init; }
}

/// <summary>Represents a node in the graph.</summary>
/// <remarks>
/// The identifier matches the <c>Text</c> field of the <c>ExecutorInvoked</c>
/// / <c>ExecutorCompleted</c> / <c>ExecutorFailed</c> events arriving during a
/// run <strong>exactly</strong>; this lets the UI highlight nodes live.
/// </remarks>
public sealed record WorkflowGraphNode
{
    /// <summary>Gets the executor identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the short label shown in the UI.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the node's role.</summary>
    public required WorkflowNodeKind Kind { get; init; }

    /// <summary>
    /// Gets the agent's name if the node represents an agent; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the Microsoft Agent Framework executor type. For debugging.</summary>
    public string? ExecutorType { get; init; }
}

/// <summary>Represents the connection between two nodes.</summary>
public sealed record WorkflowGraphEdge
{
    /// <summary>Gets the source node's identifier.</summary>
    public required string From { get; init; }

    /// <summary>Gets the target node's identifier.</summary>
    public required string To { get; init; }

    /// <summary>Gets the edge's kind.</summary>
    public required WorkflowEdgeKind Kind { get; init; }
}

/// <summary>Represents the role of a graph node.</summary>
/// <remarks>
/// Written as a name in JSON. The UI picks the node shape based on
/// it.
/// </remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<WorkflowNodeKind>))]
public enum WorkflowNodeKind
{
    /// <summary>A node whose role could not be determined.</summary>
    Unknown = 0,

    /// <summary>A node that runs an agent from the catalog.</summary>
    Agent = 1,

    /// <summary>A helper node added by a built-in pattern (distributor, aggregator, manager).</summary>
    Orchestration = 2,

    /// <summary>A port awaiting an external response. Human-in-the-loop enters here.</summary>
    RequestPort = 3,

    /// <summary>A node that collects the graph's output.</summary>
    Output = 4,

    /// <summary>A node that runs a function registered in code.</summary>
    Function = 5,
}

/// <summary>Represents the kind of an edge.</summary>
/// <remarks>Written as a name in JSON.</remarks>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<WorkflowEdgeKind>))]
public enum WorkflowEdgeKind
{
    /// <summary>Single source to single target.</summary>
    Direct = 0,

    /// <summary>Single source to multiple targets.</summary>
    FanOut = 1,

    /// <summary>Multiple sources to single target.</summary>
    FanIn = 2,
}
