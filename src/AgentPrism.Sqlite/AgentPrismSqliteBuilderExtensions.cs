using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Extensions that add SQLite persistence to the AgentPrism chain.</summary>
public static class AgentPrismSqliteBuilderExtensions
{
    /// <summary>Enables SQLite persistence by giving a connection string.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="connectionString">The SQLite connection string (example: <c>Data Source=agentprism.db</c>).</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty.</exception>
    public static IAgentPrismBuilder UseSqlite(this IAgentPrismBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseSqlite(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Enables SQLite persistence, reading settings from the <c>AgentPrism:Sqlite</c> section.
    /// </summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Usually
    /// <c>configuration.GetSection(AgentPrismSqliteOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static IAgentPrismBuilder UseSqlite(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseSqlite(options => Bind(configurationSection, options));
    }

    /// <summary>Enables SQLite persistence by giving settings in code.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configure">The settings mutator.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// Stores are registered with <see cref="ServiceCollectionDescriptorExtensions.Replace"/>,
    /// not <c>TryAdd</c>; the rationale is the same as for <c>UseSqlServer()</c>/<c>UsePostgreSql()</c>
    /// (<c>docs/KARARLAR.md</c>, decision K-025).
    /// </remarks>
    public static IAgentPrismBuilder UseSqlite(
        this IAgentPrismBuilder builder,
        Action<AgentPrismSqliteOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AgentPrismSqliteOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AgentPrismSqliteOptions>,
            AgentPrismSqliteOptionsValidator>());

        services.TryAddSingleton(static provider => SqliteDataSourceFactory.Create(
            provider.GetRequiredService<IOptions<AgentPrismSqliteOptions>>().Value));

        // The shared store layer's context. Everything provider-specific is
        // collected here; stores never see the Microsoft.Data.Sqlite type (K-176).
        services.Replace(ServiceDescriptor.Singleton(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<AgentPrismSqliteOptions>>().Value;

            return new SqlStoreContext
            {
                DataSource = provider.GetRequiredService<SqliteDataSource>(),
                Dialect = new SqliteDialect(options.TablePrefix),
                CommandTimeoutSeconds = options.CommandTimeoutSeconds,
                AutoApplyMigrations = options.AutoApplyMigrations,
                ProviderName = "SQLite",
            };
        }));

        // 🚨 If two persistence providers are registered at the same time, the
        // last registration wins. This is a configuration error; a warning is
        // logged at startup and /api/diagnostics reports it. Rationale: K-183.
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQLite"));

        services.Replace(ServiceDescriptor.Singleton(static provider => new MigrationRunner(
            provider.GetRequiredService<SqlStoreContext>(),
            provider.GetRequiredService<ILogger<MigrationRunner>>())));
        services.AddHostedService<MigrationHostedService>();

        // Diagnostics (Phase 33): the winning provider's MigrationRunner is also
        // resolved as ISqlPersistenceDiagnostics; the same instance, no extra
        // SQL connection produced.
        services.Replace(ServiceDescriptor.Singleton<ISqlPersistenceDiagnostics>(
            static provider => provider.GetRequiredService<MigrationRunner>()));

        services.Replace(ServiceDescriptor.Singleton<IAuditLog, SqlAuditLog>());

        services.Replace(ServiceDescriptor.Singleton<IAgentDefinitionStore, AuditingAgentDefinitionStore>(
            static provider => new AuditingAgentDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlAgentDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingAgentDefinitionStore>>())));
        services.Replace(
            ServiceDescriptor.Singleton<IAgentSkillStore, SqlAgentSkillStore>());

        services.Replace(ServiceDescriptor.Singleton<ISkillScriptGrantStore, AuditingSkillScriptGrantStore>(
            static provider => new AuditingSkillScriptGrantStore(
                ActivatorUtilities.CreateInstance<SqlSkillScriptGrantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSkillScriptGrantStore>>())));
        services.Replace(ServiceDescriptor.Singleton<IRunStore, SqlRunStore>());

        services.Replace(ServiceDescriptor.Singleton<IWorkflowDefinitionStore, AuditingWorkflowDefinitionStore>(
            static provider => new AuditingWorkflowDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlWorkflowDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingWorkflowDefinitionStore>>())));
        services.Replace(
            ServiceDescriptor.Singleton<IWorkflowCheckpointStore, SqlWorkflowCheckpointStore>());

        services.Replace(ServiceDescriptor.Singleton<IJobStore, SqlJobStore>());
        services.Replace(ServiceDescriptor.Singleton<IJobScheduleStore, SqlJobScheduleStore>());
        services.Replace(ServiceDescriptor.Singleton<IInboundTriggerStore, SqlInboundTriggerStore>());

        services.Replace(ServiceDescriptor.Singleton<IEvalStore, SqlEvalStore>());

        services.Replace(ServiceDescriptor.Singleton<IQuotaStore, SqlQuotaStore>());
        services.Replace(ServiceDescriptor.Singleton<IWebhookStore, SqlWebhookStore>());

        // Tenant-scoped API keys (Phase 53). Not decorated: same rationale as
        // the quota/webhook stores, admin actions are separately written to the
        // audit log in the HTTP layer.
        services.Replace(ServiceDescriptor.Singleton<IApiKeyStore, SqlApiKeyStore>());

        // Tenant provider bindings (BYOK) and egress policy (Phase 65). Same
        // rationale as the API key store: not wrapped, administrator actions
        // are written to the audit trail separately at the HTTP layer.
        services.Replace(ServiceDescriptor.Singleton<ITenantProviderBindingStore, SqlTenantProviderBindingStore>());
        services.Replace(ServiceDescriptor.Singleton<ITenantEgressPolicyStore, SqlTenantEgressPolicyStore>());

        // Data retention and archiving (Phase 25).
        services.Replace(ServiceDescriptor.Singleton<IRetentionPolicyStore, SqlRetentionPolicyStore>());
        services.Replace(ServiceDescriptor.Singleton<IRetentionStore, SqlRetentionStore>());

        // Data subject export/erasure (Phase 64). Replaces the in-memory
        // NullDataSubjectStore; meaningful only with a SQL provider on.
        services.Replace(ServiceDescriptor.Singleton<IDataSubjectStore, SqlDataSubjectStore>());

        // Single-executor election (Phase 42). Replaces the in-memory
        // InMemorySingletonLeaseStore; lease sharing is only meaningful here in
        // a multi-instance deployment.
        services.Replace(ServiceDescriptor.Singleton<ISingletonLeaseStore, SqlSingletonLeaseStore>());

        // Voice session recording (Phase 29). Only writes anything if
        // UseVoiceConversation() was called; the store stays empty if it was
        // not. NOT decorated with the audit log: recording is a byproduct of
        // execution, not an admin decision.
        services.Replace(ServiceDescriptor.Singleton<IVoiceSessionStore, SqlVoiceSessionStore>());

        // Run/message scores (Phase 31). NOT decorated with the audit log:
        // same rationale as the quota/webhook stores -- a score is not an
        // admin decision, it is user-supplied feedback.
        services.Replace(ServiceDescriptor.Singleton<IRunScoreStore, SqlRunScoreStore>());

        // Idempotency-Key support (Phase 43). Replaces the in-memory
        // InMemoryIdempotencyStore; deduplication is only meaningful here in a
        // multi-instance deployment.
        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore, SqlIdempotencyStore>());

        // Run inputs (Phase 47). Replaces the in-memory InMemoryRunInputStore;
        // replay only keeps working after the process restarts once the input
        // is persisted.
        services.Replace(ServiceDescriptor.Singleton<IRunInputStore, SqlRunInputStore>());

        // Async approval inbox (Phase 55).
        services.Replace(ServiceDescriptor.Singleton<IPendingApprovalStore, SqlPendingApprovalStore>());

        // Conversation branching (Phase 47). There is NO in-memory equivalent:
        // MAF's InMemoryChatHistoryProvider keeps history in an opaque blob of
        // session state and cannot be copied up to a given sequence number.
        // Registration only happens here; a stateless setup returns 501.
        services.TryAddSingleton<IConversationBranchStore, SqlConversationBranchStore>();

        services.Replace(ServiceDescriptor.Singleton<IExperimentStore, AuditingExperimentStore>(
            static provider => new AuditingExperimentStore(
                ActivatorUtilities.CreateInstance<SqlExperimentStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingExperimentStore>>())));

        services.Replace(ServiceDescriptor.Singleton<ISessionStore, AuditingSessionStore>(
            static provider => new AuditingSessionStore(
                ActivatorUtilities.CreateInstance<SqlSessionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSessionStore>>())));
        services.Replace(ServiceDescriptor.Singleton<ITraceStore, SqlTraceStore>());
        services.Replace(ServiceDescriptor.Singleton<IToolApprovalRuleStore, AuditingToolApprovalRuleStore>(
            static provider => new AuditingToolApprovalRuleStore(
                ActivatorUtilities.CreateInstance<SqlToolApprovalRuleStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingToolApprovalRuleStore>>())));
        services.Replace(ServiceDescriptor.Singleton<IMcpServerStore, AuditingMcpServerStore>(
            static provider => new AuditingMcpServerStore(
                ActivatorUtilities.CreateInstance<SqlMcpServerStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingMcpServerStore>>())));
        services.Replace(ServiceDescriptor.Singleton<ITenantStore, AuditingTenantStore>(
            static provider => new AuditingTenantStore(
                ActivatorUtilities.CreateInstance<SqlTenantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingTenantStore>>())));

        services.Replace(ServiceDescriptor.Singleton<ChatHistoryProvider, SqlChatHistoryProvider>());

        services.Replace(ServiceDescriptor.Singleton<IAttachmentStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAttachmentStore>(provider)));

#pragma warning disable MAAI001 // AgentFileStore — rationale same as AgentPrismServiceCollectionExtensions.
        services.Replace(ServiceDescriptor.Singleton<AgentFileStore>(
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
    /// Rationale: <c>docs/KARARLAR.md</c>, decision K-021.
    /// </remarks>
    private static void Bind(IConfiguration section, AgentPrismSqliteOptions options)
    {
        if (section[nameof(AgentPrismSqliteOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(AgentPrismSqliteOptions.TablePrefix)] is { Length: > 0 } tablePrefix)
        {
            options.TablePrefix = tablePrefix;
        }

        if (bool.TryParse(section[nameof(AgentPrismSqliteOptions.AutoApplyMigrations)], out var autoApply))
        {
            options.AutoApplyMigrations = autoApply;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSqliteOptions.CommandTimeoutSeconds)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var commandTimeout))
        {
            options.CommandTimeoutSeconds = commandTimeout;
        }
    }
}
