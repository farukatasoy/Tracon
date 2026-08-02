using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Kodda tanimlanmis agent'lari katalogda gosteren kaynak.
/// </summary>
/// <remarks>
/// Onceligi 0'dir, yani en yuksektir. Ayni ada sahip bir veritabani tanimi varsa
/// kod kazanir; sebep: kod derleme zamaninda dogrulanmistir, veritabani tanimi
/// ise calisma zamani verisidir.
/// </remarks>
public sealed class CodeAgentSource : IAgentSource
{
    private readonly Dictionary<string, CodeAgentRegistration> _registrations;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly CompiledAgentCache _cache;
    private readonly IServiceProvider _services;

    /// <summary>Yeni bir kod kaynagi olusturur.</summary>
    /// <param name="registrations">Kod agent kayitlari.</param>
    /// <param name="compiler">Bildirimsel tanimlari derleyen derleyici.</param>
    /// <param name="cache">Derlenmis agent onbellegi.</param>
    /// <param name="services">Fabrika tabanli kayitlara verilecek servis saglayici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismException">Ayni agent adi birden cok kez kaydedilmisse.</exception>
    public CodeAgentSource(
        IEnumerable<CodeAgentRegistration> registrations,
        AgentDefinitionCompiler compiler,
        CompiledAgentCache cache,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(services);

        _compiler = compiler;
        _cache = cache;
        _services = services;
        _registrations = new Dictionary<string, CodeAgentRegistration>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            if (!_registrations.TryAdd(registration.Name, registration))
            {
                throw new AgentPrismException(
                    $"'{registration.Name}' adinda birden cok kod agent'i kaydedilmis. Agent adlari benzersiz olmalidir.");
            }
        }
    }

    /// <inheritdoc />
    public string Name => "code";

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = new List<AgentDescriptor>(_registrations.Count);

        foreach (var registration in _registrations.Values)
        {
            descriptors.Add(new AgentDescriptor
            {
                Name = registration.Name,
                DisplayName = registration.DisplayName,
                Description = registration.Description,
                Origin = AgentDefinitionOrigin.Code,
                SourceName = Name,
                Model = registration.Definition?.Model,
                ToolNames = registration.Definition?.ToolNames ?? [],
                SkillNames = registration.Definition?.SkillNames ?? [],
                CallableAgentNames = registration.Definition?.CallableAgentNames ?? [],
                UsesHarness = registration.Definition?.Harness is not null,
            });
        }

        return new ValueTask<IReadOnlyList<AgentDescriptor>>(descriptors);
    }

    /// <inheritdoc />
    public async ValueTask<AIAgent?> ResolveAsync(string agentName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentName);

        if (!_registrations.TryGetValue(agentName, out var registration))
        {
            return null;
        }

        if (registration.Definition is not { } definition)
        {
            return registration.Factory!(_services);
        }

        var skills = await _compiler.ResolveSkillsAsync(definition, cancellationToken).ConfigureAwait(false);
        var callable = await _compiler.ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);

        var agent = _cache.GetOrAdd(
            definition.Name,
            definition.Version,
            CompiledAgentCache.CombineFingerprints(skills.Fingerprint, callable.Fingerprint),
            () => _compiler.Compile(definition, callable));

        return agent;
    }
}
