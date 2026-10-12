using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// Resolves the agent for a run that carries on an earlier run: the earlier
/// run's version when the agent's source keeps version history, the current
/// version otherwise.
/// </summary>
/// <remarks>
/// <para>
/// A run record names a version even for an agent that has none to pin: a
/// declarative code agent is recorded as version 1, and a pinned resolution of
/// a source without version history throws. The version is therefore pinned
/// only when the serving source can honor it.
/// </para>
/// <para>
/// A host can replace <see cref="IAgentCatalog"/>. Such a catalog does not
/// say which source serves a name, so its descriptor's origin is the signal:
/// a code agent has no version history, the same rule the experiment endpoint
/// applies.
/// </para>
/// </remarks>
internal static class SourceRunAgentResolver
{
    /// <summary>Resolves the agent the continuing run runs on.</summary>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="sourceVersion">The version the earlier run recorded, if any.</param>
    /// <param name="culture">The culture to resolve the instructions with.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The agent (<see langword="null"/> when no source defines the name) and
    /// the version it was pinned to (<see langword="null"/> when it is the
    /// current version).
    /// </returns>
    public static async ValueTask<(AIAgent? Agent, int? PinnedVersion)> ResolveAsync(
        IAgentCatalog catalog,
        string agentName,
        int? sourceVersion,
        string? culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        if (sourceVersion is { } version &&
            await KeepsVersionHistoryAsync(catalog, agentName, cancellationToken).ConfigureAwait(false))
        {
            var pinned = await catalog.ResolveAsync(agentName, version, culture, cancellationToken).ConfigureAwait(false);

            return (pinned, version);
        }

        var current = await catalog.ResolveAsync(agentName, culture, cancellationToken).ConfigureAwait(false);

        return (current, null);
    }

    private static async ValueTask<bool> KeepsVersionHistoryAsync(
        IAgentCatalog catalog,
        string agentName,
        CancellationToken cancellationToken)
    {
        if (catalog is CompositeAgentCatalog composite)
        {
            return await composite.ServesFromVersionedSourceAsync(agentName, cancellationToken).ConfigureAwait(false);
        }

        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptor = descriptors.FirstOrDefault(
            candidate => string.Equals(candidate.Name, agentName, StringComparison.Ordinal));

        return descriptor is not null && descriptor.Origin != AgentDefinitionOrigin.Code;
    }
}
