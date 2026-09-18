using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>Extensions that add SQL Server persistence to the Tracon chain.</summary>
public static class TraconSqlServerBuilderExtensions
{
    /// <summary>Enables SQL Server persistence given a connection string.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="connectionString">The SQL Server connection string.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty.</exception>
    /// <remarks>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .UseSqlServer(builder.Configuration.GetConnectionString("Tracon")!);
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder UseSqlServer(this ITraconBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UseSqlServer(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Enables SQL Server persistence, reading settings from the <c>Tracon:SqlServer</c> section.
    /// </summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Usually
    /// <c>configuration.GetSection(TraconSqlServerOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static ITraconBuilder UseSqlServer(
        this ITraconBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UseSqlServer(options => Bind(configurationSection, options));
    }

    /// <summary>Enables SQL Server persistence with settings given in code.</summary>
    /// <param name="builder">The Tracon chain.</param>
    /// <param name="configure">The settings mutator.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Stores are registered with <see cref="ServiceCollectionDescriptorExtensions.Replace"/>,
    /// not <c>TryAdd</c>. <c>AddTracon()</c> has already registered the
    /// in-memory stores with <c>TryAddSingleton</c> and runs <em>before</em> this
    /// call in the chain; using <c>TryAdd</c> here would silently do nothing.
    /// </para>
    /// <para>
    /// Overwriting is correct here because <c>UseSqlServer()</c> is the
    /// consumer's <strong>explicit</strong> choice. The "register with TryAdd"
    /// rule is for Tracon's defaults, not for explicit calls.
    /// </para>
    /// </remarks>
    public static ITraconBuilder UseSqlServer(
        this ITraconBuilder builder,
        Action<TraconSqlServerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<TraconSqlServerOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<TraconSqlServerOptions>,
            TraconSqlServerOptionsValidator>());

        // The context for the shared store layer. Everything provider-specific
        // is collected here; the stores never see an Npgsql type (Phase 23, K-176).
        // Same rule as the store registrations: the last call wins.
        //
        // Phase 110: the data source is resolved HERE, not registered as its own
        // public DI service — see the matching comment in
        // TraconPostgreSqlBuilderExtensions.UsePostgreSql for the rationale.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<TraconSqlServerOptions>>().Value;
            var contentProtector = provider.GetRequiredService<IContentProtector>();
            var contentProtectionOptions = provider.GetRequiredService<IOptions<TraconContentProtectionOptions>>().Value;
            var (dataSource, ownsDataSource) = SqlServerDataSourceFactory.Resolve(options);

            return new SqlStoreContext
            {
                DataSource = dataSource,
                OwnsDataSource = ownsDataSource,
                Dialect = new SqlServerDialect(options.SchemaName),
                CommandTimeoutSeconds = options.CommandTimeoutSeconds,
                AutoApplyMigrations = options.AutoApplyMigrations,
                // Phase 111: the "views" set publishes runs_v1 and is opt-in --
                // a published view is a permanent data contract (K1).
                EnabledMigrationSets = options.EnableReadViews
                    ? new HashSet<string>(StringComparer.Ordinal) { "views" }
                    : System.Collections.Immutable.ImmutableHashSet<string>.Empty,
                ProviderName = "SQL Server",
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
        services.AddSingleton(new SqlPersistenceRegistrationMarker("SQL Server"));

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton(static provider => new MigrationRunner(
            provider.GetRequiredService<SqlStoreContext>(),
            provider.GetRequiredService<ILogger<MigrationRunner>>())));
        services.AddHostedService<MigrationHostedService>();

        // Diagnostics (Phase 33): the winning provider's MigrationRunner is also
        // resolved as ISqlPersistenceDiagnostics; the same instance produces no
        // extra SQL connection.
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

        // The audit trail ledger also replaces the in-memory one.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IAuditLog, SqlAuditLog>());

        // Replaces the in-memory stores. TryAdd would not work here.
        //
        // All five write-performing stores are registered wrapped in Phase 9's
        // audit-trail decorators, so the audit trail behaves the same way
        // whether the store is in-memory or SQL Server. Rationale: same pattern
        // as the AddTracon() registration in Tracon.Core
        // (docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md).
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

        // Script run grants are also wrapped in the audit-trail decorator:
        // granting permission means granting the right to run code on the server.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISkillScriptGrantStore, AuditingSkillScriptGrantStore>(
            static provider => new AuditingSkillScriptGrantStore(
                ActivatorUtilities.CreateInstance<SqlSkillScriptGrantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSkillScriptGrantStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunStore, SqlRunStore>());

        // Workflow definitions and checkpoints (Phase 15). The definition store
        // is wrapped in the audit-trail decorator; the checkpoint store is not:
        // a checkpoint is not a user decision, it is a byproduct of execution
        // and is written on every super-step — it would flood the audit trail.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IWorkflowDefinitionStore, AuditingWorkflowDefinitionStore>(
            static provider => new AuditingWorkflowDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlWorkflowDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingWorkflowDefinitionStore>>(),
                provider.GetRequiredService<TraconMetrics>())));
        services.ReplaceTraconDefault(
            ServiceDescriptor.Singleton<IWorkflowCheckpointStore, SqlWorkflowCheckpointStore>());

        // Job queue and schedule stores (Phase 17). Neither is wrapped: the
        // queue carries its own state machine (Pending/Leased/Running/...),
        // same rationale as the workflow checkpoint store.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IJobStore, SqlJobStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IJobScheduleStore, SqlJobScheduleStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IInboundTriggerStore, SqlInboundTriggerStore>());

        // Eval suite/case/run store (Phase 18). Not wrapped: same rationale as
        // the job queue stores, it carries its own state machine.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IEvalStore, SqlEvalStore>());

        // Quota and webhook stores (Phase 21).
        //
        // 🚨 In a multi-instance deployment this store is REQUIRED for quotas:
        // the in-memory counter is separate per process, and the quota ends up
        // divided by the instance count.
        //
        // Neither is wrapped in the audit-trail decorator. The rationale
        // differs: quota rules and subscriptions are administrator decisions
        // (they deserve wrapping), but the consumption counter and delivery
        // history are byproducts of execution and are written on every run —
        // it would flood the audit trail with noise. Since both live in the
        // same contract, wrapping is "all or nothing"; administrator actions
        // are written to the audit trail separately at the HTTP layer.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IQuotaStore, SqlQuotaStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IWebhookStore, SqlWebhookStore>());

        // Tenant-scoped API keys (Phase 53). Not wrapped: same rationale as the
        // quota/webhook stores, administrator actions are written to the audit
        // trail separately at the HTTP layer.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IApiKeyStore, SqlApiKeyStore>());

        // Tenant provider bindings (BYOK) and egress policy (Phase 65). Same
        // rationale as the API key store: not wrapped, administrator actions
        // are written to the audit trail separately at the HTTP layer.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantProviderBindingStore, SqlTenantProviderBindingStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantEgressPolicyStore, SqlTenantEgressPolicyStore>());

        // Retention and archival (Phase 25). Same rationale: the policy/run
        // store is not wrapped, the data plane is meaningful only while a SQL
        // provider is enabled.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRetentionPolicyStore, SqlRetentionPolicyStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRetentionStore, SqlRetentionStore>());

        // Data subject export/erasure (Phase 64). Replaces the in-memory
        // NullDataSubjectStore; meaningful only with a SQL provider on.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IDataSubjectStore, SqlDataSubjectStore>());

        // Single-executor election (Phase 42). Replaces the in-memory
        // InMemorySingletonLeaseStore; lease sharing is only meaningful here in
        // a multi-instance deployment.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISingletonLeaseStore, SqlSingletonLeaseStore>());

        // Call recording (Phase 29). Writes only if UseVoiceConversation() was
        // called; the store stays empty otherwise. NOT wrapped in the
        // audit-trail decorator: the record is not an administrator decision,
        // it is a byproduct of execution.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IVoiceSessionStore, SqlVoiceSessionStore>());

        // Run/message scores (Phase 31). NOT wrapped in the audit-trail
        // decorator: same rationale as the quota/webhook stores — a score is
        // not an administrator decision, it is feedback coming from a user.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunScoreStore, SqlRunScoreStore>());

        // Idempotency-Key support (Phase 43). Replaces the in-memory
        // InMemoryIdempotencyStore; deduplication is only meaningful here in a
        // multi-instance deployment.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IIdempotencyStore, SqlIdempotencyStore>());

        // Run inputs (Phase 47). Replaces the in-memory InMemoryRunInputStore;
        // replay keeps working after the process restarts only once the input
        // is persisted.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunInputStore, SqlRunInputStore>());

        // Asynchronous approval inbox (Phase 55).
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IPendingApprovalStore, SqlPendingApprovalStore>());

        // Conversation branching (Phase 47). There is NO in-memory
        // counterpart: MAF's InMemoryChatHistoryProvider keeps history in an
        // opaque blob of session state and cannot be copied up to a given
        // sequence number. Recording only happens here; without a registered
        // store the endpoint returns 501.
        services.TryAddSingleton<IConversationBranchStore, SqlConversationBranchStore>();

        // A/B experiments (Phase 19). Wrapped in the audit-trail decorator for
        // the same rationale as IAgentDefinitionStore: an admin's deliberate
        // decision, not a byproduct of execution.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IExperimentStore, AuditingExperimentStore>(
            static provider => new AuditingExperimentStore(
                ActivatorUtilities.CreateInstance<SqlExperimentStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingExperimentStore>>(),
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

        // Chat history. AgentDefinitionCompiler wires this into every agent it
        // compiles; if not registered, MAF falls back to its in-memory default.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ChatHistoryProvider, SqlChatHistoryProvider>());

        // Attachments. When IAttachmentStorage is registered (S3/Blob), the
        // content lives there; this store only holds the metadata.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IAttachmentStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAttachmentStore>(provider)));

        // Persistent agent file memory: FileMemoryProvider and TextSearchProvider
        // resolve here without code changes (K-110).
#pragma warning disable MAAI001 // AgentFileStore — same rationale as TraconServiceCollectionExtensions.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<AgentFileStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAgentFileStore>(provider)));
#pragma warning restore MAAI001

        return builder;
    }

    /// <summary>
    /// Binds the configuration section to the settings object by hand.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// This method must also be updated when a new setting is added.
    /// </remarks>
    private static void Bind(IConfiguration section, TraconSqlServerOptions options)
    {
        if (section[nameof(TraconSqlServerOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(TraconSqlServerOptions.SchemaName)] is { Length: > 0 } schemaName)
        {
            options.SchemaName = schemaName;
        }

        if (bool.TryParse(section[nameof(TraconSqlServerOptions.AutoApplyMigrations)], out var autoApply))
        {
            options.AutoApplyMigrations = autoApply;
        }

        if (int.TryParse(
                section[nameof(TraconSqlServerOptions.CommandTimeoutSeconds)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var commandTimeout))
        {
            options.CommandTimeoutSeconds = commandTimeout;
        }

        if (bool.TryParse(section[nameof(TraconSqlServerOptions.EnableReadViews)], out var enableReadViews))
        {
            options.EnableReadViews = enableReadViews;
        }
    }
}
