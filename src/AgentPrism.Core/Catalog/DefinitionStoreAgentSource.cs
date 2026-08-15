using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// A source that exposes definitions from <see cref="IAgentDefinitionStore"/> in the catalog.
/// Agents created through the UI come from this source.
/// </summary>
public sealed class DefinitionStoreAgentSource : IVersionedAgentSource
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
    public int Priority => 100;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var definitions = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        var descriptors = new List<AgentDescriptor>(definitions.Count);

        foreach (var definition in definitions)
        {
            descriptors.Add(new AgentDescriptor
            {
                Name = definition.Name,
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Origin = AgentDefinitionOrigin.Database,
                SourceName = Name,
                Version = definition.Version,
                Model = definition.Model,
                ToolNames = definition.ToolNames,
                SkillNames = definition.SkillNames,
                CallableAgentNames = definition.CallableAgentNames,
                UsesHarness = definition.Harness is not null,
                UpdatedAt = definition.UpdatedAt,
            });
        }

        return descriptors;
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);

        var definition = await _store.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        var skills = await _compiler.ResolveSkillsAsync(definition, cancellationToken).ConfigureAwait(false);
        var callable = await _compiler.ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);

        return _cache.GetOrAdd(
            _tenantContext.TenantId,
            definition.Name,
            definition.Version,
            CompiledAgentCache.CombineFingerprints(skills.Fingerprint, callable.Fingerprint),
            () => _compiler.Compile(definition, callable));
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveVersionAsync(string agentName, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);

        var definition = await _store.GetVersionAsync(agentName, version, cancellationToken).ConfigureAwait(false);

        if (definition is null)
        {
            return null;
        }

        var skills = await _compiler.ResolveSkillsAsync(definition, cancellationToken).ConfigureAwait(false);
        var callable = await _compiler.ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);

        return _cache.GetOrAdd(
            _tenantContext.TenantId,
            definition.Name,
            definition.Version,
            CompiledAgentCache.CombineFingerprints(skills.Fingerprint, callable.Fingerprint),
            () => _compiler.Compile(definition, callable));
    }
}
