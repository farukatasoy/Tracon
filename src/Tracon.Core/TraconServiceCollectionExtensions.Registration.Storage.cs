using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    private static void RegisterStorageAndJobs(IServiceCollection services)
    {
        // In-memory stores are registered wrapped with the audit-writing
        // decorators. The persistence package (Tracon.PostgreSql) wraps its
        // own implementations with the same decorators (see UsePostgreSql);
        // this way the audit trail works identically no matter which store is
        // registered. Rationale: docs/arsiv/fazlar/09-YONETISIM-VE-DENETIM-IZI.md, section 9.2.
        services.TryAddTraconDefault<IAgentDefinitionStore>(static provider => new AuditingAgentDefinitionStore(
            new InMemoryAgentDefinitionStore(provider.GetRequiredService<ITenantContext>()),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingAgentDefinitionStore>>(),
            provider.GetRequiredService<TraconMetrics>()));
        // A skill carries instructions AND server-side scripts, so saving or
        // deleting one enters the audit trail for the same reason an agent
        // definition does (F-170). Its script GRANT store was already audited
        // while the skill itself was not, which recorded who permitted a
        // script to run but not who wrote it.
        services.TryAddTraconDefault<IAgentSkillStore>(static provider => new AuditingAgentSkillStore(
            new InMemoryAgentSkillStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingAgentSkillStore>>(),
            provider.GetRequiredService<TraconMetrics>()));
        services.TryAddSingleton(static provider => new AgentSkillCatalog(
            provider.GetServices<CodeSkillRegistration>(),
            provider.GetRequiredService<IAgentSkillStore>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IOptions<TraconOptions>>()));
        // Run/message scores (Phase 31). Registered BEFORE IRunStore:
        // InMemoryRunStore.GetStatisticsAsync's summary calculation obtains
        // this shared single instance via DI (see the InMemoryRunStore constructor).
        //
        // The agent name resolver closure captures `provider` but does not call
        // GetRequiredService<IRunStore>() until it actually RUNS (inside a later
        // SummarizeAsync call) -- IRunStore's own factory needs IRunScoreStore,
        // so resolving it eagerly here would be a constructor-time cycle
        // (Phase 154).
        services.TryAddTraconDefault<IRunScoreStore>(provider => new InMemoryRunScoreStore(
            provider.GetRequiredService<ITenantContext>(),
            (runId, cancellationToken) => ResolveScoredRunAgentNameAsync(provider, runId, cancellationToken)));
        services.TryAddTraconDefault<IRunStore>(static provider => new InMemoryRunStore(
            provider.GetRequiredService<IRunScoreStore>(),
            provider.GetRequiredService<ITenantContext>()));

        // Run inputs (Phase 47). The store is always registered (K-018:
        // first-class) - replay works even without a SQL provider. Persistent
        // providers replace this with their own implementations.
        services.TryAddTraconDefault<IRunInputStore, InMemoryRunInputStore>();

        // Async approval inbox (Phase 55). The store is always registered (the
        // SAME rationale as K-018): if a queued run requests approval, the
        // store works even without a persistent provider. Persistent providers
        // replace this with their own implementations.
        services.TryAddTraconDefault<IPendingApprovalStore>(static provider => new InMemoryPendingApprovalStore(
            provider.GetRequiredService<ITenantContext>()));

        // Script run permissions. The store is always registered; the run
        // feature itself is DISABLED until UseSkillScripts is called. The mere
        // existence of a permission record runs nothing by itself.
        services.TryAddTraconDefault<ISkillScriptGrantStore>(static provider => new AuditingSkillScriptGrantStore(
            new InMemorySkillScriptGrantStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingSkillScriptGrantStore>>(),
            provider.GetRequiredService<TraconMetrics>()));

        services.TryAddTraconDefault<ISessionStore>(static provider => new AuditingSessionStore(
            new InMemorySessionStore(provider.GetRequiredService<ITenantContext>()),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingSessionStore>>(),
            provider.GetRequiredService<TraconMetrics>()));
        services.TryAddTraconDefault<ITraceStore>(static provider => new InMemoryTraceStore(
            provider.GetRequiredService<ITenantContext>()));
        services.TryAddTraconDefault<IToolApprovalRuleStore>(static provider => new AuditingToolApprovalRuleStore(
            new InMemoryToolApprovalRuleStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingToolApprovalRuleStore>>(),
            provider.GetRequiredService<TraconMetrics>()));
        services.TryAddTraconDefault<IMcpServerStore>(static provider => new AuditingMcpServerStore(
            new InMemoryMcpServerStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingMcpServerStore>>(),
            provider.GetRequiredService<TraconMetrics>()));
        // Attachment store and type guard. When IAttachmentStorage is not
        // registered, content lives directly in memory (in production: the database).
        services.TryAddSingleton<AttachmentTypeGuard>();
        services.TryAddTraconDefault<IAttachmentStore>(
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
        services.TryAddTraconDefault<IWorkflowDefinitionStore>(static provider => new AuditingWorkflowDefinitionStore(
            new InMemoryWorkflowDefinitionStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingWorkflowDefinitionStore>>(),
            provider.GetRequiredService<TraconMetrics>()));
        services.TryAddTraconDefault<IWorkflowCheckpointStore, InMemoryWorkflowCheckpointStore>();

        // Job queue and scheduling stores (Phase 17). Same split as the
        // workflow stores: the stores are ALWAYS registered; the background
        // worker is turned on/off with TraconSchedulingOptions.RunWorker.
        // Neither of these two is wrapped with a decorator - the job queue
        // carries its own state machine, and an audit trail can be added here in Phase 18.
        services.TryAddTraconDefault<IJobStore, InMemoryJobStore>();
        services.TryAddTraconDefault<IJobScheduleStore, InMemoryJobScheduleStore>();

        // Inbound triggers (Phase 66): an external, signed event queues a run
        // through the SAME job queue as everything else. The store is always
        // registered (K-018); the accept endpoint is unauthenticated by
        // design (K-395's pattern), so the dispatcher itself, not an
        // ASP.NET Core filter, verifies the signature. Explicit factories:
        // IConfiguration may not be registered outside ASP.NET Core hosting,
        // and the built-in DI container does not fill in constructor
        // parameters that carry a default value.
        services.TryAddTraconDefault<IInboundTriggerStore, InMemoryInboundTriggerStore>();
        services.TryAddSingleton(static provider => new InboundTriggerSecretResolver(
            provider.GetService<IConfiguration>(),
            provider.GetRequiredService<IOptionsMonitor<TraconInboundTriggerOptions>>(),
            provider.GetRequiredService<IOptions<TraconOptions>>()));
        services.TryAddSingleton(static provider => new InboundTriggerRateLimiter(
            provider.GetRequiredService<IOptionsMonitor<TraconInboundTriggerOptions>>(),
            provider.GetService<TimeProvider>()));
        services.TryAddSingleton(static provider => new InboundTriggerDispatcher(
            provider.GetRequiredService<IInboundTriggerStore>(),
            provider.GetRequiredService<InboundTriggerSecretResolver>(),
            provider.GetRequiredService<InboundTriggerRateLimiter>(),
            provider.GetRequiredService<IIdempotencyStore>(),
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IRunStore>(),
            provider.GetRequiredService<IOptionsMonitor<TraconInboundTriggerOptions>>(),
            provider.GetService<TimeProvider>()));

        // 🚨 Every handler -- built-in and consumer alike -- is registered
        // SCOPED and keyed. The worker resolves it from a fresh scope per
        // execution and looks it up by exact key match, so registration order
        // no longer decides the winner and a handler may take scoped
        // dependencies. AddBuiltInJobHandler is the internal twin of the
        // public AddJobHandler<T>(key): it is the only path allowed to use the
        // reserved "tracon." namespace.
        services.AddBuiltInJobHandler<AgentBatchJobHandler>(JobHandlerKeys.AgentBatch);
        services.AddBuiltInJobHandler<AgentRunJobHandler>(JobHandlerKeys.AgentRun);

        // Post-approval resume (Phase 55). SEPARATE from AgentRunJobHandler:
        // runs with a NEW RunId, does not touch the old one (K-014).
        services.AddBuiltInJobHandler<ApprovalResumeJobHandler>(JobHandlerKeys.ApprovalResume);

        // Interrupted-run continuation (Phase 87). Triggered only by
        // RunReconciliationService, never by a client request; SEPARATE from
        // both AgentRunJobHandler and ApprovalResumeJobHandler for the same
        // "new RunId, old row never changes" reason.
        services.AddBuiltInJobHandler<RunContinuationJobHandler>(JobHandlerKeys.RunContinuation);

        // An explicit factory is used: the built-in DI container does not fill
        // in constructor parameters that carry a default value, and
        // IWorkflowRunner is not registered in most setups (unless
        // UseWorkflows() is called) - the same rationale applies to the
        // RunRecordingAgentDecorator registration too.
        services.AddBuiltInJobHandler(
            JobHandlerKeys.Workflow,
            static provider => new WorkflowJobHandler(
                provider.GetService<IWorkflowRunner>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<WorkflowJobHandler>>()));

        // Evaluation (eval) infrastructure (Phase 18). The suite/case/run store
        // is always registered; runs execute through the same job queue
        // (JobHandlerKeys.Eval). The check registry is built from custom records
        // (added via AddEvalCheck); the six built-in kinds are fixed inside EvalCheckRegistry.
        services.TryAddTraconDefault<IEvalStore, InMemoryEvalStore>();
        services.TryAddSingleton<EvalCheckRegistry>();

        // Phase 155: the seam around "what grades a suite". TryAdd, so a
        // consumer's own factory wins and registering none keeps MAF's
        // LocalEvaluator built over the suite's own checks.
        services.TryAddSingleton<IEvalEvaluatorFactory, LocalEvalEvaluatorFactory>();
        services.AddBuiltInJobHandler<EvalJobHandler>(JobHandlerKeys.Eval);

        // Promoting production cases (Phase 45, F-53). Reads the query text
        // from the run's session; all of its dependencies are already registered above.
        services.TryAddSingleton<RunToCasePromoter>();

        // Online evaluation (Phase 49). The sampler and job handler are always
        // registered (K-018: first-class); nothing SCORES anything because the
        // OnlineEvaluationOptions default keeps both gates closed
        // (Enabled=false, SampleRate=0) and no IRunJudge is registered - the
        // way to turn on the built-in judge is the AddModelRunJudge() call.
        // A factory, not a type-based registration: the constructor is internal
        // and the container only activates a public one. The two optional
        // dependencies are passed explicitly, because a factory that omits one
        // leaves it null without any error.
        services.TryAddSingleton(static provider => new RunSampler(
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IOptionsMonitor<OnlineEvaluationOptions>>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<RunSampler>>()));
        services.TryAddSingleton<OnlineEvalSummaryService>();
        services.TryAddSingleton<RunJudgeSet>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RunJudgeValidationService>());

        // The POST /api/runs/{id}/judge endpoint requests OnlineEvalJobHandler
        // by its OWN type to call JudgeRunAsync directly. Since Phase 137 a
        // handler is registered by its concrete type anyway (the worker
        // resolves it through the registry's key -> type map), so ONE scoped
        // registration now serves both the endpoint and the worker.
        services.AddBuiltInJobHandler<OnlineEvalJobHandler>(JobHandlerKeys.OnlineEval);

        // Quotas and event publishing (Phase 21). The stores are always
        // registered; nothing is rejected unless a rule is defined, and no
        // event is published when there is no subscriber. This is why no
        // separate Use...() call is needed.
        services.TryAddTraconDefault<IQuotaStore, InMemoryQuotaStore>();
        services.TryAddTraconDefault<IWebhookStore, InMemoryWebhookStore>();

        // Tenant-scoped API keys (Phase 53). The store is always registered
        // (K-018: first-class); the static bearer token's behavior does not
        // change unless a key is created (K1 - zero surprise).
        services.TryAddTraconDefault<IApiKeyStore, InMemoryApiKeyStore>();

        // 🚨 The one guard every outbound surface builds its client from
        // (K-164). Registered unconditionally: a surface that could not
        // resolve it would fall back to its own unguarded handler.
        services.TryAddSingleton(static provider => new EgressSocketGuard(
            provider.GetRequiredService<IOptionsMonitor<TraconEgressOptions>>()));

        // 🚨 SSRF protection is embedded inside this client; the consumer
        // cannot change it (K-164). Explicit factory: TimeProvider may not be
        // registered.
        services.TryAddSingleton(static provider => new WebhookHttpClient(
            provider.GetRequiredService<IOptionsMonitor<TraconWebhookOptions>>(),
            provider.GetRequiredService<IOptionsMonitor<TraconEgressOptions>>()));

        services.TryAddSingleton<IWebhookPublisher>(static provider => new WebhookPublisher(
            provider.GetRequiredService<IWebhookStore>(),
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IOptionsMonitor<TraconWebhookOptions>>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<WebhookPublisher>>()));

        services.TryAddSingleton(static provider => new QuotaEnforcer(
            provider.GetRequiredService<IQuotaStore>(),
            provider.GetRequiredService<IOptionsMonitor<TraconQuotaOptions>>(),
            provider.GetService<IWebhookPublisher>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<QuotaEnforcer>>()));

        // Quota gauge (Phase 35). A BackgroundService: the gauge callback reads
        // only the cached snapshot and this service refreshes it on a timer.
        // Registered unconditionally, because the on/off flag is not known
        // here - the consumer may configure TraconOptions after AddTracon
        // (K-251). Hosting it also makes the container build the object EARLY,
        // so the ObservableGauges exist. While EnableQuotaUsageGauge is off
        // (the default) ExecuteAsync returns at once: no timer, no query (K1).
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QuotaUsageObserver>(
            static provider => new QuotaUsageObserver(
                provider.GetRequiredService<IQuotaStore>(),
                provider.GetRequiredService<ITenantStore>(),
                provider.GetRequiredService<IOptionsMonitor<TraconOptions>>(),
                provider.GetRequiredService<IOptionsMonitor<TraconQuotaOptions>>(),
                provider.GetRequiredService<SchemaReadyGate>(),
                provider.GetService<System.Diagnostics.Metrics.IMeterFactory>(),
                provider.GetService<TimeProvider>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<QuotaUsageObserver>>())));

        // The delivery handler uses the SAME queue as Phase 17 (K-160).
        services.AddBuiltInJobHandler(
            JobHandlerKeys.WebhookDelivery,
            static provider => new WebhookDeliveryJobHandler(
                provider.GetRequiredService<IWebhookStore>(),
                provider.GetRequiredService<WebhookHttpClient>(),
                provider.GetRequiredService<IOptionsMonitor<TraconWebhookOptions>>(),
                provider.GetRequiredService<IOptions<TraconOptions>>(),
                provider.GetService<IConfiguration>(),
                provider.GetService<TimeProvider>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<WebhookDeliveryJobHandler>>()));

        // Idempotency-Key support (Phase 43). The store is always registered
        // (K-018: first-class); InMemoryIdempotencyStore is sufficient for a
        // single-instance deployment. No separate Use...() call is needed.
        services.TryAddTraconDefault<IIdempotencyStore, InMemoryIdempotencyStore>();

        // Data retention and archiving (Phase 25). The policy/run store is
        // always registered (control plane); the data plane
        // (IRetentionStore), however, is non-functional in an in-memory setup
        // (NullRetentionStore) - retention is meaningful only with a
        // persistent SQL provider on. When IArchiveSink is NOT registered, a
        // policy requesting archiving deletes no rows (the Phase 25 decision).
        services.TryAddTraconDefault<IRetentionPolicyStore, InMemoryRetentionPolicyStore>();
        services.TryAddTraconDefault<IRetentionStore, NullRetentionStore>();
        services.TryAddSingleton<RetentionPolicyResolver>();

        // Data subject export/erasure (Phase 64). Same precedent as
        // IRetentionStore above: the data plane is non-functional in an
        // in-memory setup (NullDataSubjectStore). There is deliberately NO
        // default registration for IDataSubjectResolver — Tracon does not
        // store personal identity, so only the consumer can supply one; the
        // endpoints return 409 until it is registered.
        services.TryAddTraconDefault<IDataSubjectStore, NullDataSubjectStore>();

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
            provider.GetRequiredService<IOptionsMonitor<TraconRetentionOptions>>(),
            provider.GetService<IArchiveSink>(),
            provider.GetService<TimeProvider>(),
            provider.GetService<Microsoft.Extensions.Logging.ILogger<RetentionExecutor>>()));

        services.AddBuiltInJobHandler(
            JobHandlerKeys.Retention,
            static provider => new RetentionJobHandler(
                provider.GetRequiredService<RetentionExecutor>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<RetentionJobHandler>>()));

        // 🚨 Built BEFORE the worker's first tick and before the migrations:
        // a duplicate or reserved handler key must break the host's START, not
        // surface later as one swallowed background log line.
        services.TryAddSingleton(static provider => JobHandlerRegistry.Create(
            provider.GetServices<JobHandlerRegistration>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, JobHandlerRegistryValidator>());

        // The supported way for consumer code to queue work: it fills in the
        // persistence fields IJobStore.EnqueueAsync demands, and refuses a
        // handler key nobody registered.
        services.TryAddSingleton<IJobDispatcher>(static provider => new JobDispatcher(
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<JobHandlerRegistry>(),
            provider.GetRequiredService<IOptionsMonitor<TraconSchedulingOptions>>(),
            provider.GetService<TimeProvider>()));

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, JobWorkerBackgroundService>());

        // Queue-depth gauge (Phase 133). The same shape and the same reasons as
        // QuotaUsageObserver above: a background refresh behind a cache-only
        // callback, registered unconditionally (K-251). While
        // EnableJobQueueDepthGauge is off (the default) ExecuteAsync returns
        // at once and the store is never queried.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobQueueDepthObserver>(
            static provider => new JobQueueDepthObserver(
                provider.GetRequiredService<IJobStore>(),
                provider.GetRequiredService<IOptionsMonitor<TraconOptions>>(),
                provider.GetRequiredService<SchemaReadyGate>(),
                provider.GetService<System.Diagnostics.Metrics.IMeterFactory>(),
                provider.GetService<TimeProvider>(),
                provider.GetService<Microsoft.Extensions.Logging.ILogger<JobQueueDepthObserver>>(),
                provider.GetRequiredService<TraconMetrics>())));

        // Orphaned run reconciliation (Phase 54). Both return immediately and
        // issue no query to the store while RunReconciliationOptions.Enabled is
        // off (the default) (K1).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RunHeartbeatWriter>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, RunReconciliationService>());

        // Approval expiration scan (Phase 55). TraconApprovalOptions.ExpirationEnabled
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

    /// <summary>
    /// Resolves a run's agent name for <c>InMemoryRunScoreStore</c>'s
    /// <see cref="IRunScoreStore.SummarizeAsync"/>. A separate method (not a
    /// lambda body) only so the DI registration comment above it stays
    /// readable.
    /// </summary>
    private static async ValueTask<string?> ResolveScoredRunAgentNameAsync(
        IServiceProvider provider,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var run = await provider.GetRequiredService<IRunStore>()
            .GetRunAsync(runId, cancellationToken)
            .ConfigureAwait(false);

        return run?.AgentName;
    }
}
