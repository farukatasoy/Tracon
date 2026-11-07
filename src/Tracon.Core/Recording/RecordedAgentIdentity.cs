namespace Tracon;

/// <summary>
/// What the catalog knows about the agent that a recorder wraps: the model it is bound to and the
/// version of its definition.
/// </summary>
/// <remarks>
/// All three are unknown for an agent defined in code and for a recorder built around a bare agent.
/// </remarks>
/// <param name="ModelId">The model the agent is bound to. It is written on the run row.</param>
/// <param name="ModelProvider">
/// The provider of the model. It is used only to resolve the cost and is not persisted.
/// </param>
/// <param name="Version">
/// The current version of the definition. A run that an A/B experiment resolves overrides it with
/// <see cref="TraconRunOptions.AgentVersion"/>.
/// </param>
internal sealed record RecordedAgentIdentity(string? ModelId, string? ModelProvider, int? Version)
{
    /// <summary>Gets an identity that names nothing.</summary>
    public static RecordedAgentIdentity Unknown { get; } = new(null, null, null);
}
