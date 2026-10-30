using System.Diagnostics.CodeAnalysis;
using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// The definitions an import is about to write, read as if they were already saved: a read-only view
/// that lets a definition be validated against the target AND the package together.
/// </summary>
/// <remarks>
/// <para>
/// An import must say "valid" before it writes, and an agent in the package may call, pin, review with or
/// share instructions from another agent or skill of the same package. None of them is in the target yet,
/// so validating against the target alone would report every one of those references as missing.
/// </para>
/// <para>
/// The overlay is never saved, cached or listed. It reaches the validator only through the adapters
/// below, each of which refuses a write, and the compilation the validator runs is the uncached one
/// (<c>CompiledAgentCache</c> is read by the catalog, not by <c>CompileAsync</c>). A definition that
/// leaked into a catalog result would make a dry run change what a run in the target does.
/// </para>
/// </remarks>
internal sealed class DefinitionOverlay
{
    private readonly Dictionary<string, AgentDefinition> _agents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _skills = new(StringComparer.Ordinal);

    /// <summary>Initializes an overlay.</summary>
    /// <param name="agents">
    /// The agents of the package, each with the version it will have: the number is the one the version
    /// pins of the overlay agents are judged against.
    /// </param>
    /// <param name="skillNames">The names of the stored skills of the package.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public DefinitionOverlay(IEnumerable<AgentDefinition> agents, IEnumerable<string> skillNames)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(skillNames);

        foreach (var agent in agents)
        {
            _agents[agent.Name] = agent;
        }

        _skills.UnionWith(skillNames);
    }

    /// <summary>Gets an overlay that adds nothing.</summary>
    public static DefinitionOverlay Empty { get; } = new([], []);

    /// <summary>Gets the agents of the package.</summary>
    public IReadOnlyCollection<AgentDefinition> Agents => _agents.Values;

    /// <summary>Determines whether the package carries a stored skill with this name.</summary>
    /// <param name="name">The skill name.</param>
    /// <returns><see langword="true"/> when the package carries it.</returns>
    public bool HasSkill(string name) => _skills.Contains(name);

    /// <summary>Finds an agent of the package.</summary>
    /// <param name="name">The agent name.</param>
    /// <param name="definition">The definition, with the version it will have.</param>
    /// <returns><see langword="true"/> when the package carries it.</returns>
    public bool TryGetAgent(string name, [NotNullWhen(true)] out AgentDefinition? definition)
        => _agents.TryGetValue(name, out definition);

    /// <summary>Lays the package's agents over a catalog listing.</summary>
    /// <param name="catalog">The target catalog's descriptors.</param>
    /// <returns>
    /// The listing in which a stored agent of the package replaces the target's one of the same name and a
    /// new one is added. An agent the target defines in code, or in a custom source, stays as it is: code wins
    /// the name, so the package's copy would never run.
    /// </returns>
    public IReadOnlyList<AgentDescriptor> MergeDescriptors(IReadOnlyList<AgentDescriptor> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (_agents.Count == 0)
        {
            return catalog;
        }

        var merged = new List<AgentDescriptor>(catalog.Count + _agents.Count);
        var replaced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var descriptor in catalog)
        {
            if (descriptor.Origin == AgentDefinitionOrigin.Database && _agents.TryGetValue(descriptor.Name, out var incoming))
            {
                merged.Add(Describe(incoming));
                replaced.Add(descriptor.Name);
            }
            else
            {
                merged.Add(descriptor);
                replaced.Add(descriptor.Name);
            }
        }

        foreach (var (name, definition) in _agents)
        {
            if (!replaced.Contains(name))
            {
                merged.Add(Describe(definition));
            }
        }

        return merged;
    }

    private static AgentDescriptor Describe(AgentDefinition definition)
        => AgentDescriptorProjection.FromDefinition(
            definition,
            AgentDefinitionOrigin.Database,
            AgentDefinitionLookup.SourceName(AgentDefinitionOrigin.Database));
}

/// <summary>An <see cref="IAgentCatalog"/> that lists the target's agents with a <see cref="DefinitionOverlay"/> laid over them.</summary>
/// <remarks>
/// Resolving an agent is refused on purpose: an overlay agent has never been compiled into a catalog, and a
/// validation path that started to resolve one would be reading target state the package has not changed.
/// </remarks>
internal sealed class OverlayAgentCatalog : IAgentCatalog
{
    private readonly DefinitionOverlay _overlay;
    private readonly IAgentCatalog _inner;

    /// <summary>Initializes the adapter.</summary>
    /// <param name="overlay">The package's definitions.</param>
    /// <param name="inner">The target's catalog.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public OverlayAgentCatalog(DefinitionOverlay overlay, IAgentCatalog inner)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(inner);

        _overlay = overlay;
        _inner = inner;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        => _overlay.MergeDescriptors(await _inner.ListAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture, CancellationToken cancellationToken)
        => throw Refused();

    /// <inheritdoc />
    public ValueTask<AIAgent?> ResolveAsync(
        string agentName,
        int? version,
        string? culture = null,
        CancellationToken cancellationToken = default)
        => throw Refused();

    private static NotSupportedException Refused()
        => new("An overlay catalog lists agents; it never resolves one.");
}

/// <summary>An <see cref="IAgentDefinitionStore"/> that reads the target's definitions with a <see cref="DefinitionOverlay"/> laid over them.</summary>
/// <remarks>
/// A write is refused: the overlay is a way to ask "what if", not a way to save.
/// </remarks>
internal sealed class OverlayAgentDefinitionStore : IAgentDefinitionStore
{
    private readonly DefinitionOverlay _overlay;
    private readonly IAgentDefinitionStore _inner;

    /// <summary>Initializes the adapter.</summary>
    /// <param name="overlay">The package's definitions.</param>
    /// <param name="inner">The target's store.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public OverlayAgentDefinitionStore(DefinitionOverlay overlay, IAgentDefinitionStore inner)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(inner);

        _overlay = overlay;
        _inner = inner;
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default)
        => _overlay.TryGetAgent(name, out var definition)
            ? ValueTask.FromResult<AgentDefinition?>(definition)
            : _inner.GetAsync(name, cancellationToken);

    /// <inheritdoc />
    public ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default)
        => _overlay.TryGetAgent(name, out var definition) && definition.Version == version
            ? ValueTask.FromResult<AgentDefinition?>(definition)
            : _inner.GetVersionAsync(name, version, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _inner.ListAsync(cancellationToken).ConfigureAwait(false);
        var merged = new List<AgentDefinition>(stored.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var definition in stored)
        {
            merged.Add(_overlay.TryGetAgent(definition.Name, out var incoming) ? incoming : definition);
            seen.Add(definition.Name);
        }

        foreach (var added in _overlay.Agents)
        {
            if (!seen.Contains(added.Name))
            {
                merged.Add(added);
            }
        }

        return merged;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
    {
        var stored = await _inner.ListVersionsAsync(name, cancellationToken).ConfigureAwait(false);

        return _overlay.TryGetAgent(name, out var incoming) ? [incoming, .. stored] : stored;
    }

    /// <inheritdoc />
    public ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default)
        => throw Refused();

    /// <inheritdoc />
    public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
        => throw Refused();

    /// <inheritdoc />
    public ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default)
        => throw Refused();

    private static NotSupportedException Refused()
        => new("An overlay store reads definitions; it never writes one.");
}
