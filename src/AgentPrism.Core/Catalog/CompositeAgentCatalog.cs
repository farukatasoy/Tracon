using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Combines all <see cref="IAgentSource"/> sources in one catalog and wraps resolved
/// agents with registered <see cref="IAgentDecorator"/> decorators.
/// </summary>
/// <remarks>
/// Sources are tried in <see cref="IAgentSource.Priority"/> order. When names collide,
/// the higher-priority source with the lower number wins. The losing source is omitted
/// from the list and the catalog logs a warning.
/// </remarks>
internal sealed class CompositeAgentCatalog : IAgentCatalog
{
    private readonly IAgentSource[] _sources;
    private readonly IAgentDecorator[] _decorators;
    private readonly ILogger<CompositeAgentCatalog> _logger;

    /// <summary>Initializes a new composite catalog.</summary>
    /// <param name="sources">The agent sources.</param>
    /// <param name="decorators">The decorators applied to resolved agents.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public CompositeAgentCatalog(
        IEnumerable<IAgentSource> sources,
        IEnumerable<IAgentDecorator> decorators,
        ILogger<CompositeAgentCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(decorators);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = [.. sources.OrderBy(static source => source.Priority)];
        _decorators = [.. decorators.OrderByDescending(static decorator => decorator.Order)];
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var byName = new Dictionary<string, AgentDescriptor>(StringComparer.Ordinal);

        foreach (var source in _sources)
        {
            var descriptors = await source.ListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var descriptor in descriptors)
            {
                if (byName.TryGetValue(descriptor.Name, out var winner))
                {
                    _logger.LogWarning(
                        "Agent '{AgentName}' is defined in both source '{Winner}' and source '{Loser}'. " +
                        "Source '{Winner}' will be used based on priority.",
                        descriptor.Name,
                        winner.SourceName,
                        source.Name,
                        winner.SourceName);

                    continue;
                }

                byName.Add(descriptor.Name, descriptor);
            }
        }

        var result = byName.Values.ToList();
        result.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            var agent = await source.ResolveAsync(agentName, culture, cancellationToken).ConfigureAwait(false);

            if (agent is null)
            {
                continue;
            }

            var descriptor = await FindDescriptorAsync(source, agentName, cancellationToken).ConfigureAwait(false);

            foreach (var decorator in _decorators)
            {
                agent = decorator.Decorate(agent, descriptor);
            }

            return agent;
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(
        string agentName,
        int? version,
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        if (version is null)
        {
            return await ResolveAsync(agentName, culture, cancellationToken).ConfigureAwait(false);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            // If this source does not define the agent, continue with the next source.
            // Priority order matches ListAsync and ResolveAsync: lower Priority runs first.
            var descriptor = await FindDescriptorOrNullAsync(source, agentName, cancellationToken).ConfigureAwait(false);

            if (descriptor is null)
            {
                continue;
            }

            if (source is not IVersionedAgentSource versioned)
            {
                throw new AgentPrismException(
                    $"Agent '{agentName}' comes from source '{source.Name}' and does not keep version history " +
                    "(code source). Runs or experiments against a specific version are not supported for this agent.");
            }

            var agent = await versioned.ResolveVersionAsync(agentName, version.Value, culture, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException($"Version {version.Value} of agent '{agentName}' was not found.");

            foreach (var decorator in _decorators)
            {
                agent = decorator.Decorate(agent, descriptor);
            }

            return agent;
        }

        return null;
    }

    private static async ValueTask<AgentDescriptor> FindDescriptorAsync(
        IAgentSource source,
        string agentName,
        CancellationToken cancellationToken)
    {
        var descriptor = await FindDescriptorOrNullAsync(source, agentName, cancellationToken).ConfigureAwait(false);

        // The source resolved the agent but does not list it. Build a summary with
        // the minimum information needed for decorators to run.
        return descriptor ?? new AgentDescriptor
        {
            Name = agentName,
            Origin = AgentDefinitionOrigin.Code,
            SourceName = source.Name,
        };
    }

    private static async ValueTask<AgentDescriptor?> FindDescriptorOrNullAsync(
        IAgentSource source,
        string agentName,
        CancellationToken cancellationToken)
    {
        var descriptors = await source.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var descriptor in descriptors)
        {
            if (string.Equals(descriptor.Name, agentName, StringComparison.Ordinal))
            {
                return descriptor;
            }
        }

        return null;
    }
}
