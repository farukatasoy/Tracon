namespace AgentPrism;

/// <summary>Structured response validation settings.</summary>
/// <remarks>
/// Read from the <c>AgentPrism:StructuredResponse</c> configuration section. See
/// <c>AgentPrismServiceCollectionExtensions.AddAgentPrism</c>.
/// </remarks>
public sealed class AgentPrismStructuredResponseOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:StructuredResponse";

    /// <summary>
    /// Whether a run's response is checked against the agent's requested
    /// <see cref="AgentResponseFormat"/> before the run closes. Defaults to
    /// <see langword="false"/>: today's behaviour (a response is never
    /// inspected after the model returns it) does not change unless a setup
    /// opts in.
    /// </summary>
    public bool Enabled { get; set; }
}
