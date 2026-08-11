using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// <see cref="IAgentDefinitionStore"/> icindeki tanimlari katalogda gosteren kaynak.
/// Arayuzden olusturulan agent'lar bu kaynaktan gelir.
/// </summary>
public sealed class DefinitionStoreAgentSource : IVersionedAgentSource
{
    private readonly IAgentDefinitionStore _store;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly CompiledAgentCache _cache;
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir veritabani kaynagi olusturur.</summary>
    /// <param name="store">Tanim deposu.</param>
    /// <param name="compiler">Tanimlari derleyen derleyici.</param>
    /// <param name="cache">Derlenmis agent onbellegi.</param>
    /// <param name="tenantContext">
    /// Kiraci baglami. Onbellek anahtarina kiraciyi eklemek icin gerekir — bkz.
    /// <see cref="CompiledAgentCache"/>'in kiraci notu.
    /// </param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
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
