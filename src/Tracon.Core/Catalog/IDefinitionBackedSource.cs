namespace Tracon;

/// <summary>
/// An agent source whose agents are compiled from an <see cref="AgentDefinition"/> the source can
/// hand back, so the catalog can read the parameter schema of the agent it would run.
/// </summary>
/// <remarks>
/// Internal on purpose, like <see cref="IDescribingAgentSource"/>: the code source, the database
/// source and the adapter of <see cref="IAgentDefinitionSource"/> implement it. An
/// <see cref="IAgentSource"/> that builds its own agent does not, and for it the catalog knows no
/// schema: every parameter value supplied for such an agent is unknown.
/// </remarks>
internal interface IDefinitionBackedSource
{
    /// <summary>Gets where the definitions of this source live.</summary>
    AgentDefinitionOrigin Origin { get; }

    /// <summary>Finds the definition this source runs for a name.</summary>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="version">A pinned version; <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <see langword="null"/> when this source does not hold the name. When it does, the result's
    /// definition is <see langword="null"/> if the agent has no definition to read: a code agent
    /// built by a factory, a code agent asked for a version it cannot have, or a version the source
    /// does not hold.
    /// </returns>
    ValueTask<HeldDefinition?> FindDefinitionAsync(string agentName, int? version, CancellationToken cancellationToken);
}

/// <summary>A name a definition-backed source holds, and the definition behind it when there is one.</summary>
/// <param name="Definition">The definition; <see langword="null"/> when the agent has none to read.</param>
internal readonly record struct HeldDefinition(AgentDefinition? Definition);
