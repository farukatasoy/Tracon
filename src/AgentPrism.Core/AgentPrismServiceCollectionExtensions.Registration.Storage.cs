using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

public static partial class AgentPrismServiceCollectionExtensions
{
    private static void RegisterStorageAndJobs(IServiceCollection services)
    {
        // In-memory stores are registered wrapped with the audit-writing
        // decorators. The persistence package (AgentPrism.PostgreSql) wraps its
        // own implementations with the same decorators (see UsePostgreSql);
        // this way the audit trail works identically no matter which store is
        // registered. Rationale: docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md, section 9.2.
        services.TryAddSingleton<IAgentDefinitionStore>(static provider => new AuditingAgentDefinitionStore(
            new InMemoryAgentDefinitionStore(provider.GetRequiredService<ITenantContext>()),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingAgentDefinitionStore>>()));
        services.TryAddSingleton<IAgentSkillStore, InMemoryAgentSkillStore>();
        services.TryAddSingleton(static provider => new AgentSkillCatalog(
            provider.GetServices<CodeSkillRegistration>(),
            provider.GetRequiredService<IAgentSkillStore>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IOptions<AgentPrismOptions>>()));
        // Run/message scores (Phase 31). Registered BEFORE IRunStore:
        // InMemoryRunStore.GetStatisticsAsync's summary calculation obtains
        // this shared single instance via DI (see the InMemoryRunStore constructor).
        services.TryAddSingleton<IRunScoreStore, InMemoryRunScoreStore>();
        services.TryAddSingleton<IRunStore>(static provider => new InMemoryRunStore(
            provider.GetRequiredService<IRunScoreStore>(),
            provider.GetRequiredService<ITenantContext>()));

        // Run inputs (Phase 47). The store is always registered (K-018:
        // first-class) - replay works even without a SQL provider. Persistent
        // providers replace this with their own implementations.
        services.TryAddSingleton<IRunInputStore, InMemoryRunInputStore>();

        // Async approval inbox (Phase 55). The store is always registered (the
        // SAME rationale as K-018): if a queued run requests approval, the
        // store works even without a persistent provider. Persistent providers
        // replace this with their own implementations.
        services.TryAddSingleton<IPendingApprovalStore>(static provider => new InMemoryPendingApprovalStore(
            provider.GetRequiredService<ITenantContext>()));

        // Script run permissions. The store is always registered; the run
        // feature itself is DISABLED until UseSkillScripts is called. The mere
        // existence of a permission record runs nothing by itself.
        services.TryAddSingleton<ISkillScriptGrantStore>(static provider => new AuditingSkillScriptGrantStore(
            new InMemorySkillScriptGrantStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingSkillScriptGrantStore>>()));

        services.TryAddSingleton<ISessionStore>(static provider => new AuditingSessionStore(
            new InMemorySessionStore(provider.GetRequiredService<ITenantContext>()),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingSessionStore>>()));
        services.TryAddSingleton<ITraceStore>(static provider => new InMemoryTraceStore(
            provider.GetRequiredService<ITenantContext>()));
        services.TryAddSingleton<IToolApprovalRuleStore>(static provider => new AuditingToolApprovalRuleStore(
            new InMemoryToolApprovalRuleStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingToolApprovalRuleStore>>()));
        services.TryAddSingleton<IMcpServerStore>(static provider => new AuditingMcpServerStore(
            new InMemoryMcpServerStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingMcpServerStore>>()));
        // Attachment store and type guard. When IAttachmentStorage is not
        // registered, content lives directly in memory (in production: the database).
        services.TryAddSingleton<AttachmentTypeGuard>();
        services.TryAddSingleton<IAttachmentStore>(
            static provider => new InMemoryAttachmentStore(provider.GetService<IAttachmentStorage>()));

        // Image generation uses the same attachment store as uploads and voice.
        // The writer is registered unconditionally, but it is reached only when
        // the conditional generate_image tool or its optional HTTP endpoint exists.
        services.TryAddSingleton<ImagePricing>();
        services.TryAddSingleton<ImageAttachmentWriter>();
        services.TryAddSingleton<ImageGeneratorResolver>();

        // Workflow definitions and checkpoints. The stores are ALWAYS
        // registered; the execution engine, however, is DISABLED until
        // UseWorkflows() is called. This split is deliberate: the HTTP layer
        // must be able to list and manage definitions even without the
        // engine, only the "run" endpoint returns 501.
        services.TryAddSingleton<IWorkflowDefinitionStore>(static provider => new AuditingWorkflowDefinitionStore(
            new InMemoryWorkflowDefinitionStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingWorkflowDefinitionStore>>()));
        services.TryAddSingleton<IWorkflowCheckpointStore, InMemoryWorkflowCheckpointStore>();

        // Job queue and scheduling stores (Phase 17). Same split as the
        // workflow stores: the stores are ALWAYS registered; the background
        // worker is turned on/off with AgentPrismSchedulingOptions.RunWorker.
        // Neither of these two is wrapped with a decorator - the job queue
        // carries its own state machine, and an audit trail can be added here in Phase 18.
        services.TryAddSingleton<IJobStore, InMemoryJobStore>();
        services.TryAddSingleton<IJobScheduleStore, InMemoryJobScheduleStore>();

        // Inbound triggers (Phase 66): an external, signed event queues a run
        // through the SAME job queue as everything else. The store is always
        // registered (K-018); the accept endpoint is unauthenticated by
        // design (K-395's pattern), so the dispatcher itself, not an
        // ASP.NET Core filter, verifies the signature. Explicit factories:
        // IConfiguration may not be registered outside ASP.NET Core hosting,
        // and the built-in DI container does not fill in constructor
        // parameters that carry a default value.
        services.TryAddSingleton<IInboundTriggerStore, InMemoryInboundTriggerStore>();
        services.TryAddSingleton(static provider => new InboundTriggerSecretResolver(
            provider.GetService<IConfiguration>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismInboundTriggerOptions>>()));
        services.TryAddSingleton(static provider => new InboundTriggerRateLimiter(
            provider.GetRequiredService<IOptionsMonitor<AgentPrismInboundTriggerOptions>>(),
            provider.GetService<TimeProvider>()));
        services.TryAddSingleton(static provider => new InboundTriggerDispatcher(
            provider.GetRequiredService<IInboundTriggerStore>(),
            provider.GetRequiredService<InboundTriggerSecretResolver>(),
            provider.GetRequiredService<InboundTriggerRateLimiter>(),
            provider.GetRequiredService<IIdempotencyStore>(),
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IRunStore>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismInboundTriggerOptions>>(),
            provider.GetService<TimeProvider>()));

        // Three built-in handlers: agent batch run, queued single run (Phase
        // 46), and workflow. All three are added with TryAddEnumerable; Phase
        // 18 (eval) adds its own handler the same way with AddJobHandler<T>().
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, AgentBatchJobHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, AgentRunJobHandler>());

        // Post-approval resume (Phase 55). SEPARATE from AgentRunJobHandler:
        // runs with a NEW RunId, does not touch the old one (K-014).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, ApprovalResumeJobHandler>());

        // Interrupted-run continuation (Phase 87). Triggered only by
        // RunReconciliationService, never by a client request; SEPARATE from
        // both AgentRunJobHandler and ApprovalResumeJobHandler for the same
        // "new RunId, old row never changes" reason.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, RunContinuationJobHandler>());

        // An explicit factory is used: the built-in DI container does not fill
        // in constructor parameters that carry a default value, and
        // IWorkflowRunner is not registered in most setups (unless
        // UseWorkflows() is called) - the same rationale applies to the
        // RunRecordingAgentDecorator registration too.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, WorkflowJobHandler>(
            static provider => new WorkflowJobHandler(
                provider.GetService<IWorkflowRunner>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<WorkflowJobHandler>>())));

        // Evaluation (eval) infrastructure (Phase 18). The suite/case/run store
        // is always registered; runs execute through the same job queue
        // (JobKind.Eval). The check registry is built from custom records
        // (added via AddEvalCheck); the six built-in kinds are fixed inside EvalCheckRegistry.
        services.TryAddSingleton<IEvalStore, InMemoryEvalStore>();
        services.TryAddSingleton<EvalCheckRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, EvalJobHandler>());

        // Promoting production cases (Phase 45, F-53). Reads the query text
        // from the run's session; all of its dependencies are already registered above.
        services.TryAddSingleton<RunToCasePromoter>();

        // Online evaluation (Phase 49). The sampler and job handler are always
        // registered (K-018: first-class); nothing SCORES anything because the
        // OnlineEvaluationOptions default keeps both gates closed
        // (Enabled=false, SampleRate=0) and no IRunJudge is registered - the
        // way to turn on the built-in judge is the AddModelRunJudge() call.
        services.TryAddSingleton<RunSampler>();
        services.TryAddSingleton<OnlineEvalSummaryService>();
        services.TryAddSingleton<RunJudgeSet>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RunJudgeValidationService>());

        // 🚨 The concrete type is ALSO registered: the POST
        // /api/runs/{id}/judge endpoint requests OnlineEvalJobHandler by its
        // OWN type to call JudgeRunAsync directly. TryAddEnumerable(Singleton<IJobHandler, T>)
        // only produces a registration resolvable via the interface; without
        // registering the concrete type SEPARATELY, the endpoint could not
        // find it in DI. The same factory returns the SAME instance, so the
        // two registrations (concrete + interface) share a single singleton.
        services.TryAddSingleton<OnlineEvalJobHandler>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, OnlineEvalJobHandler>(
            static provider => provider.GetRequiredService<OnlineEvalJobHandler>()));

        // Quotas and event publishing (Phase 21). The stores are always
        // registered; nothing is rejected unless a rule is defined, and no
        // event is published when there is no subscriber. This is why no
        // separate Use...() call is needed.
        services.TryAddSingleton<IQuotaStore, InMemoryQuotaStore>();
        services.TryAddSingleton<IWebhookStore, InMemoryWebhookStore>();

        // Tenant-scoped API keys (Phase 53). The store is always registered
        // (K-018: first-class); the static bearer token's behavior does not
        // change unless a key is created (K1 - zero surprise).
        services.TryAddSingleton<IApiKeyStore, InMemoryApiKeyStore>();

        // 🚨 The one guard every outbound surface builds its client from
        // (K-164). Registered unconditionally: a surface that could not
        // resolve it would fall back to its own unguarded handler.
        services.TryAddSingleton(static provider => new EgressSocketGuard(
            provider.GetRequiredService<IOptionsMonitor<AgentPrismEgressOptions>>()));

        // 🚨 SSRF protection is embedded inside this client; the consumer
        // cannot change it (K-164). Explicit factory: TimeProvider may not be
        // registered.
        services.TryAddSingleton(static provider => new WebhookHttpClient(
            provider.GetRequiredService<IOptionsMonitor<AgentPrismWebhookOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismEgressOptions>>()));

        services.TryAddSingleton<IWebhookPublisher>(static provider => new WebhookPublisher(
            provider.GetRequiredService<IWebhookStore>(),
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismWebhookOptions>>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<WebhookPublisher>>()));

        services.TryAddSingleton(static provider => new QuotaEnforcer(
            provider.GetRequiredService<IQuotaStore>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismQuotaOptions>>(),
            provider.GetService<IWebhookPublisher>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<QuotaEnforcer>>()));

        // Quota gauge (Phase 35). The sole purpose of adding it as an
        // IHostedService is to have the container resolve this object EARLY
        // while the host starts; otherwise the ObservableGauges would never be
        // created unless some consumer resolved it. While
        // EnableQuotaUsageGauge is off (the default) the gauge is still
        // created but never touches the cache - see the class documentation.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QuotaUsageObserver>(
            static provider => new QuotaUsageObserver(
                provider.GetRequiredService<IQuotaStore>(),
                provider.GetRequiredService<ITenantStore>(),
                provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
                provider.GetRequiredService<IOptionsMonitor<AgentPrismQuotaOptions>>(),
                provider.GetService<System.Diagnostics.Metrics.IMeterFactory>(),
                provider.GetService<TimeProvider>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<QuotaUsageObserver>>())));

        // The delivery handler uses the SAME queue as Phase 17 (K-160).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, WebhookDeliveryJobHandler>(
            static provider => new WebhookDeliveryJobHandler(
                provider.GetRequiredService<IWebhookStore>(),
                provider.GetRequiredService<WebhookHttpClient>(),
                provider.GetRequiredService<IOptionsMonitor<AgentPrismWebhookOptions>>(),
                provider.GetService<IConfiguration>(),
                provider.GetService<TimeProvider>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<WebhookDeliveryJobHandler>>())));

        // Idempotency-Key support (Phase 43). The store is always registered
        // (K-018: first-class); InMemoryIdempotencyStore is sufficient for a
        // single-instance deployment. No separate Use...() call is needed.
        services.TryAddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        // Data retention and archiving (Phase 25). The policy/run store is
        // always registered (control plane); the data plane
        // (IRetentionStore), however, is non-functional in an in-memory setup
        // (NullRetentionStore) - retention is meaningful only with a
        // persistent SQL provider on. When IArchiveSink is NOT registered, a
        // policy requesting archiving deletes no rows (the Phase 25 decision).
        services.TryAddSingleton<IRetentionPolicyStore, InMemoryRetentionPolicyStore>();
        services.TryAddSingleton<IRetentionStore, NullRetentionStore>();
        services.TryAddSingleton<RetentionPolicyResolver>();

        // Data subject export/erasure (Phase 64). Same precedent as
        // IRetentionStore above: the data plane is non-functional in an
        // in-memory setup (NullDataSubjectStore). There is deliberately NO
        // default registration for IDataSubjectResolver — AgentPrism does not
        // store personal identity, so only the consumer can supply one; the
        // endpoints return 409 until it is registered.
        services.TryAddSingleton<IDataSubjectStore, NullDataSubjectStore>();

        // Resolves the internal chat-history conversation a session carries
        // (ISessionStore + IAgentCatalog are both always registered, so this has
        // no separate on/off switch).
        services.TryAddSingleton<SessionConversationResolver>();

        // An explicit factory is used: the built-in DI container does not fill
        // in constructor parameters that carry a default value (IArchiveSink,
        // TimeProvider, ILogger may not be registered).
        services.TryAddSingleton(static provider => new RetentionExecutor(
            provider.GetRequiredService<IRetentionPolicyStore>(),
            provider.GetRequiredService<IRetentionStore>(),
            provider.GetRequiredService<RetentionPolicyResolver>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismRetentionOptions>>(),
            provider.GetService<IArchiveSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<RetentionExecutor>>()));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, RetentionJobHandler>(
            static provider => new RetentionJobHandler(provider.GetRequiredService<RetentionExecutor>())));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, JobWorkerBackgroundService>());

        // Orphaned run reconciliation (Phase 54). Both return immediately and
        // issue no query to the store while RunReconciliationOptions.Enabled is
        // off (the default) (K1).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RunHeartbeatWriter>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RunReconciliationService>());

        // Approval expiration scan (Phase 55). AgentPrismApprovalOptions.ExpirationEnabled
        // defaults to ON (a safety requirement unlike RunReconciliationOptions),
        // but when SQL is not registered, SchemaReadyGate opens immediately and
        // no new query is issued after the first tick.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ApprovalExpirationService>());

        // Canary evaluation and automatic rollback (Phase 56).
        // CanaryOptions.AutoRollbackEnabled defaults to DISABLED (K1); no
        // experiment is scanned unless turned on.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, CanaryEvaluationService>());
    }
}
