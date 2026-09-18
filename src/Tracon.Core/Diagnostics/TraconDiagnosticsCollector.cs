using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

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
/// The generated <see cref="TraconDiagnosticsReport"/> contains no <c>secret</c>.
/// </para>
/// </remarks>
public sealed class TraconDiagnosticsCollector
{
    private readonly IEnumerable<IModelProvider> _providers;
    private readonly ModelProviderHealthCache _healthCache;
    private readonly ModelProviderCircuitBreaker? _circuitBreaker;
    private readonly IEnumerable<ISqlPersistenceDiagnostics> _sqlDiagnostics;
    private readonly IEnumerable<SqlPersistenceRegistrationMarker> _sqlMarkers;
    private readonly IAgentCatalog _agentCatalog;
    private readonly IEnumerable<IAgentSource> _agentSources;
    private readonly IToolRegistry _toolRegistry;
    private readonly ITenantContext _tenantContext;
    private readonly IRunAttributionContext _runAttributionContext;
    private readonly IToolAuthorizationHandler _toolAuthorizationHandler;
    private readonly IRunAuthorizationHandler _runAuthorizationHandler;
    private readonly IEnumerable<IRunEventSink> _runEventSinks;
    private readonly IAttachmentStorage? _attachmentStorage;
    private readonly IToolApprovalPresenter _approvalPresenter;
    private readonly TraconRunRecordingOptions _runRecording;
    private readonly ILogger<TraconDiagnosticsCollector>? _logger;

    /// <summary>Initializes a diagnostics collector.</summary>
    /// <param name="providers">The registered model providers.</param>
    /// <param name="healthCache">The model provider health cache.</param>
    /// <param name="sqlDiagnostics">The active SQL provider diagnostics contract, with zero or one instance.</param>
    /// <param name="sqlMarkers">The registered SQL provider markers, counted to detect more than one active provider.</param>
    /// <param name="agentCatalog">The agent catalog.</param>
    /// <param name="agentSources">The registered agent sources.</param>
    /// <param name="toolRegistry">The tool registry.</param>
    /// <param name="tenantContext">The bound tenant context, reported as an embedding point.</param>
    /// <param name="runAttributionContext">The bound run attribution context, reported as an embedding point.</param>
    /// <param name="toolAuthorizationHandler">The bound tool authorization handler, reported as an embedding point.</param>
    /// <param name="runAuthorizationHandler">The bound run/session authorization handler, reported as an embedding point.</param>
    /// <param name="runEventSinks">The registered run event sinks, reported as an embedding point.</param>
    /// <param name="attachmentStorage">The bound attachment storage, reported as an embedding point. <see langword="null"/> when content lives in the database.</param>
    /// <param name="approvalPresenter">The bound tool-approval presenter, reported as an embedding point.</param>
    /// <param name="circuitBreaker">The circuit breaker. No circuit is open when it is not registered.</param>
    /// <param name="options">
    /// The installation's options, read for the run recording settings. When
    /// absent, the report states the built-in defaults.
    /// </param>
    /// <param name="logger">
    /// The logger. When absent, a catalog read failure is ignored and the report
    /// leaves <see cref="TraconDiagnosticsReport.AgentCount"/> empty.
    /// </param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public TraconDiagnosticsCollector(
        IEnumerable<IModelProvider> providers,
        ModelProviderHealthCache healthCache,
        IEnumerable<ISqlPersistenceDiagnostics> sqlDiagnostics,
        IEnumerable<SqlPersistenceRegistrationMarker> sqlMarkers,
        IAgentCatalog agentCatalog,
        IEnumerable<IAgentSource> agentSources,
        IToolRegistry toolRegistry,
        ITenantContext tenantContext,
        IRunAttributionContext runAttributionContext,
        IToolAuthorizationHandler toolAuthorizationHandler,
        IRunAuthorizationHandler runAuthorizationHandler,
        IEnumerable<IRunEventSink> runEventSinks,
        IToolApprovalPresenter approvalPresenter,
        IAttachmentStorage? attachmentStorage = null,
        ModelProviderCircuitBreaker? circuitBreaker = null,
        IOptions<TraconOptions>? options = null,
        ILogger<TraconDiagnosticsCollector>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(healthCache);
        ArgumentNullException.ThrowIfNull(sqlDiagnostics);
        ArgumentNullException.ThrowIfNull(sqlMarkers);
        ArgumentNullException.ThrowIfNull(agentCatalog);
        ArgumentNullException.ThrowIfNull(agentSources);
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(runAttributionContext);
        ArgumentNullException.ThrowIfNull(toolAuthorizationHandler);
        ArgumentNullException.ThrowIfNull(runAuthorizationHandler);
        ArgumentNullException.ThrowIfNull(runEventSinks);
        ArgumentNullException.ThrowIfNull(approvalPresenter);

        _providers = providers;
        _healthCache = healthCache;
        _sqlDiagnostics = sqlDiagnostics;
        _sqlMarkers = sqlMarkers;
        _agentCatalog = agentCatalog;
        _agentSources = agentSources;
        _toolRegistry = toolRegistry;
        _tenantContext = tenantContext;
        _runAttributionContext = runAttributionContext;
        _toolAuthorizationHandler = toolAuthorizationHandler;
        _runAuthorizationHandler = runAuthorizationHandler;
        _runEventSinks = runEventSinks;
        _attachmentStorage = attachmentStorage;
        _approvalPresenter = approvalPresenter;
        _runRecording = options?.Value.RunRecording ?? new TraconRunRecordingOptions();
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }

    /// <summary>Collects the installation diagnostics report.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The report. <see cref="TraconDiagnosticsReport.UiEmbedded"/> always
    /// returns <see langword="false"/> because the field belongs to the
    /// <c>Tracon.AspNetCore</c> layer, which <c>Tracon.Core</c> does not
    /// know. The caller must set it with a <c>with</c> expression.
    /// </returns>
    public async ValueTask<TraconDiagnosticsReport> CollectAsync(CancellationToken cancellationToken = default)
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

        return new TraconDiagnosticsReport
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
            AgentSources = [.. _agentSources
                .OrderBy(static source => source.Priority)
                .Select(static source => new AgentSourceDiagnostic
                {
                    Name = source.Name,
                    Priority = source.Priority,
                    Implementation = source.GetType().FullName ?? source.GetType().Name,
                })],
            ExtensionPoints = CollectExtensionPoints(),
            RunRecording = new RunRecordingDiagnostic
            {
                Enabled = _runRecording.Enabled,
                RecordRunInput = _runRecording.RecordRunInput,
                RecordMessageDeltas = _runRecording.RecordMessageDeltas,
                RecordReasoningDeltas = _runRecording.RecordReasoningDeltas,
                RecordToolPayloads = _runRecording.RecordToolPayloads,
                MaxPayloadLength = _runRecording.MaxPayloadLength,
            },
        };
    }

    /// <summary>
    /// Reports the seven embedding points, and for each one whether the bound
    /// implementation is Tracon's built-in default or the host's own.
    /// </summary>
    /// <remarks>
    /// The built-in default types come from <see cref="TraconExtensionPoints"/>,
    /// the same table the required-binding startup gate reads. The report states
    /// the fact and carries no judgement about whether that binding is acceptable;
    /// the gate is where an installation declares what it will accept.
    /// </remarks>
    private IReadOnlyList<ExtensionPointDiagnostic> CollectExtensionPoints()
    {
        var sinks = _runEventSinks.ToList();
        var sinkIsDefault = sinks.Count == 0;
        var sinkImplementation = sinkIsDefault
            ? "(none)"
            : string.Join(", ", sinks.Select(static sink => sink.GetType().Name).Distinct(StringComparer.Ordinal));

        return
        [
            new ExtensionPointDiagnostic
            {
                Contract = nameof(ITenantContext),
                Implementation = _tenantContext.GetType().Name,
                IsBuiltInDefault = _tenantContext.GetType() == TraconExtensionPoints.BuiltInDefaultOf(typeof(ITenantContext)),
            },
            new ExtensionPointDiagnostic
            {
                Contract = nameof(IRunAttributionContext),
                Implementation = _runAttributionContext.GetType().Name,
                IsBuiltInDefault = _runAttributionContext.GetType() == TraconExtensionPoints.BuiltInDefaultOf(typeof(IRunAttributionContext)),
            },
            new ExtensionPointDiagnostic
            {
                Contract = nameof(IToolAuthorizationHandler),
                Implementation = _toolAuthorizationHandler.GetType().Name,
                IsBuiltInDefault = _toolAuthorizationHandler.GetType() == TraconExtensionPoints.BuiltInDefaultOf(typeof(IToolAuthorizationHandler)),
            },
            new ExtensionPointDiagnostic
            {
                Contract = nameof(IRunAuthorizationHandler),
                Implementation = _runAuthorizationHandler.GetType().Name,
                IsBuiltInDefault = _runAuthorizationHandler.GetType() == TraconExtensionPoints.BuiltInDefaultOf(typeof(IRunAuthorizationHandler)),
            },
            new ExtensionPointDiagnostic
            {
                Contract = nameof(IRunEventSink),
                Implementation = sinkImplementation,
                IsBuiltInDefault = sinkIsDefault,
            },
            new ExtensionPointDiagnostic
            {
                Contract = nameof(IAttachmentStorage),
                Implementation = _attachmentStorage?.GetType().Name ?? "(database)",
                IsBuiltInDefault = _attachmentStorage is null,
            },
            new ExtensionPointDiagnostic
            {
                Contract = nameof(IToolApprovalPresenter),
                Implementation = _approvalPresenter.GetType().Name,
                IsBuiltInDefault = _approvalPresenter.GetType() == TraconExtensionPoints.BuiltInDefaultOf(typeof(IToolApprovalPresenter)),
            },
        ];
    }
}
