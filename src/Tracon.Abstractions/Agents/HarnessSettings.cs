namespace Tracon;

/// <summary>
/// Settings for the harness capabilities. It mirrors a safe subset of the
/// <c>HarnessAgentOptions</c> structure of Microsoft Agent Framework.
/// </summary>
/// <remarks>
/// Shell access and background agents are <em>deliberately absent</em> from these
/// settings. Those two capabilities open a code execution surface on the server and
/// need a separate security review.
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
    /// <remarks>
    /// File memory is off unless <c>MemorySettings.EnableFileMemory</c> turns
    /// it on; it then uses the registered <c>AgentFileStore</c>, never a
    /// directory of the server. A definition that sets both is refused when
    /// the agent is compiled.
    /// </remarks>
    public bool DisableFileMemory { get; init; }

    /// <summary>Gets a value that turns off web search.</summary>
    /// <remarks>
    /// Web search is off unless <see cref="EnableWebSearch"/> turns it on. A
    /// definition that sets both is refused when the agent is compiled.
    /// </remarks>
    public bool DisableWebSearch { get; init; }

    /// <summary>Gets a value that turns on the model provider's hosted web search.</summary>
    /// <remarks>
    /// The search runs at the model provider and is billed by it. Not every
    /// provider surface offers it: the OpenAI Chat Completions surface accepts
    /// it only for search models and answers <c>400</c> otherwise, while the
    /// Responses surface (<c>openai-responses</c>) offers it to every model.
    /// </remarks>
    public bool EnableWebSearch { get; init; }

    /// <summary>
    /// Gets a value that turns off automatic tool approval. Once turned off, every tool
    /// call waits for an explicit approval.
    /// </summary>
    public bool DisableToolAutoApproval { get; init; }

    /// <summary>Gets a value that turns off the agent skills provider.</summary>
    /// <remarks>
    /// The provider is on only when the definition names skills
    /// (<c>AgentDefinition.SkillNames</c>), and it offers only those skills;
    /// it never reads skills from the server's working directory. A definition
    /// that names skills and sets this value is refused when the agent is
    /// compiled.
    /// </remarks>
    public bool DisableAgentSkillsProvider { get; init; }

    /// <summary>Gets a value that turns off the agent mode provider.</summary>
    public bool DisableAgentModeProvider { get; init; }

    /// <summary>
    /// Gets the loop (run-until-done) settings. <see langword="null"/>, the
    /// default, leaves the loop off.
    /// </summary>
    /// <remarks>
    /// Not to be confused with <c>MaximumIterationsPerRequest</c>, which
    /// bounds the harness's INNER tool-calling loop within a single invocation.
    /// <c>LoopSettings</c> bounds the OUTER loop that re-invokes the whole
    /// agent until a stop criterion is satisfied. See <c>LoopSettings</c>
    /// for the comparison in full.
    /// </remarks>
    public LoopSettings? Loop { get; init; }
}
