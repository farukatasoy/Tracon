namespace Tracon;

/// <summary>
/// An <see cref="IAgentDefinitionSource"/> that keeps a version history and can return one specific
/// version of a definition. Only sources that keep a history implement it.
/// </summary>
/// <remarks>
/// <para>
/// A run pinned to a version (an A/B experiment arm, a callable agent with a fixed version, a replay
/// of an older run) is refused for an agent of a source that does not implement this interface.
/// </para>
/// <para>
/// The lifetime, tenant and delivery rules of <see cref="IAgentDefinitionSource"/> apply unchanged: a
/// source is a singleton and must be thread-safe, it reads the ambient tenant when it is tenant-aware, and no
/// delivery guarantee applies, because each call answers one resolve and nothing is queued or retried.
/// A version of a definition must not change after the source returned it.
/// </para>
/// </remarks>
public interface IVersionedAgentDefinitionSource : IAgentDefinitionSource
{
    /// <summary>Gets one version of an agent's definition.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="version">The requested definition version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The definition of that version, or <see langword="null"/> when this source does not hold the
    /// agent or that version does not exist.
    /// </returns>
    ValueTask<AgentDefinition?> GetVersionAsync(string agentName, int version, CancellationToken cancellationToken = default);
}
