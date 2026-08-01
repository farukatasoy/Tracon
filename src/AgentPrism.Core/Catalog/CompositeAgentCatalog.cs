using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Tum <see cref="IAgentSource"/> kaynaklarini tek bir katalogda birlestirir ve
/// cozulen agent'lari kayitli <see cref="IAgentDecorator"/> sarmalayicilariyla sarar.
/// </summary>
/// <remarks>
/// Kaynaklar <see cref="IAgentSource.Priority"/> sirasina gore denenir.
/// Ad cakismasinda onceligi yuksek (sayisi kucuk) kaynak kazanir; kaybeden
/// kaynak listeye eklenmez ve bir uyari loglanir.
/// </remarks>
public sealed class CompositeAgentCatalog : IAgentCatalog
{
    private readonly IAgentSource[] _sources;
    private readonly IAgentDecorator[] _decorators;
    private readonly ILogger<CompositeAgentCatalog> _logger;

    /// <summary>Yeni bir birlesik katalog olusturur.</summary>
    /// <param name="sources">Agent kaynaklari.</param>
    /// <param name="decorators">Cozulen agent'lara uygulanacak sarmalayicilar.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
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
                        "'{AgentName}' agent'i hem '{Winner}' hem '{Loser}' kaynaginda tanimli. " +
                        "Oncelik sirasina gore '{Winner}' kullanilacak.",
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
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        foreach (var source in _sources)
        {
            var agent = await source.ResolveAsync(agentName, cancellationToken).ConfigureAwait(false);

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

    private static async ValueTask<AgentDescriptor> FindDescriptorAsync(
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

        // Kaynak agent'i cozdu ancak listesinde gostermiyor. Sarmalayicilarin
        // calisabilmesi icin en az bilgiyi tasiyan bir ozet uretiyoruz.
        return new AgentDescriptor
        {
            Name = agentName,
            Origin = AgentDefinitionOrigin.Code,
            SourceName = source.Name,
        };
    }
}
