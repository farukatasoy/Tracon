using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// A source the catalog collects agents from. AgentPrism joins several sources into
/// one catalog: the ones declared in code, the ones stored in the database and (from
/// phase 4 onwards) Microsoft Agent Framework hosting registrations.
/// </summary>
/// <remarks>
/// <para>
/// This abstraction keeps <c>AgentPrism.Core</c> from depending on the prerelease
/// <c>Microsoft.Agents.AI.Hosting</c> package. The bridge that shows MAF hosting
/// registrations in the catalog implements this interface inside
/// <c>AgentPrism.AspNetCore</c>. Rationale: <c>docs/KARARLAR.md</c>, decisions K-008
/// and K-019.
/// </para>
/// </remarks>
public interface IAgentSource
{
    /// <summary>Gets the name of the source, used for diagnostics and user interface badges.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the resolution priority. A lower value is tried first. When two agents share
    /// a name, the source with priority wins.
    /// </summary>
    /// <remarks>
    /// The values in use: 0 for the code source, 10 for the MAF hosting source, 100 for
    /// the database source.
    /// </remarks>
    int Priority { get; }

    /// <summary>Lists every agent in this source.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent summaries.</returns>
    ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves the agent with the given name and makes it runnable.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent, or <see langword="null"/> when this source does not hold it.</returns>
    ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default);
}
