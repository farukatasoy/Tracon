using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Collects the installation's self-diagnostic summary report.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Has no side effects.</strong> It makes no model calls: model provider
/// status is read from <see cref="ModelProviderHealthCache"/> without triggering
/// a new health check. When SQL persistence is registered, it performs a light
/// connectivity probe through <see cref="ISqlPersistenceDiagnostics.GetSnapshotAsync"/>;
/// it does not apply migrations.
/// </para>
/// <para>
/// The generated <see cref="AgentPrismDiagnosticsReport"/> contains no <c>secret</c>.
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

    /// <summary>Initializes a diagnostics collector.</summary>
    /// <param name="providers">The registered model providers.</param>
    /// <param name="healthCache">The model provider health cache.</param>
    /// <param name="sqlDiagnostics">The active SQL provider diagnostics contract, with zero or one instance.</param>
    /// <param name="sqlMarkers">The registered SQL provider markers, counted to detect more than one active provider.</param>
    /// <param name="agentCatalog">The agent catalog.</param>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="circuitBreaker">The circuit breaker. No circuit is open when it is not registered.</param>
    /// <param name="logger">
    /// The logger. When absent, a catalog read failure is ignored and the report
    /// leaves <see cref="AgentPrismDiagnosticsReport.AgentCount"/> empty.
    /// </param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
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

    /// <summary>Collects the installation diagnostics report.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The report. <see cref="AgentPrismDiagnosticsReport.UiEmbedded"/> always
    /// returns <see langword="false"/> because the field belongs to the
    /// <c>AgentPrism.AspNetCore</c> layer, which <c>AgentPrism.Core</c> does not
    /// know. The caller must set it with a <c>with</c> expression.
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

            // The same provider, for example UseOpenAI() registering two instances
            // for ChatCompletions and Responses, can report one configuration key
            // more than once. The report has one row per key.
            if (provider is IModelProviderConfigurationDiagnostics configProvider
                && configProvider.GetConfigurationDiagnostic() is { } diagnostic
                && seenConfigurationKeys.Add(diagnostic.Key))
            {
                configuration.Add(diagnostic);
            }
        }

        // 🚨 A catalog query must not crash this report. The diagnostics endpoint
        // is primarily used while the schema is not yet ready, for example when an
        // operator wants to inspect pending migrations with AutoApplyMigrations=false.
        // Querying before returning the collected migration information would make
        // the endpoint return 500 exactly when it is needed. Observed: MT-PG-051.
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
                "The agent catalog could not be read for the diagnostics report; the report returns with an empty " +
                "AgentCount field. The schema might not be applied yet.");

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
