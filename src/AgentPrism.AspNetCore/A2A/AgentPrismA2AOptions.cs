namespace AgentPrism;

/// <summary>Settings for publishing AgentPrism agents over A2A.</summary>
/// <remarks>
/// <para>
/// There is <strong>NO</strong> counterpart to
/// <see cref="AgentPrismMcpServerOptions.ExposeAllAgents"/>. Measured
/// (<c>Microsoft.Agents.AI.Hosting.A2A</c> 1.16.0-preview.260730.1):
/// <c>AddA2AServer</c> is a REGISTRATION-TIME API and can only publish agents
/// explicitly named in <see cref="ExposedAgents"/>; it cannot see an agent
/// added at runtime.
/// </para>
/// <para>
/// This type is deliberately <strong>not</strong> a <c>record</c>.
/// </para>
/// </remarks>
public sealed class AgentPrismA2AOptions
{
    /// <summary>
    /// Names of agents to expose. Read at startup and FROZEN — a name added at
    /// runtime does NOT appear in A2A.
    /// </summary>
    public IList<string> ExposedAgents { get; } = [];

    /// <summary>
    /// Depth and token budget template for external calls. Each call creates a
    /// NEW <see cref="AgentRunBudget"/> instance with these values; the object
    /// itself is not shared.
    /// </summary>
    public AgentRunBudget Budget { get; set; } = new() { MaxDepth = 1 };
}
