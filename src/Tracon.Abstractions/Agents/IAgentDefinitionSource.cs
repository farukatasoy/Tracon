namespace Tracon;

/// <summary>
/// A source that holds agent <em>definitions</em> outside Tracon's own store: in Git, in an object
/// store, in your own database. The source returns an <see cref="AgentDefinition"/>; Tracon
/// compiles it into a runnable agent.
/// </summary>
/// <remarks>
/// <para>
/// Prefer this over <see cref="IAgentSource"/> whenever the agent is a definition. Tracon compiles
/// the definition itself, so parameters, the compiled-agent cache, version pinning, the catalog
/// descriptor, run recording and the tenant rules work exactly as they do for an agent saved in the
/// database. A source that builds its own <c>AIAgent</c> (<see cref="IAgentSource"/>) is opaque to
/// Tracon: it cannot receive parameter values, and its tenant and cache handling are its own work.
/// </para>
/// <para>
/// Sources are singleton instances. Calls can run concurrently, so an implementation must be
/// thread-safe and must not keep request or run state in instance fields. Do not capture scoped
/// services; create a scope from <see cref="IServiceProvider"/> when needed.
/// </para>
/// <para>
/// <see cref="ListAsync"/> and <see cref="GetAsync"/> are on the run path, not startup snapshot
/// APIs. A resolve calls <see cref="GetAsync"/> and the catalog listing calls <see cref="ListAsync"/>;
/// both can run repeatedly and must be side-effect free. The catalog does not cache, retry or
/// apply a timeout; a source that needs any of them owns that policy, including invalidating its own
/// cache. Tracon caches the <em>compiled</em> agent by the definition's content, so returning an equal
/// definition on every call is cheap, and returning a changed one takes effect on the next resolve.
/// </para>
/// <para>
/// <see cref="ListAsync"/> and <see cref="GetAsync"/> describe the same agent set. A definition
/// returned for a name must carry that same <see cref="AgentDefinition.Name"/>, and a list must not
/// repeat a name. Returned definitions must not change after they return.
/// </para>
/// <para>
/// A source may be global or tenant-aware: it reads the ambient tenant from <see cref="ITenantContext"/>.
/// Startup and background calls use the default tenant when no tenant scope is present. A tenant-aware
/// source returns only the definitions of the ambient tenant; Tracon adds that tenant to the key of the
/// compiled-agent cache, so one tenant's compiled agent is never served to another.
/// </para>
/// <para>
/// No delivery guarantee applies. The catalog calls the source for each resolve and each listing; nothing is
/// queued, retried or deduplicated, and a failure reaches the caller as a normalized
/// <see cref="TraconAgentSourceException"/>.
/// </para>
/// <para>
/// Name clashes follow <see cref="IAgentSource"/>: the source with the <em>lower</em>
/// <see cref="Priority"/> value wins, and <c>POST /api/agents/{name}/run</c> runs that source's
/// agent. The built-in code source uses <see cref="AgentSourcePriority.Code"/> and the database uses
/// <see cref="AgentSourcePriority.Database"/>; a custom source may use any value.
/// </para>
/// <para>
/// Implement <see cref="IVersionedAgentDefinitionSource"/> as well when the source keeps a version
/// history. Without it, a run pinned to a version of one of this source's agents is refused.
/// </para>
/// </remarks>
public interface IAgentDefinitionSource
{
    /// <summary>Gets the name of the source, used for diagnostics and user interface badges.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the resolution priority. A lower value is tried first. When two agents share
    /// a name, the source with the <em>lower</em> value wins and the other one is dropped.
    /// </summary>
    int Priority { get; }

    /// <summary>Lists every definition in this source.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definitions, each under a distinct name.</returns>
    ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the current definition of an agent.</summary>
    /// <param name="agentName">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The definition, or <see langword="null"/> when this source does not hold the agent.</returns>
    ValueTask<AgentDefinition?> GetAsync(string agentName, CancellationToken cancellationToken = default);
}
