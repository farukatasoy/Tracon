using Microsoft.Agents.AI.Workflows;

namespace AgentPrism;

/// <summary>
/// Kodda tanimli ve veritabaninda saklanan workflow'lari tek bir katalogda birlestirir.
/// </summary>
/// <remarks>
/// Ad cakismasinda <strong>kod kazanir</strong>. Ayni kural agent katalogunda da
/// gecerlidir (K-019): kodda tanimli olan derleme zamaninda dogrulanmistir ve
/// veritabanina yazma yetkisi olan biri, kodda kayitli bir davranisi ele
/// geciremez.
/// </remarks>
internal sealed class WorkflowCatalog
{
    private readonly Dictionary<string, CodeWorkflowRegistration> _codeWorkflows;
    private readonly IWorkflowDefinitionStore _store;
    private readonly WorkflowDefinitionCompiler _compiler;
    private readonly ITenantContext _tenantContext;
    private readonly IServiceProvider _services;

    /// <summary>Yeni bir katalog olusturur.</summary>
    /// <param name="codeWorkflows">Kodda kayitli workflow'lar.</param>
    /// <param name="store">Veritabani tanim deposu.</param>
    /// <param name="compiler">Tanim derleyicisi.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="services">Kod fabrikalarinin kullanacagi servis saglayici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public WorkflowCatalog(
        IEnumerable<CodeWorkflowRegistration> codeWorkflows,
        IWorkflowDefinitionStore store,
        WorkflowDefinitionCompiler compiler,
        ITenantContext tenantContext,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(codeWorkflows);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(services);

        _codeWorkflows = codeWorkflows.ToDictionary(
            static registration => registration.Name,
            StringComparer.Ordinal);
        _store = store;
        _compiler = compiler;
        _tenantContext = tenantContext;
        _services = services;
    }

    /// <summary>Katalogdaki tum workflow'lari ada gore listeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ozetler.</returns>
    public async ValueTask<IReadOnlyList<WorkflowDescriptor>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        var descriptors = new Dictionary<string, WorkflowDescriptor>(StringComparer.Ordinal);

        var stored = await _store.ListAsync(_tenantContext.TenantId, cancellationToken).ConfigureAwait(false);

        foreach (var definition in stored)
        {
            descriptors[definition.Name] = Describe(definition);
        }

        // Kod kayitlari SONRA yazilir ve veritabanindakinin uzerine gecer.
        foreach (var registration in _codeWorkflows.Values)
        {
            descriptors[registration.Name] = new WorkflowDescriptor
            {
                Name = registration.Name,
                Description = registration.Description,
                Origin = AgentDefinitionOrigin.Code,
            };
        }

        return [.. descriptors.Values.OrderBy(static descriptor => descriptor.Name, StringComparer.Ordinal)];
    }

    /// <summary>Tek bir workflow'un ozetini getirir.</summary>
    /// <param name="name">Workflow adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ozet; yoksa <see langword="null"/>.</returns>
    public async ValueTask<WorkflowDescriptor?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_codeWorkflows.TryGetValue(name, out var registration))
        {
            return new WorkflowDescriptor
            {
                Name = registration.Name,
                Description = registration.Description,
                Origin = AgentDefinitionOrigin.Code,
            };
        }

        var definition = await _store
            .GetAsync(_tenantContext.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        return definition is null ? null : Describe(definition);
    }

    /// <summary>Adi verilen workflow'u calistirilabilir bir grafa cevirir.</summary>
    /// <param name="name">Workflow adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kurulmus graf; workflow katalogda yoksa <see langword="null"/>.</returns>
    /// <exception cref="AgentPrismException">Tanim gecersizse veya bir agent bulunamiyorsa.</exception>
    /// <remarks>
    /// Graf <strong>her calistirmada yeniden kurulur</strong>, onbelleklenmez.
    /// Sebep: Microsoft Agent Framework executor'lari durum tasir ve ayni
    /// <see cref="Workflow"/> ornegini es zamanli iki calistirmada kullanmak
    /// durumu paylastirirdi. Kurulum maliyeti bir model cagrisinin yaninda
    /// olculemeyecek kadar kucuktur.
    /// </remarks>
    public async ValueTask<Workflow?> ResolveAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_codeWorkflows.TryGetValue(name, out var registration))
        {
            return registration.Factory(_services);
        }

        var definition = await _store
            .GetAsync(_tenantContext.TenantId, name, cancellationToken)
            .ConfigureAwait(false);

        return definition is null
            ? null
            : await _compiler.CompileAsync(definition, cancellationToken).ConfigureAwait(false);
    }

    private static WorkflowDescriptor Describe(WorkflowDefinition definition)
        => new()
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Origin = AgentDefinitionOrigin.Database,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            Version = definition.Version,
            UpdatedAt = definition.UpdatedAt,
        };
}
