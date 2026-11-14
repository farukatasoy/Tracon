using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Registers everything a SQL persistence provider adds to the service collection
/// that does not depend on which database it is.
/// </summary>
/// <remarks>
/// <para>
/// The store set lives here once, so a new store is added in one place and every
/// provider picks it up. <c>UsePostgreSql</c>, <c>UseSqlServer</c> and
/// <c>UseSqlite</c> name only what differs between databases: their options, their
/// <see cref="SqlStoreContext"/> and the way their state is shared between processes.
/// </para>
/// <para>
/// This file is compiled into each provider assembly as shared source, so the
/// type exists once per assembly and stays <see langword="internal"/>. It must not
/// reference a database driver namespace: anything specific to a driver comes in
/// through <see cref="SqlStoreContext"/>.
/// </para>
/// <para>
/// Every store goes through <c>ReplaceTraconDefault</c>, not <c>TryAdd</c>:
/// <c>AddTracon()</c> has already registered the in-memory default, so a
/// <c>TryAdd</c> would silently do nothing, while a plain <c>Replace</c> would
/// overwrite a store the consuming application registered itself.
/// </para>
/// </remarks>
internal static class SqlPersistenceRegistration
{
    /// <summary>
    /// Registers the store context, every provider-independent store and service,
    /// and the migration startup of a SQL persistence provider.
    /// </summary>
    /// <param name="services">The service collection the Tracon chain builds.</param>
    /// <param name="providerName">
    /// The provider's display name (<c>"PostgreSQL"</c>), reported when more than one
    /// persistence provider is registered.
    /// </param>
    /// <param name="storeContextFactory">
    /// Builds the <see cref="SqlStoreContext"/>: the data source, the dialect and the
    /// values derived from the provider's settings.
    /// </param>
    /// <param name="stateSharing">
    /// The descriptor of the <see cref="StateSharing"/> value that says whether the
    /// provider's state is shared between processes. It replaces the in-process
    /// default Core registers.
    /// </param>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="providerName"/> is empty.</exception>
    public static void AddSqlPersistence(
        this IServiceCollection services,
        string providerName,
        Func<IServiceProvider, SqlStoreContext> storeContextFactory,
        ServiceDescriptor stateSharing)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(storeContextFactory);
        ArgumentNullException.ThrowIfNull(stateSharing);

        // The context for the shared store layer. Same rule as the store
        // registrations: the last call wins. If this were TryAdd, registering a
        // second provider would leave the stores pointing at the new provider while
        // the context still pointed at the old one, and the two would silently diverge.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton(storeContextFactory));

        // The marker accumulates (not TryAdd): if more than one provider is
        // registered, MigrationHostedService warns at startup and
        // /api/diagnostics reports it. Two persistence providers in one chain are a
        // configuration error; the last registration wins.
        services.AddSingleton(new SqlPersistenceRegistrationMarker(providerName));

        services.ReplaceTraconDefault(ServiceDescriptor.Singleton(static provider => new MigrationRunner(
            provider.GetRequiredService<SqlStoreContext>(),
            provider.GetRequiredService<ILogger<MigrationRunner>>())));
        services.AddHostedService<MigrationHostedService>();

        // Diagnostics: the winning provider's MigrationRunner is also
        // resolved as ISqlPersistenceDiagnostics; the same instance produces no
        // extra SQL connection.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISqlPersistenceDiagnostics>(
            static provider => provider.GetRequiredService<MigrationRunner>()));

        // The host answers a 400 instead of a 503 when the store rejected a value
        // (a text over its column, a number out of its range); only the provider
        // knows its own error numbers.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IStoreDataFaultClassifier>(
            static provider => new SqlStoreDataFaultClassifier(
                provider.GetRequiredService<SqlStoreContext>().Dialect)));

        // Same reasoning, for the `tracon migrate` CLI command:
        // MigrationRunner is linked-source, so a consumer that
        // references more than one provider sees ambiguous types with the
        // same name (CS0433). IMigrationApplier is the resolvable seam.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IMigrationApplier>(
            static provider => provider.GetRequiredService<MigrationRunner>()));

        // State preflight: read-only, tenant-agnostic counting of
        // the stored schema generations. A separate seam from ISessionStore on
        // purpose - that one always filters by tenant, pages, and pulls the
        // whole state payload, none of which a whole-database preflight wants.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IStatePreflightReader>(
            static provider => new SqlStatePreflightReader(provider.GetRequiredService<SqlStoreContext>())));

        // The audit trail ledger also replaces the in-memory one.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IAuditLog, SqlAuditLog>());

        // Replaces the in-memory stores. TryAdd would not work here.
        //
        // All five write-performing stores are registered wrapped in the
        // audit-trail decorators, so the audit trail behaves the same way
        // whether the store is in-memory or in a database. Rationale: same pattern
        // as the AddTracon() registration in Tracon.Core
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
                provider.GetRequiredService<TraconMetrics>(),
                provider.GetService<TimeProvider>())));
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunStore, SqlRunStore>());

        // Workflow definitions and checkpoints. The definition store
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

        // Job queue and schedule stores. Neither is wrapped: the
        // queue carries its own state machine (Pending/Leased/Running/...),
        // same rationale as the workflow checkpoint store.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IJobStore, SqlJobStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IJobScheduleStore, SqlJobScheduleStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IInboundTriggerStore, SqlInboundTriggerStore>());

        // Eval suite/case/run store. Not wrapped: same rationale as
        // the job queue stores, it carries its own state machine.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IEvalStore, SqlEvalStore>());

        // Quota and webhook stores.
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

        // Tenant-scoped API keys. Not wrapped: same rationale as the
        // quota/webhook stores, administrator actions are written to the audit
        // trail separately at the HTTP layer.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IApiKeyStore, SqlApiKeyStore>());

        // Tenant provider bindings (BYOK) and egress policy. Same
        // rationale as the API key store: not wrapped, administrator actions
        // are written to the audit trail separately at the HTTP layer.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantProviderBindingStore, SqlTenantProviderBindingStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantEgressPolicyStore, SqlTenantEgressPolicyStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ITenantToolPolicyStore, SqlTenantToolPolicyStore>());

        // Retention and archival. The policy/run store (control
        // plane) is NOT wrapped in the audit-trail decorator: same rationale as
        // the webhook/quota stores (administrator actions are written
        // separately at the HTTP layer). The data plane (IRetentionStore) is
        // meaningful only while a SQL provider is enabled; it replaces the
        // in-memory NullRetentionStore here.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRetentionPolicyStore, SqlRetentionPolicyStore>());
        // Built like SqlAttachmentStore: the optional IAttachmentStorage
        // must reach the data plane, which deletes an attachment's external
        // content before its row.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRetentionStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlRetentionStore>(provider)));

        // Data subject export/erasure. Replaces the in-memory
        // NullDataSubjectStore; meaningful only with a SQL provider on. Built
        // like the retention data plane, for the same reason.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IDataSubjectStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlDataSubjectStore>(provider)));

        // Single-executor election. Replaces the in-memory
        // InMemorySingletonLeaseStore; lease sharing is only meaningful here in
        // a multi-instance deployment.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<ISingletonLeaseStore, SqlSingletonLeaseStore>());

        // Whether the state is shared between processes depends on the database, so
        // the provider hands the value in; it replaces Core's in-process default.
        services.Replace(stateSharing);

        // Shared rate-limit counter. Replaces the in-memory
        // InMemoryRateLimitCounterStore; what makes Tracon:RateLimit:Scope =
        // Deployment hold across instances. Scope = Instance never calls it.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRateLimitCounterStore, SqlRateLimitCounterStore>());

        // Call recording. Writes only if UseVoiceConversation() was
        // called; the store stays empty otherwise. NOT wrapped in the
        // audit-trail decorator: the record is not an administrator decision,
        // it is a byproduct of execution.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IVoiceSessionStore, SqlVoiceSessionStore>());

        // Run/message scores. NOT wrapped in the audit-trail
        // decorator: same rationale as the quota/webhook stores — a score is
        // not an administrator decision, it is feedback coming from a user.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunScoreStore, SqlRunScoreStore>());

        // Idempotency-Key support. Replaces the in-memory
        // InMemoryIdempotencyStore; deduplication is only meaningful here in a
        // multi-instance deployment.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IIdempotencyStore, SqlIdempotencyStore>());

        // Run inputs. Replaces the in-memory InMemoryRunInputStore;
        // replay keeps working after the process restarts only once the input
        // is persisted.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IRunInputStore, SqlRunInputStore>());

        // Asynchronous approval inbox.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IPendingApprovalStore, SqlPendingApprovalStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IWorkflowResponseStore, SqlWorkflowResponseStore>());
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<IWorkflowRequestStore, SqlWorkflowRequestStore>());

        // Conversation branching. There is NO in-memory
        // counterpart: MAF's InMemoryChatHistoryProvider keeps history in an
        // opaque blob of session state and cannot be copied up to a given
        // sequence number. Recording only happens here; without a registered
        // store the endpoint returns 501.
        services.TryAddSingleton<IConversationBranchStore, SqlConversationBranchStore>();

        // A/B experiments. Wrapped in the audit-trail decorator for
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
        // resolve here without code changes.
#pragma warning disable MAAI001 // AgentFileStore — same rationale as TraconServiceCollectionExtensions.
        services.ReplaceTraconDefault(ServiceDescriptor.Singleton<AgentFileStore>(
            static provider => ActivatorUtilities.CreateInstance<SqlAgentFileStore>(provider)));
#pragma warning restore MAAI001
    }
}
