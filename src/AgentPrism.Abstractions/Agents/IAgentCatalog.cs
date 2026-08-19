using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// The catalog that joins every agent source into a single view. The user interface
/// and the HTTP layer reach agents only through this interface.
/// </summary>
public interface IAgentCatalog
{
    /// <summary>
    /// Lists the agents from every source. On a name clash the source with the higher
    /// priority wins, and the lower-priority one is not added to the list.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Agent summaries ordered by name.</returns>
    ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the agent with the given name. The returned agent is wrapped by the run
    /// recording decorator; every run is written into <see cref="IRunStore"/>.
    /// </summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="culture">
    /// The requested culture, resolved against the definition's culture-keyed instructions
    /// (see <c>AgentDefinition.InstructionsByCulture</c>). <see langword="null"/> uses the
    /// definition's default instructions.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent, or <see langword="null"/> when no source holds it.</returns>
    ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves an agent by name and by <strong>a specific definition version</strong>.
    /// Code agents (<see cref="AgentDefinitionOrigin.Code"/>) have no version history;
    /// <see cref="AgentPrismException"/> is thrown when <paramref name="version"/> is
    /// given for a code agent.
    /// </summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="version">
    /// The requested definition version. When <see langword="null"/> it behaves like
    /// <see cref="ResolveAsync(string, string, CancellationToken)"/> (the current version).
    /// </param>
    /// <param name="culture">
    /// See <see cref="ResolveAsync(string, string, CancellationToken)"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent, or <see langword="null"/> when it is not found.</returns>
    /// <exception cref="AgentPrismException">
    /// <paramref name="version"/> is given and the agent comes from code, or that version
    /// does not exist.
    /// </exception>
    ValueTask<AIAgent?> ResolveAsync(
        string agentName,
        int? version,
        string? culture = null,
        CancellationToken cancellationToken = default);
}
