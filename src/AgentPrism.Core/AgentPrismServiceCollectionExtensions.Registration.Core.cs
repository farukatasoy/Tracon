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
    private static void RegisterCoreInfrastructure(IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismOptions>, AgentPrismOptionsValidator>());

        // Tenant context. In a multi-tenant setup, the consumer registers
        // their own implementation before this call.
        services.TryAddSingleton<ITenantContext, SingleTenantContext>();

        // Run attribution: who ran this, and for which job. The default reports
        // only what AmbientRunAttributionScope carries, so an application that
        // registers nothing keeps its exact current behaviour. A consumer binds
        // this to its own identity pipeline; TryAdd makes that registration win.
        services.TryAddSingleton<IRunAttributionContext, DefaultRunAttributionContext>();

        // Tool authorization (phase 69, F-113): allows every call by default, so
        // an application that registers nothing keeps today's behaviour exactly.
        // A consumer replaces this registration to enforce its own policy.
        services.TryAddSingleton<IToolAuthorizationHandler, AllowAllToolAuthorizationHandler>();

        // Registries.
        // The image tool is conditional: registering its provider package alone
        // must not expose a paid tool. ToolRegistry.Create reads the final option
        // and IImageGenerator registrations only when the registry is first built.
        services.TryAddSingleton<IToolRegistry>(ToolRegistry.Create);

        // The circuit breaker is registered BEFORE IModelProviderRegistry:
        // ModelProviderRegistry resolves it in its constructor and wraps every
        // IChatClient it produces with it. An explicit factory is used: the
        // built-in DI container does not fill in constructor parameters that
        // carry a default value, and TimeProvider may not be registered.
        services.TryAddSingleton(static provider => new ModelProviderCircuitBreaker(
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            provider.GetService<TimeProvider>()));

        // Per-provider outgoing concurrency limit (phase 62, F-44). Same
        // registration rationale as the circuit breaker: registered BEFORE
        // IModelProviderRegistry so its constructor can resolve it.
        services.TryAddSingleton(static provider => new ProviderConcurrencyLimiter(
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>()));

        // Content moderation pipeline (Phase 48). Always registered, but when
        // HasGuards is false, the registry never adds the moderation wrapper.
        // Explicit factory: registration order does not matter, IContentGuard
        // instances can also be added after this call (GetServices resolves lazily).
        services.TryAddSingleton(static provider => new ContentGuardPipeline(
            provider.GetServices<IContentGuard>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismContentGuardOptions>>(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<ILoggerFactory>()));

        // Tenant provider bindings / BYOK (Phase 65). The stores are always
        // registered (K-018: first-class); nothing changes for a tenant with
        // no binding (K1). Explicit factory: TenantProviderCredentialResolver
        // needs IConfiguration, which may not be registered outside ASP.NET
        // Core hosting (see its own null-safety remark).
        services.TryAddSingleton<ITenantProviderBindingStore, InMemoryTenantProviderBindingStore>();
        services.TryAddSingleton<ITenantEgressPolicyStore, InMemoryTenantEgressPolicyStore>();
        services.TryAddSingleton(static provider => new TenantProviderCredentialResolver(
            provider.GetService<IConfiguration>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismTenantProviderOptions>>()));

        // Provider retry classifier (Phase 113, F-149). Registered BEFORE
        // IModelProviderRegistry so its constructor can resolve it. The
        // taxonomy is AgentPrism's opinion; thanks to TryAddSingleton, the
        // consumer's own classifier wins (K4) - a consumer that registers
        // nothing gets DefaultProviderRetryClassifier, so FallbackChatClient's
        // retry decisions stay bit-for-bit identical to before the seam existed.
        services.TryAddSingleton<IProviderRetryClassifier, DefaultProviderRetryClassifier>();

        // Sets up the WHOLE registry pipeline (moved out of the provider
        // packages in Phase 48). An explicit factory is required: the built-in
        // DI container does not fill in constructor parameters that carry a
        // default value.
        services.TryAddSingleton<IModelProviderRegistry>(static provider => new ModelProviderRegistry(
            provider.GetServices<IModelProvider>(),
            provider.GetService<ModelProviderCircuitBreaker>(),
            provider.GetService<IAttachmentStore>(),
            provider.GetService<ITenantContext>(),
            provider.GetService<ContentGuardPipeline>(),
            provider.GetService<ILoggerFactory>(),
            provider.GetService<ProviderConcurrencyLimiter>(),
            provider.GetService<ITenantProviderBindingStore>(),
            provider.GetService<ITenantEgressPolicyStore>(),
            provider.GetService<TenantProviderCredentialResolver>(),
            // Phase 81 (F-45): when not registered, an agent that enables
            // ModelBinding.ResponseCache fails to compile instead of silently
            // running uncached - see ModelProviderRegistry.BuildPipeline.
            provider.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            provider.GetService<AgentPrismMetrics>(),
            provider.GetService<IProviderRetryClassifier>()));

        // Pre-flight context-window estimator (phase 62, F-59). Registered
        // unconditionally: POST /api/agents/{name}/estimate works regardless
        // of AgentPrismPreflightOptions.Enabled — that flag only gates the
        // inline check on the run endpoint, not the diagnostic endpoint.
        services.TryAddSingleton<ContextWindowEstimator>();

        // Cost resolver (Phase 20): model catalog, then AgentPrism:Pricing.
        services.TryAddSingleton<IRunPricingResolver, RunPricingResolver>();
        services.TryAddSingleton<RunCostRecalculationService>();

        // Error classifier (Phase 44). The taxonomy is AgentPrism's opinion;
        // thanks to TryAddSingleton, the consumer's own classifier wins (K4).
        services.TryAddSingleton<IRunErrorClassifier, DefaultRunErrorClassifier>();

        // Run cancellation registry (Phase 32). Always registered: it has no
        // side effect beyond holding an in-memory dictionary (K-165's "new
        // behavior ships disabled by default" decision is for features that
        // produce an overt side effect, this registry is not one of them).
        services.TryAddSingleton<IRunCancellationRegistry, RunCancellationRegistry>();

        // Health cache and optional background refresher. Explicit factory:
        // same rationale, TimeProvider may not be registered.
        services.TryAddSingleton(static provider => new ModelProviderHealthCache(
            provider.GetServices<IModelProvider>(),
            provider.GetRequiredService<IOptionsMonitor<AgentPrismOptions>>(),
            provider.GetService<ModelProviderCircuitBreaker>(),
            provider.GetService<TimeProvider>()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, ModelProviderHealthBackgroundService>());

        // Schema-ready gate. Background services that touch SQL wait for this
        // BEFORE their first query; this way, registration order (.UseMcp()
        // first or .UseSqlite() first) does not produce "no such table". When
        // no SQL provider is registered, the gate is open by itself.
        // Rationale: K-354.
        services.TryAddSingleton<SchemaReadyGate>();

        // Diagnostics collector (Phase 33; embedding points added Phase 85).
        // IAgentCatalog, IToolRegistry, and IAttachmentStorage are registered
        // after this point, but the explicit factory resolves lazily;
        // registration order does not matter.
        services.TryAddSingleton(static provider => new AgentPrismDiagnosticsCollector(
            provider.GetServices<IModelProvider>(),
            provider.GetRequiredService<ModelProviderHealthCache>(),
            provider.GetServices<ISqlPersistenceDiagnostics>(),
            provider.GetServices<SqlPersistenceRegistrationMarker>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetServices<IAgentSource>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IRunAttributionContext>(),
            provider.GetRequiredService<IToolAuthorizationHandler>(),
            provider.GetServices<IRunEventSink>(),
            provider.GetService<IAttachmentStorage>(),
            provider.GetService<ModelProviderCircuitBreaker>()));

        // Chat history provider. Without registration, MAF would set up its own
        // in-memory provider for each agent and that instance would not be
        // reachable from outside; /api/sessions/{id} history could only be
        // read while PostgreSQL was on. Explicit registration gives the same
        // read path in both modes. State lives in the session's StateBag, not
        // the provider's fields; that is why a single instance being shared
        // across all sessions is the usage MAF anticipates.
        // AgentPrism.PostgreSql replaces this with PostgresChatHistoryProvider.
        services.TryAddSingleton<Microsoft.Agents.AI.ChatHistoryProvider>(
            static _ => new Microsoft.Agents.AI.InMemoryChatHistoryProvider(
                new Microsoft.Agents.AI.InMemoryChatHistoryProviderOptions()));

        // File store for memory-backed AIContextProviders (FileMemoryProvider,
        // TextSearchProvider). MAF's own abstraction (not a file system); this
        // phase has no persistent version, it lives in memory. The persistent
        // version will connect to Phase 14+'s storage table.
        // MAAI001: AgentFileStore is marked "evaluation purposes only" - the
        // rationale is the same as in AgentDefinitionCompiler.
#pragma warning disable MAAI001
        services.TryAddSingleton<Microsoft.Agents.AI.AgentFileStore>(
            static _ => new Microsoft.Agents.AI.InMemoryAgentFileStore());
#pragma warning restore MAAI001

        // Compiler and cache.
        services.TryAddSingleton<CompiledAgentCache>();

        // Sub-agent resolver. Requests the catalog on FIRST USE, not in its
        // CONSTRUCTOR; otherwise the IAgentCatalog -> IAgentSource ->
        // AgentDefinitionCompiler -> resolver -> IAgentCatalog cycle could not be built.
        services.TryAddSingleton<CallableAgentResolver>();

#pragma warning disable MAAI001 // AgentFileStore — the rationale is the same as in AgentDefinitionCompiler.
        services.TryAddSingleton(static provider => new AgentDefinitionCompiler(
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>(),
            provider,
            // When not registered, MAF's in-memory default is used.
            // AgentPrism.PostgreSql fills this in with PostgresChatHistoryProvider.
            provider.GetService<Microsoft.Agents.AI.ChatHistoryProvider>(),
            provider.GetRequiredService<AgentSkillCatalog>(),
            // When not registered, there is no script support: no script can run.
            provider.GetService<SkillScriptSupport>(),
            provider.GetRequiredService<CallableAgentResolver>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IOptions<AgentPrismOptions>>().Value.UtilityModel,
            provider.GetRequiredService<Microsoft.Agents.AI.AgentFileStore>(),
            // When not registered, a definition using McpResourceUris gets a
            // compilation error. AgentPrism.Mcp's UseMcp() call registers this.
            provider.GetService<IMcpResourceContextProviderFactory>(),
            // Phase 51: when not registered, a definition requesting
            // EnableVectorSearch gets a compilation error.
            // AgentPrism.PostgreSql's UsePostgreSql() call registers this.
            provider.GetService<IVectorSearchStore>(),
            // The consumer registers their own IEmbeddingGenerator (the K-032 pattern).
            provider.GetService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>(),
            provider.GetRequiredService<IOptions<AgentPrismKnowledgeOptions>>().Value.MaxResults,
            // Resolves SharedInstructionsName. Registered unconditionally a few
            // lines below (the in-memory store by default), so this is never null
            // in practice - GetService, not GetRequiredService, only to avoid a
            // hard dependency order requirement between the two registrations.
            provider.GetService<IAgentDefinitionStore>()));
#pragma warning restore MAAI001

        // Knowledge base management surface (Phase 51): document upload,
        // search, delete, list. While IsSupported == false, every method
        // throws an overt AgentPrismException.
        services.TryAddSingleton(static provider => new KnowledgeIngestionService(
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IOptions<AgentPrismKnowledgeOptions>>(),
            provider.GetService<IVectorSearchStore>(),
            provider.GetService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>()));

        // Definition validation endpoint (Phase 34, F-60). Repeats the real
        // compilation path in its own order; IAgentCatalog can be taken directly
        // here because, unlike CallableAgentResolver, the validator is not part
        // of IAgentCatalog's OWN setup - it is a consumer added afterward, so
        // there is no risk of a circular dependency.
        services.TryAddSingleton(static provider => new AgentDefinitionValidator(
            provider.GetRequiredService<IModelProviderRegistry>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetRequiredService<AgentSkillCatalog>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<AgentDefinitionCompiler>(),
            provider.GetRequiredService<IOptions<AgentPrismOptions>>(),
            // When not registered, AgentPrism.Mcp is not in use; missing tool
            // names are reported directly as an error without attempting a fresh MCP scan.
            provider.GetService<IMcpToolRefresher>()));

        // Audit trail. The actor is read from AuditActorContext (AsyncLocal);
        // AgentPrism.AspNetCore writes HttpContext.User there at the start of
        // every protected request. This way Core can read the actor without
        // adding a dependency on ASP.NET Core.
        services.TryAddSingleton<IAuditLog, InMemoryAuditLog>();
        services.TryAddSingleton<IAuditActorResolver, AmbientAuditActorResolver>();

        // At-rest content protection (Phase 82). The default writes plaintext
        // unchanged; AddContentProtection(...) replaces it (K1's gate is the
        // registration itself, same pattern as IAuditLog above).
        services.TryAddSingleton<IContentProtector>(NullContentProtector.Instance);
    }

    private static void RegisterCatalogAssembly(IServiceCollection services)
    {
        // A/B experiments (Phase 19). Since it is an entity the admin
        // creates/starts/stops, it is wrapped with the audit trail decorator
        // for the same rationale as IAgentDefinitionStore (unlike
        // IEvalStore/IJobStore, which are a byproduct of execution).
        services.TryAddSingleton<IExperimentStore>(static provider => new AuditingExperimentStore(
            new InMemoryExperimentStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingExperimentStore>>()));
        services.TryAddSingleton<ExperimentAssignmentResolver>();

        services.TryAddSingleton<ITenantStore>(static provider => new AuditingTenantStore(
            new InMemoryTenantStore(),
            provider.GetRequiredService<IAuditLog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetRequiredService<IAuditActorResolver>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AuditingTenantStore>>()));

        // Telemetry. Metrics are set up through IMeterFactory when it is
        // registered; otherwise it creates its own Meter - the consumer is not
        // forced to call AddMetrics().
        services.TryAddSingleton(static provider => new AgentPrismMetrics(
            provider.GetService<System.Diagnostics.Metrics.IMeterFactory>()));

        // The span collector registers the ActivityListener in its
        // constructor; this is why resolving it once at the start is enough.
        // RunRecordingAgentDecorator resolves it.
        services.TryAddSingleton<RunTraceCollector>();

        // Code-defined approval policies (Phase 63, AddToolApprovalPolicy). Always
        // registered, even with no policies added — the registry is then empty and
        // every tool falls straight through to the data rules.
        services.TryAddSingleton<ToolApprovalPolicyRegistry>();

        // Service that evaluates approval rules.
        services.TryAddSingleton<ToolApprovalRuleEvaluator>();

        // Session lifecycle. Independent of the store.
        // An explicit factory is used: the built-in DI container does not fill
        // in constructor parameters that carry a default value, TimeProvider
        // may not be registered.
        services.TryAddSingleton(static provider => new AgentSessionManager(
            provider.GetRequiredService<ISessionStore>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetService<TimeProvider>()));

        // Conversation branching (Phase 47). IConversationBranchStore is
        // registered only while a SQL provider is on; without it, the service
        // says "not supported" and the endpoint returns 501 (see ConversationBranchService.IsSupported).
        services.TryAddSingleton(static provider => new ConversationBranchService(
            provider.GetRequiredService<ISessionStore>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<ITenantContext>(),
            provider.GetService<IConversationBranchStore>(),
            provider.GetService<TimeProvider>()));

        // Replay (Phase 47). Uses NOT the catalog but the definition store and
        // the compiler: model binding and tool modes require recompiling the
        // definition, and the result does NOT ENTER the cache.
        services.TryAddSingleton(static provider => new RunReplayService(
            provider.GetRequiredService<IRunStore>(),
            provider.GetRequiredService<IRunInputStore>(),
            provider.GetRequiredService<IAgentDefinitionStore>(),
            provider.GetRequiredService<IAgentCatalog>(),
            provider.GetRequiredService<AgentDefinitionCompiler>(),
            provider.GetRequiredService<IToolRegistry>(),
            provider.GetServices<IAgentDecorator>(),
            provider.GetRequiredService<ITenantContext>()));

        // Catalog sources. TryAddEnumerable prevents the same type from being added twice.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentSource, CodeAgentSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentSource, DefinitionStoreAgentSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AgentSourceValidationService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ToolRegistrationValidationService>());

        // Reports non-persistent storage in the Production environment. It reads
        // no database, so it is independent of migration order; it only looks at
        // which store implementations the container resolved.
        //
        // The explicit factory is REQUIRED, not a style choice: IHostEnvironment
        // is registered by the host, and AddAgentPrism() is also valid on a bare
        // service collection with no host behind it. With constructor resolution
        // the container treats the missing dependency as an error even though the
        // parameter is nullable, and enumerating IHostedService then throws for
        // every consumer - measured in phase 104.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NonPersistentStorageWarningService>(
            static provider => new NonPersistentStorageWarningService(
                provider.GetService<IHostEnvironment>(),
                provider.GetRequiredService<IAgentDefinitionStore>(),
                provider.GetRequiredService<IRunStore>(),
                provider.GetRequiredService<ISessionStore>(),
                provider.GetRequiredService<ILogger<NonPersistentStorageWarningService>>())));

        // Wrappers. Application order is determined by Order:
        // run recording (0) → telemetry (10) → tool approval (20) → agent.
        //
        // The recording decorator is set up with an explicit factory: the
        // built-in DI container does not fill in constructor parameters that
        // carry a default value, and TimeProvider may not be registered.
        //
        // The two-type-argument overload is REQUIRED: with the single-argument
        // form, the factory's return type becomes IAgentDecorator and
        // TryAddEnumerable cannot tell the implementations apart, producing
        // an "indistinguishable from other services" error.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, RunRecordingAgentDecorator>(
            static provider => new RunRecordingAgentDecorator(
                provider.GetRequiredService<IRunStore>(),
                provider.GetRequiredService<ITenantContext>(),
                provider.GetRequiredService<IOptions<AgentPrismOptions>>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RunRecordingAgent>>(),
                provider.GetRequiredService<AgentPrismMetrics>(),
                provider.GetRequiredService<RunTraceCollector>(),
                provider.GetService<TimeProvider>(),
                provider.GetRequiredService<IRunPricingResolver>(),
                // 🚨 Without these two lines, the constructor parameters stay
                // null and the quota counter never increases, no run.* event is
                // published - the build and tests would look green
                // (the lesson of K-157).
                provider.GetRequiredService<QuotaEnforcer>(),
                provider.GetRequiredService<IWebhookPublisher>(),
                provider.GetRequiredService<IRunCancellationRegistry>(),
                provider.GetRequiredService<IRunErrorClassifier>(),
                provider.GetRequiredService<IRunInputStore>(),
                // Phase 49: being registered alone samples nothing, see the
                // RunSampler/OnlineEvaluationOptions class documentation.
                provider.GetRequiredService<RunSampler>(),
                // HATA-S3-006: without this line, the input written to the
                // RunStarted event and IRunInputStore never passes through the guards.
                provider.GetRequiredService<ContentGuardPipeline>(),
                // 🚨 Phase 68, and the SAME trap the comment above describes:
                // without this line every run records a NULL user and NULL
                // labels while the build and the tests stay green.
                provider.GetRequiredService<IRunAttributionContext>(),
                // Phase 70. GetServices resolves lazily and never throws when
                // no IRunEventSink is registered — an empty sequence.
                provider.GetServices<IRunEventSink>())));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, OpenTelemetryAgentDecorator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentDecorator, ToolApprovalAgentDecorator>());

        services.TryAddSingleton<IAgentCatalog>(static provider => new CompositeAgentCatalog(
            provider.GetServices<IAgentSource>(),
            provider.GetServices<IAgentDecorator>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<CompositeAgentCatalog>>(),
            provider.GetRequiredService<AgentPrismMetrics>()));

        // Graceful-shutdown drain (Phase 87). Registered LAST among
        // IHostedService implementations: the generic host stops hosted
        // services in the REVERSE of their registration order, so this one's
        // StopAsync runs FIRST during shutdown -- before JobWorkerBackgroundService
        // and RunReconciliationService stop, while runs they started are
        // still in flight. Always registered (K-018); while
        // AgentPrismDrainOptions.Enabled is off (the default) IsDraining
        // never becomes true and StopAsync returns immediately (K1).
        services.TryAddSingleton<AgentPrismDrainService>();
        services.TryAddSingleton<IAgentPrismDrainState>(
            static provider => provider.GetRequiredService<AgentPrismDrainService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, AgentPrismDrainService>(
            static provider => provider.GetRequiredService<AgentPrismDrainService>()));

    }
}
