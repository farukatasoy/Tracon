using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Kurulumun kendi kendini denetleyen ozet raporunu toplar (Faz 33).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Yan etkisizdir.</strong> Hicbir model cagrisi yapmaz — model saglayicisi
/// durumu <see cref="ModelProviderHealthCache"/>'in onbelleginden OKUNUR, yeni bir
/// denetim tetiklenmez. SQL saglayicisi kayitliysa hafif bir baglanti sinamasi
/// yapilir (<see cref="ISqlPersistenceDiagnostics.GetSnapshotAsync"/>); migration
/// UYGULANMAZ.
/// </para>
/// <para>
/// 🚨 Uretilen <see cref="AgentPrismDiagnosticsReport"/> hicbir <c>secret</c> tasimaz.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-059.
/// </para>
/// </remarks>
public sealed class AgentPrismDiagnosticsCollector
{
    private readonly IEnumerable<IModelProvider> _providers;
    private readonly ModelProviderHealthCache _healthCache;
    private readonly ModelProviderCircuitBreaker? _circuitBreaker;
    private readonly IEnumerable<ISqlPersistenceDiagnostics> _sqlDiagnostics;
    private readonly IEnumerable<SqlPersistenceRegistrationMarker> _sqlMarkers;
    private readonly IAgentCatalog _agentCatalog;
    private readonly IToolRegistry _toolRegistry;
    private readonly ILogger<AgentPrismDiagnosticsCollector>? _logger;

    /// <summary>Yeni bir teshis toplayicisi olusturur.</summary>
    /// <param name="providers">Kayitli model saglayicilari.</param>
    /// <param name="healthCache">Model saglayicisi saglik onbellegi.</param>
    /// <param name="sqlDiagnostics">Etkin SQL saglayicisinin teshis sozlesmesi (0 veya 1 ornek).</param>
    /// <param name="sqlMarkers">Kayitli SQL saglayici isaretleri (K-183 sayaci).</param>
    /// <param name="agentCatalog">Agent kataloğu.</param>
    /// <param name="toolRegistry">Tool defteri.</param>
    /// <param name="circuitBreaker">Devre kesici. Kayitli degilse hicbir devre acik sayilmaz.</param>
    /// <param name="logger">
    /// Gunlukcu. Verilmezse katalog okuma hatasi sessizce yutulur; rapor yine de
    /// <see cref="AgentPrismDiagnosticsReport.AgentCount"/> alanini bos birakir.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public AgentPrismDiagnosticsCollector(
        IEnumerable<IModelProvider> providers,
        ModelProviderHealthCache healthCache,
        IEnumerable<ISqlPersistenceDiagnostics> sqlDiagnostics,
        IEnumerable<SqlPersistenceRegistrationMarker> sqlMarkers,
        IAgentCatalog agentCatalog,
        IToolRegistry toolRegistry,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        ILogger<AgentPrismDiagnosticsCollector>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(healthCache);
        ArgumentNullException.ThrowIfNull(sqlDiagnostics);
        ArgumentNullException.ThrowIfNull(sqlMarkers);
        ArgumentNullException.ThrowIfNull(agentCatalog);
        ArgumentNullException.ThrowIfNull(toolRegistry);

        _providers = providers;
        _healthCache = healthCache;
        _sqlDiagnostics = sqlDiagnostics;
        _sqlMarkers = sqlMarkers;
        _agentCatalog = agentCatalog;
        _toolRegistry = toolRegistry;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    /// <summary>Kurulumun teshis raporunu toplar.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Rapor. <see cref="AgentPrismDiagnosticsReport.UiEmbedded"/> her zaman
    /// <see langword="false"/> doner — bu alan <c>AgentPrism.Core</c>'un bilmedigi
    /// <c>AgentPrism.AspNetCore</c> katmanina aittir; cagiran <c>with</c> ifadesiyle
    /// doldurmalidir.
    /// </returns>
    public async ValueTask<AgentPrismDiagnosticsReport> CollectAsync(CancellationToken cancellationToken = default)
    {
        var registeredCount = _sqlMarkers.Count();
        var sql = _sqlDiagnostics.FirstOrDefault();

        var persistenceProvider = "InMemory";
        var canConnect = true;
        var migrationsUpToDate = true;
        IReadOnlyList<string> pendingMigrations = [];

        if (sql is not null)
        {
            persistenceProvider = sql.ProviderName;

            var snapshot = await sql.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            canConnect = snapshot.CanConnect;
            pendingMigrations = snapshot.PendingMigrations;
            migrationsUpToDate = snapshot.CanConnect && snapshot.PendingMigrations.Count == 0;
        }

        var modelProviders = new List<ProviderDiagnostic>();
        var configuration = new List<ConfigurationDiagnostic>();
        var seenConfigurationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            var status = _healthCache.TryPeek(provider.Name, out var health)
                ? health.Status.ToString()
                : nameof(ModelProviderHealthStatus.Unknown);

            var circuitOpen = _circuitBreaker?.IsOpen(provider.Name, out _) ?? false;

            modelProviders.Add(new ProviderDiagnostic
            {
                Name = provider.Name,
                Status = status,
                CircuitOpen = circuitOpen,
            });

            // Ayni saglayici (ornek: UseOpenAI() ChatCompletions VE Responses icin iki
            // ornek kaydeder) ayni yapilandirma anahtarini birden fazla bildirebilir;
            // rapor anahtar basina tek satir tasir.
            if (provider is IModelProviderConfigurationDiagnostics configProvider
                && configProvider.GetConfigurationDiagnostic() is { } diagnostic
                && seenConfigurationKeys.Add(diagnostic.Key))
            {
                configuration.Add(diagnostic);
            }
        }

        // 🚨 Katalog sorgusu bu raporu COKERTMEZ. Tesihs ucunun birincil kullanim
        // ani, semanin HENUZ hazir olmadigi andir (AutoApplyMigrations=false ile
        // operator bekleyen migration'lari gormek ister). Sorguyu yukarida
        // toplanan migration bilgisinin onune gecirmek, ucu tam da ihtiyac
        // duyuldugu anda 500 yapardi. Olculdu: MT-PG-051.
        int? agentCount;

        try
        {
            var agents = await _agentCatalog.ListAsync(cancellationToken).ConfigureAwait(false);

            agentCount = agents.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(
                ex,
                "Teshis raporu icin agent katalogu okunamadi; rapor AgentCount alani bos " +
                "birakilarak dondurulur. Sema henuz uygulanmamis olabilir.");

            agentCount = null;
        }

        return new AgentPrismDiagnosticsReport
        {
            PersistenceProvider = persistenceProvider,
            RegisteredPersistenceProviders = registeredCount,
            CanConnect = canConnect,
            MigrationsUpToDate = migrationsUpToDate,
            PendingMigrations = pendingMigrations,
            ModelProviders = modelProviders,
            Configuration = configuration,
            UiEmbedded = false,
            ToolCount = _toolRegistry.List().Count,
            AgentCount = agentCount,
        };
    }
}
