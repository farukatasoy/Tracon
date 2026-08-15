namespace AgentPrism;

/// <summary>Settings for publishing AgentPrism agents as MCP tools.</summary>
/// <remarks>
/// This type is deliberately <strong>not</strong> a <c>record</c> (K-035
/// pattern): the options object carries no <c>secret</c>, but the compiler-
/// generated <c>ToString</c> could repeat the same trap for a field added in
/// the future; the class keeps this type discipline.
/// </remarks>
public sealed class AgentPrismMcpServerOptions
{
    /// <summary>
    /// Names of agents to expose. If empty and <see cref="ExposeAllAgents"/>
    /// is off, no agent is visible through MCP.
    /// </summary>
    public IList<string> ExposedAgents { get; } = [];

    /// <summary>
    /// Exposes every agent in the catalog. Default <see langword="false"/>;
    /// enabling it is an explicit choice (K1).
    /// </summary>
    public bool ExposeAllAgents { get; set; }

    /// <summary>
    /// MCP tool name prefix. Default <c>agentprism</c>; the tool name becomes
    /// <c>{ToolNamePrefix}_{agent}</c>.
    /// </summary>
    public string ToolNamePrefix { get; set; } = "agentprism";

    /// <summary>
    /// Depth and token budget template for external calls. Each <c>tools/call</c>
    /// creates a NEW <see cref="AgentRunBudget"/> instance with these values;
    /// the object itself is not shared.
    /// </summary>
    /// <remarks>
    /// The default <see cref="AgentRunBudget.MaxDepth"/> is 1: letting an
    /// externally-called agent open its own tree makes the cost unpredictable.
    /// </remarks>
    public AgentRunBudget Budget { get; set; } = new() { MaxDepth = 1 };
}
