using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

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
internal sealed class CallableAgentResolver
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
        => ResolveCatalog().ResolveAsync(agentName, culture: null, cancellationToken);

    /// <summary>Resolves the agent with the given name, pinned to a stored version.</summary>
    /// <param name="agentName">Agent name.</param>
    /// <param name="version">
    /// The version to run. <see langword="null"/> resolves the current version, exactly like
    /// <see cref="ResolveAsync(string, CancellationToken)"/>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The agent wrapped with the run-recording wrapper; <see langword="null"/> when absent.</returns>
    /// <exception cref="TraconException">
    /// <paramref name="version"/> is given and the agent keeps no version history (it is defined in code),
    /// or that version does not exist.
    /// </exception>
    public ValueTask<AIAgent?> ResolveAsync(string agentName, int? version, CancellationToken cancellationToken = default)
        => version is null
            ? ResolveAsync(agentName, cancellationToken)
            : ResolveCatalog().ResolveAsync(agentName, version, culture: null, cancellationToken);

    /// <summary>Finds the first agent version pin of a workflow definition that cannot be honoured.</summary>
    /// <param name="definition">The definition to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>User-facing error text; <see langword="null"/> when there is no pin or every pin holds.</returns>
    /// <remarks>
    /// Reads only descriptors and one version row, never compiles an agent, so it is safe to
    /// run for every compile of a workflow. A definition with no pin pays nothing.
    /// </remarks>
    public async ValueTask<string?> FindPinProblemAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return await AgentVersionPin
            .FindProblemAsync(definition, ResolveCatalog(), _services.GetService<IAgentDefinitionStore>(), cancellationToken)
            .ConfigureAwait(false);
    }

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

        var byName = await DescribeCatalogAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CallableAgentInfo>(callableAgentNames.Count);

        foreach (var name in callableAgentNames)
        {
            byName.TryGetValue(name, out var descriptor);
            result.Add(new CallableAgentInfo(name, descriptor?.Description, descriptor?.Version ?? 0));
        }

        return result;
    }

    /// <summary>
    /// Resolves the callable agents of a definition together with their descriptions, each from the version
    /// the definition runs: the pinned one, or the current one.
    /// </summary>
    /// <param name="callerName">The calling agent, for the refusal.</param>
    /// <param name="callableAgents">The references to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// One entry for each reference, in the same order. A pinned entry carries the pinned version and the
    /// description of that version; an unpinned one carries the catalog's current version and description. A name
    /// not present in the catalog gets no description, as in <see cref="DescribeAsync(IReadOnlyList{string}, CancellationToken)"/>.
    /// </returns>
    /// <exception cref="TraconCompilationException">
    /// A reference pins an agent defined in code, or a version the agent no longer has, or a name is listed
    /// twice. The definition was checked when it was saved; the agent can change after that, and a pin that no
    /// longer holds fails the caller by name instead of running another version.
    /// </exception>
    public async ValueTask<IReadOnlyList<CallableAgentInfo>> DescribeAsync(
        string callerName,
        IReadOnlyList<CallableAgentReference> callableAgents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callableAgents);

        if (callableAgents.Count == 0)
        {
            return [];
        }

        var byName = await DescribeCatalogAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<CallableAgentInfo>(callableAgents.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in callableAgents)
        {
            // The save refuses a repeated name, but a definition written in code never met the save. Two entries
            // of one name would also share one pin, and the second would silently decide which version runs.
            if (!seen.Add(reference.Name))
            {
                throw new TraconCompilationException(CallableAgentPins.DuplicateMessage(callerName, reference.Name))
                {
                    AgentName = callerName,
                };
            }

            byName.TryGetValue(reference.Name, out var descriptor);

            result.Add(reference.Version is { } version && descriptor is not null
                ? await DescribePinnedAsync(callerName, reference.Name, version, descriptor, cancellationToken).ConfigureAwait(false)
                : new CallableAgentInfo(reference.Name, descriptor?.Description, reference.Version ?? descriptor?.Version ?? 0));
        }

        return result;
    }

    private async ValueTask<CallableAgentInfo> DescribePinnedAsync(
        string callerName,
        string calleeName,
        int version,
        AgentDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (descriptor.Origin == AgentDefinitionOrigin.Code)
        {
            throw new TraconCompilationException(CallableAgentPins.CodeAgentMessage(callerName, calleeName, version))
            {
                AgentName = callerName,
            };
        }

        // A stored agent's description is that of the pinned version: the model reads it to decide when to call.
        // A custom source cannot be read this way; its description stays the current one, and the catalog
        // refuses the call when the source has no such version.
        if (descriptor.Origin == AgentDefinitionOrigin.Database &&
            _services.GetService<IAgentDefinitionStore>() is { } store)
        {
            var pinned = await store.GetVersionAsync(calleeName, version, cancellationToken).ConfigureAwait(false)
                ?? throw new TraconCompilationException(CallableAgentPins.VersionNotFoundMessage(callerName, calleeName, version))
                {
                    AgentName = callerName,
                };

            return new CallableAgentInfo(calleeName, pinned.Description, version);
        }

        return new CallableAgentInfo(calleeName, descriptor.Description, version);
    }

    private async ValueTask<Dictionary<string, AgentDescriptor>> DescribeCatalogAsync(CancellationToken cancellationToken)
    {
        var descriptors = await ListAsync(cancellationToken).ConfigureAwait(false);
        var byName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);

        foreach (var descriptor in descriptors)
        {
            byName[descriptor.Name] = descriptor;
        }

        return byName;
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
