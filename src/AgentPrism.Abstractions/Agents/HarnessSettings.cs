namespace AgentPrism;

/// <summary>
/// Settings for the harness capabilities. It mirrors a safe subset of the
/// <c>HarnessAgentOptions</c> structure of Microsoft Agent Framework.
/// </summary>
/// <remarks>
/// Shell access and background agents are <em>deliberately absent</em> from these
/// settings. Those two capabilities open a code execution surface on the server and
/// need a separate security review; phase 6 covers them.
/// </remarks>
public sealed record HarnessSettings
{
    /// <summary>Gets the token limit of the context window. Compaction starts once it is exceeded.</summary>
    public int? MaxContextWindowTokens { get; init; }

    /// <summary>Gets the upper token limit produced in a single response.</summary>
    public int? MaxOutputTokens { get; init; }

    /// <summary>Gets the upper number of iterations allowed within a single request.</summary>
    public int? MaximumIterationsPerRequest { get; init; }

    /// <summary>Gets the extra instructions passed to the harness.</summary>
    public string? HarnessInstructions { get; init; }

    /// <summary>Gets a value that turns off context compaction.</summary>
    public bool DisableCompaction { get; init; }

    /// <summary>Gets a value that turns off todo tracking.</summary>
    public bool DisableTodoProvider { get; init; }

    /// <summary>Gets a value that turns off file memory.</summary>
    public bool DisableFileMemory { get; init; }

    /// <summary>Gets a value that turns off web search.</summary>
    public bool DisableWebSearch { get; init; }

    /// <summary>
    /// Gets a value that turns off automatic tool approval. Once turned off, every tool
    /// call waits for an explicit approval.
    /// </summary>
    public bool DisableToolAutoApproval { get; init; }

    /// <summary>Gets a value that turns off the agent skills provider.</summary>
    public bool DisableAgentSkillsProvider { get; init; }

    /// <summary>Gets a value that turns off the agent mode provider.</summary>
    public bool DisableAgentModeProvider { get; init; }
}
