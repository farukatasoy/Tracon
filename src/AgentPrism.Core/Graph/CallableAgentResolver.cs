using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// Resolves, from the catalog, the other agents an agent may call.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The catalog is intentionally resolved late.</strong> The
/// dependency graph would otherwise be circular: <c>IAgentCatalog</c> →
/// <c>IAgentSource</c> → <c>AgentDefinitionCompiler</c> → callable agents →
/// <c>IAgentCatalog</c>. Constructor injection from the container cannot
/// build this chain. The fix is to request the catalog <em>on first use,
/// not at setup time</em>; by then both sides are already set up.
/// </para>
/// <para>
/// The second and more valuable benefit of late resolution: a sub-agent is
/// re-resolved on every call. When a sub-agent's definition is updated, the
/// calling agent's compiled copy does not go <em>stale</em>; there is no need
/// to carry the sub-agent's version into the cache key.
/// </para>
/// </remarks>
public sealed class CallableAgentResolver
{
    private readonly IServiceProvider _services;
    private IAgentCatalog? _catalog;

    /// <summary>Creates a new resolver.</summary>
    /// <param name="services">Service provider from which the catalog is resolved.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public CallableAgentResolver(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = services;
    }

    /// <summary>Resolves the agent with the given name.</summary>
    /// <param name="agentName">Agent name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The agent wrapped with the run-recording wrapper; <see langword="null"/> when absent.</returns>
    public ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
        => ResolveCatalog().ResolveAsync(agentName, cancellationToken);

    /// <summary>Lists the agent summaries in the catalog.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Agent summaries.</returns>
    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => ResolveCatalog().ListAsync(cancellationToken);

    /// <summary>
    /// Resolves callable agent names together with their descriptions.
    /// </summary>
    /// <param name="callableAgentNames">Names to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Name/description pairs; the description is empty for names not present in the catalog.</returns>
    /// <remarks>
    /// The description is included in the agent list sent to the model. A
    /// sub-agent without a description is one the model has no way of knowing
    /// when to call.
    /// </remarks>
    public async ValueTask<IReadOnlyList<CallableAgentInfo>> DescribeAsync(
        IReadOnlyList<string> callableAgentNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callableAgentNames);

        if (callableAgentNames.Count == 0)
        {
            return [];
        }

        var descriptors = await ListAsync(cancellationToken).ConfigureAwait(false);
        var byName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            byName[descriptor.Name] = descriptor;
        }

        var result = new List<CallableAgentInfo>(callableAgentNames.Count);

        foreach (var name in callableAgentNames)
        {
            byName.TryGetValue(name, out var descriptor);
            result.Add(new CallableAgentInfo(name, descriptor?.Description, descriptor?.Version ?? 0));
        }

        return result;
    }

    /// <summary>Resolves the catalog on first use and caches it.</summary>
    /// <remarks>
    /// It is harmless for two concurrent calls to race and resolve the catalog
    /// twice: the catalog is a singleton and the container returns the same
    /// instance. Adding a lock would needlessly synchronize every call.
    /// </remarks>
    private IAgentCatalog ResolveCatalog()
        => _catalog ??= _services.GetRequiredService<IAgentCatalog>();
}

/// <summary>The summary of a callable sub-agent, reported to the model.</summary>
/// <param name="Name">Agent name.</param>
/// <param name="Description">Description of what it does.</param>
/// <param name="Version">
/// Version of the sub-agent's definition. 0 when not found in the catalog.
/// Enters the cache key of the calling agent's compiled copy: a sub-agent's
/// description is embedded in the text sent to the model, so the caller must
/// be recompiled when the sub-agent is updated.
/// </param>
public readonly record struct CallableAgentInfo(string Name, string? Description, int Version);
