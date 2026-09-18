using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add SQLite persistence to the Tracon chain.</summary>
public static class TraconSqliteBuilderExtensions
{
    /// <summary>Enables SQLite persistence by giving a connection string.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="connectionString">The SQLite connection string (example: <c>Data Source=tracon.db</c>).</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty.</exception>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseSqlite("Data Source=tracon.db");
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseSqlite(this ITraconBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseSqlite(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Enables SQLite persistence, reading settings from the <c>Tracon:Sqlite</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Usually
    /// <c>configuration.GetSection(TraconSqliteOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseSqlite(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseSqlite(options => Bind(configurationSection, options));
    }

    /// <summary>Enables SQLite persistence by giving settings in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings mutator.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// Stores are registered with <see cref="ServiceCollectionDescriptorExtensions.Replace"/>,
    /// not <c>TryAdd</c>; the rationale is the same as for <c>UseSqlServer()</c>/<c>UsePostgreSql()</c>
    /// </remarks>
    public static ITraconBuilder UseSqlite(
        this ITraconBuilder builder,
        Action<TraconSqliteOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<TraconSqliteOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TraconSqliteOptions>,
            TraconSqliteOptionsValidator>());

        // The shared store layer's context. Everything provider-specific is
        // collected here; stores never see the Microsoft.Data.Sqlite type (K-176).
        //
        // Phase 110: the data source is resolved HERE, not registered as its own
        // public DI service — see the matching comment in
        // TraconPostgreSqlBuilderExtensions.UsePostgreSql for the rationale.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<TraconSqliteOptions>>().Value;
            var contentProtector = provider.GetRequiredService<IContentProtector>();
            var contentProtectionOptions = provider.GetRequiredService<IOptions<TraconContentProtectionOptions>>().Value;
            var (dataSource, ownsDataSource) = SqliteDataSourceFactory.Resolve(options);

            return new SqlStoreContext
            {
                DataSource = dataSource,
                OwnsDataSource = ownsDataSource,
                Dialect = new SqliteDialect(options.TablePrefix),
                CommandTimeoutSeconds = options.CommandTimeoutSeconds,
                AutoApplyMigrations = options.AutoApplyMigrations,
                // Phase 111: the "views" set publishes runs_v1 and is opt-in --
                // a published view is a permanent data contract (K1).
                EnabledMigrationSets = options.EnableReadViews
                    ? new HashSet<string>(StringComparer.Ordinal) { "views" }
                    : System.Collections.Immutable.ImmutableHashSet<string>.Empty,
                ProviderName = "SQLite",
                ContentProtector = contentProtector,
                // Phase 82: empty unless the protector is actually enabled -
                // K1 (identical behavior with the feature off) does not depend
                // on what TraconContentProtectionOptions.Columns holds.
                ProtectedColumns = contentProtector.IsEnabled
                    ? (IReadOnlySet<ProtectedColumn>)contentProtectionOptions.Columns
                    : System.Collections.Immutable.ImmutableHashSet<ProtectedColumn>.Empty,
            };
        }));

        // 🚨 If two persistence providers are registered at the same time, the
        // last registration wins. This is a configuration error; a warning is
        // logged at startup and /api/diagnostics reports it. Rationale: K-183.
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton(static provider => new MigrationRunner(
            provider.GetRequiredService<SqlStoreContext>(),
            provider.GetRequiredService<ILogger<MigrationRunner>>())));
        services.AddHostedService<MigrationHostedService>();

        // Diagnostics (Phase 33): the winning provider's MigrationRunner is also
        // resolved as ISqlPersistenceDiagnostics; the same instance, no extra
        // SQL connection produced.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISqlPersistenceDiagnostics>(
            static provider => provider.GetRequiredService<MigrationRunner>()));

        // Same reasoning, for the `tracon migrate` CLI command (Phase 83,
        // section 83.5): MigrationRunner is linked-source, so a consumer that
        // references more than one provider sees ambiguous types with the
        // same name (CS0433). IMigrationApplier is the resolvable seam.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IMigrationApplier>(
            static provider => provider.GetRequiredService<MigrationRunner>()));

        // State preflight (Phase 156): read-only, tenant-agnostic counting of
        // the stored schema generations. A separate seam from ISessionStore on
        // purpose - that one always filters by tenant, pages, and pulls the
        // whole state payload, none of which a whole-database preflight wants.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IStatePreflightReader>(
            static provider => new SqlStatePreflightReader(provider.GetRequiredService<SqlStoreContext>())));

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IAuditLog, SqlAuditLog>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IAgentDefinitionStore, AuditingAgentDefinitionStore>(
            static provider => new AuditingAgentDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlAgentDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingAgentDefinitionStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IAgentSkillStore, AuditingAgentSkillStore>(
            static provider => new AuditingAgentSkillStore(
                ActivatorUtilities.CreateInstance<SqlAgentSkillStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingAgentSkillStore>>(),
                provider.GetRequiredService<TraconMetrics>())));

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISkillScriptGrantStore, AuditingSkillScriptGrantStore>(
            static provider => new AuditingSkillScriptGrantStore(
                ActivatorUtilities.CreateInstance<SqlSkillScriptGrantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSkillScriptGrantStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunStore, SqlRunStore>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IWorkflowDefinitionStore, AuditingWorkflowDefinitionStore>(
            static provider => new AuditingWorkflowDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlWorkflowDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingWorkflowDefinitionStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(
            ServiceDescriptor.Singleton<IWorkflowCheckpointStore, SqlWorkflowCheckpointStore>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IJobStore, SqlJobStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IJobScheduleStore, SqlJobScheduleStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IInboundTriggerStore, SqlInboundTriggerStore>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IEvalStore, SqlEvalStore>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IQuotaStore, SqlQuotaStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IWebhookStore, SqlWebhookStore>());

        // Tenant-scoped API keys (Phase 53). Not decorated: same rationale as
        // the quota/webhook stores, admin actions are separately written to the
        // audit log in the HTTP layer.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IApiKeyStore, SqlApiKeyStore>());

        // Tenant provider bindings (BYOK) and egress policy (Phase 65). Same
        // rationale as the API key store: not wrapped, administrator actions
        // are written to the audit trail separately at the HTTP layer.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantProviderBindingStore, SqlTenantProviderBindingStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantEgressPolicyStore, SqlTenantEgressPolicyStore>());

        // Data retention and archiving (Phase 25).
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRetentionPolicyStore, SqlRetentionPolicyStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRetentionStore, SqlRetentionStore>());

        // Data subject export/erasure (Phase 64). Replaces the in-memory
        // NullDataSubjectStore; meaningful only with a SQL provider on.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IDataSubjectStore, SqlDataSubjectStore>());

        // Single-executor election (Phase 42). Replaces the in-memory
        // InMemorySingletonLeaseStore; lease sharing is only meaningful here in
        // a multi-instance deployment.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISingletonLeaseStore, SqlSingletonLeaseStore>());

        // Voice session recording (Phase 29). Only writes anything if
        // UseVoiceConversation() was called; the store stays empty if it was
        // not. NOT decorated with the audit log: recording is a byproduct of
        // execution, not an admin decision.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IVoiceSessionStore, SqlVoiceSessionStore>());

        // Run/message scores (Phase 31). NOT decorated with the audit log:
        // same rationale as the quota/webhook stores -- a score is not an
        // admin decision, it is user-supplied feedback.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunScoreStore, SqlRunScoreStore>());

        // Idempotency-Key support (Phase 43). Replaces the in-memory
        // InMemoryIdempotencyStore; deduplication is only meaningful here in a
        // multi-instance deployment.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IIdempotencyStore, SqlIdempotencyStore>());

        // Run inputs (Phase 47). Replaces the in-memory InMemoryRunInputStore;
        // replay only keeps working after the process restarts once the input
        // is persisted.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunInputStore, SqlRunInputStore>());

        // Async approval inbox (Phase 55).
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IPendingApprovalStore, SqlPendingApprovalStore>());

        // Conversation branching (Phase 47). There is NO in-memory equivalent:
        // MAF's InMemoryChatHistoryProvider keeps history in an opaque blob of
        // session state and cannot be copied up to a given sequence number.
        // Registration only happens here; a stateless setup returns 501.
        services.TryAddSingleton<IConversationBranchStore, SqlConversationBranchStore>();

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IExperimentStore, AuditingExperimentStore>(
            static provider => new AuditingExperimentStore(
                ActivatorUtilities.CreateInstance<SqlExperimentStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingExperimentStore>>(),
                provider.GetRequiredService<TraconMetrics>())));

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISessionStore, AuditingSessionStore>(
            static provider => new AuditingSessionStore(
                ActivatorUtilities.CreateInstance<SqlSessionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSessionStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITraceStore, SqlTraceStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IToolApprovalRuleStore, AuditingToolApprovalRuleStore>(
            static provider => new AuditingToolApprovalRuleStore(
                ActivatorUtilities.CreateInstance<SqlToolApprovalRuleStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingToolApprovalRuleStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IMcpServerStore, AuditingMcpServerStore>(
            static provider => new AuditingMcpServerStore(
                ActivatorUtilities.CreateInstance<SqlMcpServerStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingMcpServerStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantStore, AuditingTenantStore>(
            static provider => new AuditingTenantStore(
                ActivatorUtilities.CreateInstance<SqlTenantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingTenantStore>>(),
                provider.GetRequiredService<TraconMetrics>())));

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ChatHistoryProvider, SqlChatHistoryProvider>());

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IAttachmentStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAttachmentStore>(provider)));

#pragma warning disable MAAI001 // AgentFileStore — rationale same as TraconServiceCollectionExtensions.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<AgentFileStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAgentFileStore>(provider)));
#pragma warning restore MAAI001

        return builder;
    }

    /// <summary>
    /// Manually binds the configuration section into the settings object.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// This method must also be updated when a new setting is added.
    /// </remarks>
    private static void Bind(IConfiguration section, TraconSqliteOptions options)
    {
        if (section[nameof(TraconSqliteOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(TraconSqliteOptions.TablePrefix)] is { Length: > 0 } tablePrefix)
        {
            options.TablePrefix = tablePrefix;
        }

        if (bool.TryParse(section[nameof(TraconSqliteOptions.AutoApplyMigrations)], out var autoApply))
        {
            options.AutoApplyMigrations = autoApply;
        }

        if (int.TryParse(
                section[nameof(TraconSqliteOptions.CommandTimeoutSeconds)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var commandTimeout))
        {
            options.CommandTimeoutSeconds = commandTimeout;
        }

        if (bool.TryParse(section[nameof(TraconSqliteOptions.EnableReadViews)], out var enableReadViews))
        {
            options.EnableReadViews = enableReadViews;
        }
    }
}
