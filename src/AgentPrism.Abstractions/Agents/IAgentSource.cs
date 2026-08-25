using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// A source the catalog collects agents from. AgentPrism joins sources declared in
/// code, stored in the database, and registered by the application.
/// </summary>
/// <remarks>
/// <para>Sources are singleton instances. Calls can run concurrently, so an implementation
/// must be thread-safe and must not keep request or run state in instance fields. Do not
/// capture scoped services; create a scope from <see cref="IServiceProvider"/> when needed.</para>
/// <para><see cref="ListAsync"/> is on the run path. It can run repeatedly and must be
/// side-effect free. The catalog does not cache a global snapshot, retry calls, or apply a
/// timeout. A source that needs caching, retries, or a timeout owns that policy.</para>
/// <para><see cref="ListAsync"/> and <see cref="ResolveAsync"/> describe the same agent set.
/// Returned descriptors and their nested collections must not change after they return.</para>
/// <para>A source may be global or tenant-aware. Reading <see cref="ITenantContext"/> is valid.
/// Startup and background calls use the default tenant when no tenant scope is present.</para>
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
    /// <see cref="AgentSourcePriority.Code"/> and <see cref="AgentSourcePriority.Database"/>
    /// are reserved for built-in sources. Equal priorities preserve DI registration order.
    /// </remarks>
    int Priority { get; }

    /// <summary>Lists every agent in this source.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent summaries.</returns>
    ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves the agent with the given name and makes it runnable.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="culture">
    /// The requested culture, resolved against the definition's culture-keyed instructions
    /// (see <c>AgentDefinition.InstructionsByCulture</c>). <see langword="null"/> uses the
    /// definition's default instructions.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent, or <see langword="null"/> when this source does not hold it.</returns>
    ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default);
}
