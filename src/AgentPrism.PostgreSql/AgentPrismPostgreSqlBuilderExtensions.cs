using System.Globalization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AgentPrism;

/// <summary>Extensions that add PostgreSQL persistence to the AgentPrism chain.</summary>
public static class AgentPrismPostgreSqlBuilderExtensions
{
    /// <summary>Enables PostgreSQL persistence given a connection string.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty.</exception>
    public static IAgentPrismBuilder UsePostgreSql(this IAgentPrismBuilder builder, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return builder.UsePostgreSql(options => options.ConnectionString = connectionString);
    }

    /// <summary>
    /// Enables PostgreSQL persistence, reading settings from the <c>AgentPrism:PostgreSql</c> section.
    /// </summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configurationSection">
    /// The section settings are read from. Usually
    /// <c>configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName)</c>.
    /// </param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    public static IAgentPrismBuilder UsePostgreSql(
        this IAgentPrismBuilder builder,
        IConfiguration configurationSection)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configurationSection);

        return builder.UsePostgreSql(options => Bind(configurationSection, options));
    }

    /// <summary>Enables PostgreSQL persistence with settings given in code.</summary>
    /// <param name="builder">The AgentPrism chain.</param>
    /// <param name="configure">The settings mutator.</param>
    /// <returns>The continuation of the chain.</returns>
    /// <exception cref="ArgumentNullException">One of the parameters is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Stores are registered with <see cref="ServiceCollectionDescriptorExtensions.Replace"/>,
    /// not <c>TryAdd</c>. Reason: <c>AddAgentPrism()</c> has already registered the
    /// in-memory stores with <c>TryAddSingleton</c> and runs <em>before</em> this
    /// call in the chain; using <c>TryAdd</c> here would silently do nothing.
    /// </para>
    /// <para>
    /// Overwriting is correct here because <c>UsePostgreSql()</c> is the
    /// consumer's <strong>explicit</strong> choice. The "register with TryAdd"
    /// rule is for AgentPrism's defaults, not for explicit calls.
    /// Rationale: <c>docs/KARARLAR.md</c>, decision K-025.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder UsePostgreSql(
        this IAgentPrismBuilder builder,
        Action<AgentPrismPostgreSqlOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var services = builder.Services;

        services.AddOptions<AgentPrismPostgreSqlOptions>().ValidateOnStart();
        services.Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<
            IValidateOptions<AgentPrismPostgreSqlOptions>,
            AgentPrismPostgreSqlOptionsValidator>());

        // A single data source; the Npgsql pool manages itself.
        services.TryAddSingleton(static provider => NpgsqlDataSourceFactory.Create(
            provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value,
            provider.GetService<ILoggerFactory>()));

        // The context for the shared store layer. Everything provider-specific
        // is collected here; the stores never see an Npgsql type (Phase 23, K-176).
        // Same rule as the store registrations: the last call wins. If this were
        // TryAdd, registering a second provider would leave the stores pointing
        // at the new provider while the context still pointed at the old one,
        // and the two would silently diverge.
        services.Replace(ServiceDescriptor.Singleton(static provider =>
        {
            var options = provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value;
            var knowledgeOptions = provider.GetRequiredService<IOptions<AgentPrismKnowledgeOptions>>().Value;

            return new SqlStoreContext
            {
                DataSource = provider.GetRequiredService<NpgsqlDataSource>(),
                Dialect = new PostgresDialect(options.SchemaName),
                CommandTimeoutSeconds = options.CommandTimeoutSeconds,
                AutoApplyMigrations = options.AutoApplyMigrations,
                ProviderName = "PostgreSQL",
                // Phase 51: the {dimension} placeholder of migration 0024. Kept
                // SEPARATE from the fixed schema placeholder (schema) because it
                // comes from AgentPrismKnowledgeOptions at startup, not headed by
                // the provider.
                MigrationTemplateValues = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dimension"] = knowledgeOptions.Dimensions.ToString(CultureInfo.InvariantCulture),
                },
            };
        }));

        // The marker accumulates (not TryAdd): if more than one provider is
        // registered, MigrationHostedService warns at startup and
        // /api/diagnostics reports it. Rationale: K-183.
        services.AddSingleton(new SqlPersistenceRegistrationMarker("PostgreSQL"));

        services.Replace(ServiceDescriptor.Singleton(static provider => new MigrationRunner(
            provider.GetRequiredService<SqlStoreContext>(),
            provider.GetRequiredService<ILogger<MigrationRunner>>())));
        services.AddHostedService<MigrationHostedService>();

        // Diagnostics (Phase 33): the winning provider's MigrationRunner is also
        // resolved as ISqlPersistenceDiagnostics; the same instance produces no
        // extra SQL connection.
        services.Replace(ServiceDescriptor.Singleton<ISqlPersistenceDiagnostics>(
            static provider => provider.GetRequiredService<MigrationRunner>()));

        // The audit trail ledger also replaces the in-memory one.
        services.Replace(ServiceDescriptor.Singleton<IAuditLog, SqlAuditLog>());

        // Replaces the in-memory stores. TryAdd would not work here.
        //
        // All five write-performing stores are registered wrapped in Phase 9's
        // audit-trail decorators, so the audit trail behaves the same way
        // whether the store is in-memory or PostgreSQL. Rationale: same pattern
        // as the AddAgentPrism() registration in AgentPrism.Core
        // (docs/09-YONETISIM-VE-DENETIM-IZI.md).
        services.Replace(ServiceDescriptor.Singleton<IAgentDefinitionStore, AuditingAgentDefinitionStore>(
            static provider => new AuditingAgentDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlAgentDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingAgentDefinitionStore>>())));
        services.Replace(
            ServiceDescriptor.Singleton<IAgentSkillStore, SqlAgentSkillStore>());

        // Script run grants are also wrapped in the audit-trail decorator:
        // granting permission means granting the right to run code on the server.
        services.Replace(ServiceDescriptor.Singleton<ISkillScriptGrantStore, AuditingSkillScriptGrantStore>(
            static provider => new AuditingSkillScriptGrantStore(
                ActivatorUtilities.CreateInstance<SqlSkillScriptGrantStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingSkillScriptGrantStore>>())));
        services.Replace(ServiceDescriptor.Singleton<IRunStore, SqlRunStore>());

        // Workflow definitions and checkpoints (Phase 15). The definition store
        // is wrapped in the audit-trail decorator; the checkpoint store is not:
        // a checkpoint is not a user decision, it is a byproduct of execution
        // and is written on every super-step — it would flood the audit trail.
        services.Replace(ServiceDescriptor.Singleton<IWorkflowDefinitionStore, AuditingWorkflowDefinitionStore>(
            static provider => new AuditingWorkflowDefinitionStore(
                ActivatorUtilities.CreateInstance<SqlWorkflowDefinitionStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<ILogger<AuditingWorkflowDefinitionStore>>())));
        services.Replace(
            ServiceDescriptor.Singleton<IWorkflowCheckpointStore, SqlWorkflowCheckpointStore>());

        // Job queue and schedule stores (Phase 17). Neither is wrapped: the
        // queue carries its own state machine (Pending/Leased/Running/...),
        // same rationale as the workflow checkpoint store.
        services.Replace(ServiceDescriptor.Singleton<IJobStore, SqlJobStore>());
        services.Replace(ServiceDescriptor.Singleton<IJobScheduleStore, SqlJobScheduleStore>());

        // Eval suite/case/run store (Phase 18). Not wrapped: same rationale as
        // the job queue stores, it carries its own state machine.
        services.Replace(ServiceDescriptor.Singleton<IEvalStore, SqlEvalStore>());

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
        services.Replace(ServiceDescriptor.Singleton<IQuotaStore, SqlQuotaStore>());
        services.Replace(ServiceDescriptor.Singleton<IWebhookStore, SqlWebhookStore>());

        // Tenant-scoped API keys (Phase 53). Not wrapped: same rationale as the
        // quota/webhook stores, administrator actions are written to the audit
        // trail separately at the HTTP layer.
        services.Replace(ServiceDescriptor.Singleton<IApiKeyStore, SqlApiKeyStore>());

        // Retention and archival (Phase 25). The policy/run store (control
        // plane) is NOT wrapped in the audit-trail decorator: same rationale as
        // the webhook/quota stores (administrator actions are written
        // separately at the HTTP layer). The data plane (IRetentionStore) is
        // meaningful only while a SQL provider is enabled; it replaces the
        // in-memory NullRetentionStore here.
        services.Replace(ServiceDescriptor.Singleton<IRetentionPolicyStore, SqlRetentionPolicyStore>());
        services.Replace(ServiceDescriptor.Singleton<IRetentionStore, SqlRetentionStore>());

        // Data subject export/erasure (Phase 64). Replaces the in-memory
        // NullDataSubjectStore; meaningful only with a SQL provider on.
        services.Replace(ServiceDescriptor.Singleton<IDataSubjectStore, SqlDataSubjectStore>());

        // Single-executor election (Phase 42). Replaces the in-memory
        // InMemorySingletonLeaseStore; lease sharing is only meaningful here in
        // a multi-instance deployment.
        services.Replace(ServiceDescriptor.Singleton<ISingletonLeaseStore, SqlSingletonLeaseStore>());

        // Call recording (Phase 29). Writes only if UseVoiceConversation() was
        // called; the store stays empty otherwise. NOT wrapped in the
        // audit-trail decorator: the record is not an administrator decision,
        // it is a byproduct of execution.
        services.Replace(ServiceDescriptor.Singleton<IVoiceSessionStore, SqlVoiceSessionStore>());

        // Run/message scores (Phase 31). NOT wrapped in the audit-trail
        // decorator: same rationale as the quota/webhook stores — a score is
        // not an administrator decision, it is feedback coming from a user.
        services.Replace(ServiceDescriptor.Singleton<IRunScoreStore, SqlRunScoreStore>());

        // Idempotency-Key support (Phase 43). Replaces the in-memory
        // InMemoryIdempotencyStore; deduplication is only meaningful here in a
        // multi-instance deployment.
        services.Replace(ServiceDescriptor.Singleton<IIdempotencyStore, SqlIdempotencyStore>());

        // Run inputs (Phase 47). Replaces the in-memory InMemoryRunInputStore;
        // replay keeps working after the process restarts only once the input
        // is persisted.
        services.Replace(ServiceDescriptor.Singleton<IRunInputStore, SqlRunInputStore>());

        // Asynchronous approval inbox (Phase 55).
        services.Replace(ServiceDescriptor.Singleton<IPendingApprovalStore, SqlPendingApprovalStore>());

        // Conversation branching (Phase 47). There is NO in-memory
        // counterpart: MAF's InMemoryChatHistoryProvider keeps history in an
        // opaque blob of session state and cannot be copied up to a given
        // sequence number. Recording only happens here; without a registered
        // store the endpoint returns 501.
        services.TryAddSingleton<IConversationBranchStore, SqlConversationBranchStore>();

        // A/B experiments (Phase 19). Wrapped in the audit-trail decorator for
        // the same rationale as IAgentDefinitionStore: an admin's deliberate
        // decision, not a byproduct of execution.
        services.Replace(ServiceDescriptor.Singleton<IExperimentStore, AuditingExperimentStore>(
            static provider => new AuditingExperimentStore(
                ActivatorUtilities.CreateInstance<SqlExperimentStore>(provider),
                provider.GetRequiredService<IAuditLog>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IAuditActorResolver>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingExperimentStore>>())));

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

        // Chat history. AgentDefinitionCompiler wires this into every agent it
        // compiles; if not registered, MAF falls back to its in-memory default.
        services.Replace(ServiceDescriptor.Singleton<ChatHistoryProvider, SqlChatHistoryProvider>());

        // Attachments. When IAttachmentStorage is registered (S3/Blob), the
        // content lives there; this store only holds the metadata.
        services.Replace(ServiceDescriptor.Singleton<IAttachmentStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAttachmentStore>(provider)));

        // Persistent agent file memory: FileMemoryProvider and TextSearchProvider
        // resolve here without code changes (K-110).
#pragma warning disable MAAI001 // AgentFileStore — same rationale as AgentPrismServiceCollectionExtensions.
        services.Replace(ServiceDescriptor.Singleton<AgentFileStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAgentFileStore>(provider)));
#pragma warning restore MAAI001

        // Vector-based semantic search (Phase 51). K4: the ONLY concrete
        // implementation. TryAdd: if a consumer has already registered their
        // own IVectorSearchStore (for SQL Server/SQLite), theirs wins.
        services.TryAddSingleton<IVectorSearchStore>(static provider => new PgVectorSearchStore(
            provider.GetRequiredService<NpgsqlDataSource>(),
            provider.GetRequiredService<IOptions<AgentPrismPostgreSqlOptions>>().Value,
            provider.GetRequiredService<IOptions<AgentPrismKnowledgeOptions>>().Value));

        return builder;
    }

    /// <summary>
    /// Binds the configuration section to the settings object by hand.
    /// </summary>
    /// <remarks>
    /// <c>Bind()</c> relies on reflection and produces <c>IL2026</c> + <c>IL3050</c>.
    /// This method must also be updated when a new setting is added.
    /// Rationale: <c>docs/KARARLAR.md</c>, decision K-021.
    /// </remarks>
    private static void Bind(IConfiguration section, AgentPrismPostgreSqlOptions options)
    {
        if (section[nameof(AgentPrismPostgreSqlOptions.ConnectionString)] is { Length: > 0 } connectionString)
        {
            options.ConnectionString = connectionString;
        }

        if (section[nameof(AgentPrismPostgreSqlOptions.SchemaName)] is { Length: > 0 } schemaName)
        {
            options.SchemaName = schemaName;
        }

        if (bool.TryParse(section[nameof(AgentPrismPostgreSqlOptions.AutoApplyMigrations)], out var autoApply))
        {
            options.AutoApplyMigrations = autoApply;
        }

        if (int.TryParse(
                section[nameof(AgentPrismPostgreSqlOptions.CommandTimeoutSeconds)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var commandTimeout))
        {
            options.CommandTimeoutSeconds = commandTimeout;
        }
    }
}
