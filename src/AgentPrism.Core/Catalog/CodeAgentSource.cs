using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// A source that exposes code-defined agents in the catalog.
/// </summary>
/// <remarks>
/// Its priority is 0, the highest. If a database definition has the same name,
/// code wins because it is validated at build time while the database definition is run-time data.
/// </remarks>
public sealed class CodeAgentSource : IAgentSource
{
    private readonly Dictionary<string, CodeAgentRegistration> _registrations;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly CompiledAgentCache _cache;
    private readonly IServiceProvider _services;
    private readonly ITenantContext _tenantContext;

    /// <summary>Initializes a new code source.</summary>
    /// <param name="registrations">The code agent registrations.</param>
    /// <param name="compiler">The compiler for declarative definitions.</param>
    /// <param name="cache">The compiled agent cache.</param>
    /// <param name="services">The service provider supplied to factory registrations.</param>
    /// <param name="tenantContext">
    /// The tenant context. Code agent definitions are shared across tenants, but
    /// <see cref="AgentDefinitionCompiler"/> can bind tenant-dependent tools, such as
    /// semantic search, to the ambient tenant at compilation time. See <c>AddVectorSearchTool</c>.
    /// Adding the tenant to the cache key prevents that binding from leaking to another tenant.
    /// </param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismException">The same agent name is registered more than once.</exception>
    public CodeAgentSource(
        IEnumerable<CodeAgentRegistration> registrations,
        AgentDefinitionCompiler compiler,
        CompiledAgentCache cache,
        IServiceProvider services,
        ITenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _compiler = compiler;
        _cache = cache;
        _services = services;
        _tenantContext = tenantContext;
        _registrations = new Dictionary<string, CodeAgentRegistration>(StringComparer.Ordinal);

        foreach (var registration in registrations)
        {
            if (!_registrations.TryAdd(registration.Name, registration))
            {
                throw new AgentPrismException(
                    $"More than one code agent is registered with name '{registration.Name}'. Agent names must be unique.");
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
            _tenantContext.TenantId,
            definition.Name,
            definition.Version,
            CompiledAgentCache.CombineFingerprints(skills.Fingerprint, callable.Fingerprint),
            () => _compiler.Compile(definition, callable));

        return agent;
    }
}
