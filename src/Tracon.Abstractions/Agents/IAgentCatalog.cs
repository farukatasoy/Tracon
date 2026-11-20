using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The catalog that joins every agent source into a single view. The user interface
/// and the HTTP layer reach agents only through this interface.
/// </summary>
/// <remarks>
/// Call this interface to look agents up. Tracon supplies the implementation; do not
/// implement or replace it, because a replacement would have to join every agent source
/// itself. To add agents, implement <see cref="IAgentSource"/> instead.
/// </remarks>
public interface IAgentCatalog
{
    /// <summary>
    /// Lists the agents from every source. On a name clash the source with the
    /// <em>lower</em> <see cref="IAgentSource.Priority"/> value wins - it ranks higher,
    /// because a lower value is tried first - and the other one is not added to the list.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Agent summaries ordered by name.</returns>
    ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the agent with the given name. The returned agent is wrapped by the run
    /// recording decorator; every run is written into <see cref="IRunStore"/>.
    /// </summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="options">
    /// The definition version and instruction culture to resolve. <see langword="null"/>
    /// resolves the current version with the definition's default instructions.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent, or <see langword="null"/> when no source holds it.</returns>
    /// <exception cref="TraconException">
    /// <see cref="AgentResolveOptions.Version"/> is given and the agent comes from code or from a
    /// source that keeps no version history, or that version does not exist.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapPost("/triage", async (IAgentCatalog catalog, CancellationToken cancellationToken) =&gt;
    /// {
    ///     var agent = await catalog.ResolveAsync(
    ///         "support-triage",
    ///         new AgentResolveOptions { Culture = "tr" },
    ///         cancellationToken);
    ///
    ///     return agent is not null;
    /// });
    /// </code>
    /// </example>
    ValueTask<AIAgent?> ResolveAsync(
        string agentName,
        AgentResolveOptions? options = null,
        CancellationToken cancellationToken = default);
}
