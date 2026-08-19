using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// An <see cref="IAgentSource"/> that can resolve a specific definition version. Only
/// sources that keep a version history implement it (the database source); the code
/// source has no notion of a version and need not implement this interface (decision
/// K-003).
/// </summary>
public interface IVersionedAgentSource : IAgentSource
{
    /// <summary>Resolves an agent by name and by a specific version.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="version">The requested definition version.</param>
    /// <param name="culture">
    /// The requested culture, resolved against the definition's culture-keyed instructions
    /// (see <c>AgentDefinition.InstructionsByCulture</c>). <see langword="null"/> uses the
    /// definition's default instructions.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent, or <see langword="null"/> when this source does not hold it or that version does not exist.</returns>
    ValueTask<AIAgent?> ResolveVersionAsync(
        string agentName,
        int version,
        string? culture = null,
        CancellationToken cancellationToken = default);
}
