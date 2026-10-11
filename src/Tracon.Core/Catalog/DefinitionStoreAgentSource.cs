using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// A source that exposes definitions from <see cref="IAgentDefinitionStore"/> in the catalog.
/// Agents created through the UI come from this source.
/// </summary>
internal sealed class DefinitionStoreAgentSource : IVersionedAgentSource, IDescribingAgentSource
{
    private readonly IAgentDefinitionStore _store;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly CompiledAgentCache _cache;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new database source.</summary>
    /// <param name="store">The definition store.</param>
    /// <param name="compiler">The compiler that compiles definitions.</param>
    /// <param name="cache">The compiled agent cache.</param>
    /// <param name="tenantContext">
    /// The tenant context. It is required to add the tenant to the cache key. See
    /// the tenant note in <see cref="CompiledAgentCache"/>.
    /// </param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public DefinitionStoreAgentSource(
        IAgentDefinitionStore store,
        AgentDefinitionCompiler compiler,
        CompiledAgentCache cache,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _store = store;
        _compiler = compiler;
        _cache = cache;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public string Name => "database";

    /// <inheritdoc />
    public int Priority => AgentSourcePriority.Database;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definitions = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptors = new List<AgentDescriptor>(definitions.Count);

        foreach (var definition in definitions)
        {
            descriptors.Add(AgentDescriptorProjection.FromDefinition(definition, AgentDefinitionOrigin.Database, Name));
        }

        return descriptors;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        var definition = await _store.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        return await _compiler
            .CompileCachedAsync(definition, _cache, _tenantContext.TenantId, culture, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveVersionAsync(
        string agentName,
        int version,
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        var definition = await _store.GetVersionAsync(agentName, version, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        return await _compiler
            .CompileCachedAsync(definition, _cache, _tenantContext.TenantId, culture, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One store read serves both the agent and its descriptor: the
    /// catalog used to read the definition to compile it and then LIST the
    /// store for the descriptor, so a pinned version got the current
    /// version's descriptor, and a save between the two reads paired one
    /// version's agent with the next one's descriptor.
    /// </remarks>
    public async ValueTask<DescribedAgent?> ResolveDescribedAsync(
        string agentName,
        int? version,
        string? culture,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentName);
        cancellationToken.ThrowIfCancellationRequested();

        var definition = version is { } pinned
            ? await _store.GetVersionAsync(agentName, pinned, cancellationToken).ConfigureAwait(false)
            : await _store.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        var agent = await _compiler
            .CompileCachedAsync(definition, _cache, _tenantContext.TenantId, culture, cancellationToken)
            .ConfigureAwait(false);

        return new DescribedAgent(agent, AgentDescriptorProjection.FromDefinition(definition, AgentDefinitionOrigin.Database, Name));
    }
}
