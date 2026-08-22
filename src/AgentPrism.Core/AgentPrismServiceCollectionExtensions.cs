using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>Extensions that register AgentPrism with dependency injection.</summary>
public static class AgentPrismServiceCollectionExtensions
{
    /// <summary>
    /// Adds AgentPrism to the host application builder and reads settings from
    /// the <c>AgentPrism</c> section.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The configuration chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The one call every AgentPrism application starts with. On its own it
    /// runs with in-memory stores and needs no database; a provider and a store
    /// are added to the chain it returns.
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    ///
    /// builder.AddAgentPrism()
    ///        .UseOpenAI(builder.Configuration["OpenAI:ApiKey"]!)
    ///        .UsePostgreSql(builder.Configuration.GetConnectionString("AgentPrism")!);
    /// </code>
    /// </example>
    /// </remarks>
    public static IAgentPrismBuilder AddAgentPrism(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Services.AddAgentPrism(builder.Configuration.GetSection(AgentPrismOptions.SectionName));
    }

    /// <summary>Adds AgentPrism to the service collection.</summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configurationSection">Configuration section settings are read from.</param>
    /// <returns>The configuration chain.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// All services are registered with <c>TryAdd</c>. If you register your own
    /// implementation <em>before</em> this call, yours wins; AgentPrism does not overwrite it.
    /// </para>
    /// <para>
    /// When no additional configuration is done, AgentPrism runs with
    /// in-memory stores and requires no database.
    /// </para>
    /// </remarks>
    public static IAgentPrismBuilder AddAgentPrism(
        this IServiceCollection services,
        IConfiguration? configurationSection = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AgentPrismOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            // Configuration is bound BY HAND. `optionsBuilder.Bind(section)` relies
            // on reflection and produces IL2026 + IL3050; the source generator
            // hides this during the build, but the diagnostics resurface in
            // `dotnet format`'s analyzer pass. Manual binding is clean on both
            // gates and removes a package dependency (Options.ConfigurationExtensions).
            // Rationale: docs/KARARLAR.md, decision K-021.
            services.Configure<AgentPrismOptions>(options => Bind(configurationSection, options));
        }

        // Batch and scheduled run (Phase 17). A separate section: it carries
        // its own SectionName like the PostgreSql package's
        // AgentPrismPostgreSqlOptions, but unlike AgentPrismOptions it is
        // always registered even without a separate Use...() call (K-018 -
        // stores are first-class).
        services.AddOptions<AgentPrismSchedulingOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<AgentPrismSchedulingOptions>(
                options => BindScheduling(configurationSection.GetSection("Scheduling"), options));
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismSchedulingOptions>, AgentPrismSchedulingOptionsValidator>());

        // Single-executor selection (Phase 42). Same rationale: carries its own
        // SectionName, requires no separate Use...() call. Default
        // Enabled=false; while disabled, no call reaches InMemorySingletonLeaseStore (K1).
        services.AddOptions<SingletonExecutionOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<SingletonExecutionOptions>(
                options => BindSingletonExecution(configurationSection.GetSection("SingletonExecution"), options));
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<SingletonExecutionOptions>, SingletonExecutionOptionsValidator>());
        services.TryAddSingleton<ISingletonLeaseStore, InMemorySingletonLeaseStore>();

        // Quotas and event publishing (Phase 21). Same rationale as
        // scheduling: carries its own SectionName and requires no separate
        // Use...() call.
        services.AddOptions<AgentPrismQuotaOptions>().ValidateOnStart();
        services.AddOptions<AgentPrismWebhookOptions>().ValidateOnStart();
        services.AddOptions<AgentPrismRateLimitOptions>().ValidateOnStart();

        // Data retention and archiving (Phase 25). Same rationale: carries its
        // own SectionName, requires no separate Use...() call.
        services.AddOptions<AgentPrismRetentionOptions>().ValidateOnStart();

        // Idempotency-Key support (Phase 43). Same rationale: carries its own
        // SectionName, requires no separate Use...() call.
        services.AddOptions<AgentPrismIdempotencyOptions>().ValidateOnStart();

        // Queued (durable) run (Phase 46). Same rationale: carries its own
        // SectionName, requires no separate Use...() call.
        services.AddOptions<AgentPrismAsyncRunOptions>().ValidateOnStart();

        // Orphaned run reconciliation (Phase 54). Same rationale: carries its
        // own SectionName, requires no separate Use...() call. Default
        // Enabled=false; while disabled, none of the heartbeat/reconciliation
        // queries are issued (K1).
        services.AddOptions<RunReconciliationOptions>().ValidateOnStart();

        // Async approval inbox (Phase 55). Same rationale: carries its own
        // SectionName, requires no separate Use...() call.
        services.AddOptions<AgentPrismApprovalOptions>().ValidateOnStart();

        // Knowledge base / semantic search (Phase 51). Same rationale: carries
        // its own SectionName, requires no separate Use...() call. Becomes
        // functional only when an IVectorSearchStore (today only PostgreSQL)
        // AND an IEmbeddingGenerator are both registered together
        // (KnowledgeIngestionService.IsSupported).
        services.AddOptions<AgentPrismKnowledgeOptions>().ValidateOnStart();

        // Content moderation (Phase 48). Settings are always registered, but
        // when no IContentGuard is registered they are never read at all: the
        // moderation wrapper is not added to the pipeline. K1's gate is the
        // registration itself, not a flag.
        services.AddOptions<AgentPrismContentGuardOptions>().ValidateOnStart();
        services.AddOptions<PatternContentGuardOptions>().ValidateOnStart();

        // Online evaluation (Phase 49). Same rationale: carries its own
        // SectionName, requires no separate Use...() call. The two-gate
        // default (Enabled=false AND SampleRate=0) ensures the judge model is
        // NEVER called - see the OnlineEvaluationOptions class documentation.
        services.AddOptions<OnlineEvaluationOptions>().ValidateOnStart();

        // Canary rollout and automatic rollback (Phase 56). Same rationale:
        // carries its own SectionName, requires no separate Use...() call.
        // AutoRollbackEnabled defaults to false (K1) - no experiment stops on
        // its own unless turned on.
        services.AddOptions<CanaryOptions>().ValidateOnStart();

        // Tenant provider bindings / BYOK (Phase 65). Same rationale: carries
        // its own section, requires no separate Use...() call. The default
        // prefix is restrictive on its own (section 65.2); no separate
        // Enabled flag is needed.
        services.AddOptions<AgentPrismTenantProviderOptions>().ValidateOnStart();

        // Inbound triggers (Phase 66). Same rationale as tenant providers:
        // carries its own section, requires no separate Use...() call. The
        // default prefix is restrictive on its own (section 66.2).
        services.AddOptions<AgentPrismInboundTriggerOptions>().ValidateOnStart();

        // Outbound network guard (Phase 77). Governs all three outbound
        // surfaces: webhook delivery, MCP connections and model provider
        // calls. AllowPrivateNetworkTargets defaults to false, so the guard
        // arrives open and the private network arrives closed.
        services.AddOptions<AgentPrismEgressOptions>().ValidateOnStart();

        // The prefix that bounds which configuration key an MCP server
        // definition may name (Phase 77). Lives in the abstractions package
        // because the saving endpoint and the connecting transport are in two
        // packages that do not see each other.
        services.AddOptions<AgentPrismMcpSecurityOptions>().ValidateOnStart();

        // At-rest content protection (Phase 82). Same rationale: carries its
        // own section, requires no separate Use...() call. Enabled defaults
        // to false (K1); the default IContentProtector below writes plaintext
        // unchanged until AddContentProtection(...) replaces it.
        services.AddOptions<AgentPrismContentProtectionOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<AgentPrismQuotaOptions>(
                options => BindQuotas(configurationSection.GetSection("Quotas"), options));
            services.Configure<AgentPrismWebhookOptions>(
                options => BindWebhooks(configurationSection.GetSection("Webhooks"), options));
            services.Configure<AgentPrismRateLimitOptions>(
                options => BindRateLimit(configurationSection.GetSection("RateLimit"), options));
            services.Configure<AgentPrismRetentionOptions>(
                options => BindRetention(configurationSection.GetSection("Retention"), options));
            services.Configure<AgentPrismIdempotencyOptions>(
                options => BindIdempotency(configurationSection.GetSection("Idempotency"), options));
            services.Configure<AgentPrismAsyncRunOptions>(
                options => BindAsyncRun(configurationSection.GetSection("AsyncRun"), options));
            services.Configure<RunReconciliationOptions>(
                options => BindRunReconciliation(configurationSection.GetSection("RunReconciliation"), options));
            services.Configure<AgentPrismApprovalOptions>(
                options => BindApproval(configurationSection.GetSection("Approvals"), options));
            services.Configure<OnlineEvaluationOptions>(
                options => BindOnlineEvaluation(configurationSection.GetSection("OnlineEvaluation"), options));
            services.Configure<CanaryOptions>(
                options => BindCanary(configurationSection.GetSection("Canary"), options));
            services.Configure<AgentPrismTenantProviderOptions>(
                options => BindTenantProviders(configurationSection.GetSection("TenantProviders"), options));
            services.Configure<AgentPrismInboundTriggerOptions>(
                options => BindInboundTriggers(configurationSection.GetSection("InboundTriggers"), options));
            services.Configure<AgentPrismEgressOptions>(
                options => BindEgress(configurationSection.GetSection("Egress"), options));
            services.Configure<AgentPrismMcpSecurityOptions>(
                options => BindMcpSecurity(configurationSection.GetSection("Mcp"), options));
            services.Configure<AgentPrismContentProtectionOptions>(
                options => BindContentProtection(configurationSection.GetSection("ContentProtection"), options));

            var contentGuardSection = configurationSection.GetSection("ContentGuard");

            services.Configure<AgentPrismContentGuardOptions>(
                options => BindContentGuard(contentGuardSection, options));

            var patternSection = contentGuardSection.GetSection("Pattern");

            services.Configure<PatternContentGuardOptions>(
                options => BindPatternContentGuard(patternSection, options));

            // 🚨 The built-in guard is registered only when the section REALLY
            // exists. The registration is K1's gate: without it, the moderation
            // wrapper is never added to the pipeline and the cost stays exactly
            // zero. The way to turn it on from code is the AddPatternContentGuard() call.
            if (patternSection.Exists())
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IContentGuard, PatternContentGuard>());
            }
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismQuotaOptions>, AgentPrismQuotaOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismWebhookOptions>, AgentPrismWebhookOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismRetentionOptions>, AgentPrismRetentionOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OnlineEvaluationOptions>, OnlineEvaluationOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RunReconciliationOptions>, RunReconciliationOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<AgentPrismContentProtectionOptions>, AgentPrismContentProtectionOptionsValidator>());

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
        services.TryAddSingleton<IToolRegistry, ToolRegistry>();

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
            provider.GetService<AgentPrismMetrics>()));

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

        services.TryAddSingleton<IAgentCatalog, CompositeAgentCatalog>();

        return new AgentPrismBuilder(services);
    }

    /// <summary>
    /// Adds an <see cref="IJobHandler"/> extension point.
    /// </summary>
    /// <typeparam name="THandler">The handler type to add.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Evaluation registers its own handler with this method.
    /// <c>TryAddEnumerable</c> is used: if the same type is added twice, only
    /// the first counts.
    /// <example>
    /// <code>
    /// builder.Services.AddJobHandler&lt;NightlyReportJobHandler&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddJobHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services)
        where THandler : class, IJobHandler
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHandler, THandler>());

        return services;
    }

    /// <summary>
    /// Sets batch and scheduled run settings from code.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configure">Settings modifier. When not given, only the defaults/configuration apply.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// The job queue and scheduling stores are already registered by
    /// <c>AddAgentPrism()</c>; this method only changes the settings (e.g.
    /// turning off this process's background worker with
    /// <c>o.RunWorker = false</c>). <c>PostConfigure</c> is used, so the value
    /// given from code always wins over the value coming from the configuration file.
    /// <example>
    /// <code>
    /// // A web instance that serves requests and leaves the jobs to a worker process.
    /// builder.Services.UseScheduling(o => o.RunWorker = false);
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection UseScheduling(
        this IServiceCollection services,
        Action<AgentPrismSchedulingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.PostConfigure(configure);
        }

        return services;
    }

    /// <summary>
    /// Manually binds a configuration section to the settings object.
    /// </summary>
    /// <remarks>
    /// This method must also be extended when a new setting is added. In
    /// exchange, AgentPrism.Core stays reflection-free and AOT-compatible.
    /// </remarks>
    private static void Bind(IConfiguration section, AgentPrismOptions options)
    {
        if (section[nameof(AgentPrismOptions.DefaultTenantId)] is { Length: > 0 } tenantId)
        {
            options.DefaultTenantId = tenantId;
        }

        if (int.TryParse(
                section[nameof(AgentPrismOptions.MaxParameterValueLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxParameterValueLength))
        {
            options.MaxParameterValueLength = maxParameterValueLength;
        }

        // Every sub-section is responsible for ITS OWN existence check. An
        // early return causes later sections to never be read at all when the
        // first one is undefined; measured: Observability was being silently
        // ignored in a configuration where RunRecording was not written.
        BindRunRecording(section.GetSection(nameof(AgentPrismOptions.RunRecording)), options.RunRecording);
        BindObservability(section.GetSection(nameof(AgentPrismOptions.Observability)), options.Observability);
        BindCircuitBreaker(section.GetSection(nameof(AgentPrismOptions.CircuitBreaker)), options.CircuitBreaker);
        BindHealth(section.GetSection(nameof(AgentPrismOptions.Health)), options.Health);
        BindAudit(section.GetSection(nameof(AgentPrismOptions.Audit)), options.Audit);
        BindSkills(section.GetSection(nameof(AgentPrismOptions.Skills)), options.Skills);
        BindAgentGraph(section.GetSection(nameof(AgentPrismOptions.AgentGraph)), options.AgentGraph);
        BindAttachments(section.GetSection(nameof(AgentPrismOptions.Attachments)), options.Attachments);
        BindPricing(section.GetSection(nameof(AgentPrismOptions.Pricing)), options.Pricing);
        options.UtilityModel = BindUtilityModel(section.GetSection(nameof(AgentPrismOptions.UtilityModel)));
        BindValidation(section.GetSection(nameof(AgentPrismOptions.Validation)), options.Validation);
        BindPreflight(section.GetSection(nameof(AgentPrismOptions.Preflight)), options.Preflight);
        BindModelConcurrency(section.GetSection(nameof(AgentPrismOptions.ModelConcurrency)), options.ModelConcurrency);
        BindTools(section.GetSection(nameof(AgentPrismOptions.Tools)), options.Tools);
    }

    /// <summary>Binds the <c>AgentPrism:Tools</c> section.</summary>
    private static void BindTools(IConfigurationSection section, AgentPrismToolOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismToolOptions.DefaultTimeout)],
                CultureInfo.InvariantCulture,
                out var defaultTimeout))
        {
            options.DefaultTimeout = defaultTimeout;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Preflight</c> section.</summary>
    private static void BindPreflight(IConfigurationSection section, AgentPrismPreflightOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismPreflightOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (double.TryParse(
                section[nameof(AgentPrismPreflightOptions.ReserveRatio)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var reserveRatio))
        {
            options.ReserveRatio = reserveRatio;
        }
    }

    /// <summary>Binds the <c>AgentPrism:ModelConcurrency</c> section.</summary>
    private static void BindModelConcurrency(IConfigurationSection section, AgentPrismModelConcurrencyOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(AgentPrismModelConcurrencyOptions.MaxConcurrentCallsPerProvider)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxConcurrentCallsPerProvider))
        {
            options.MaxConcurrentCallsPerProvider = maxConcurrentCallsPerProvider;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Validation</c> section.</summary>
    private static void BindValidation(IConfigurationSection section, AgentPrismValidationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismValidationOptions.McpTimeout)],
                CultureInfo.InvariantCulture,
                out var mcpTimeout))
        {
            options.McpTimeout = mcpTimeout;
        }
    }

    /// <summary>
    /// Binds the <c>AgentPrism:Pricing</c> section. The <c>Currency</c> and
    /// <c>Voice</c> keys are reserved; every other child is read as a provider name.
    /// </summary>
    private static void BindPricing(IConfigurationSection section, AgentPrismPricingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismPricingOptions.Currency)] is { Length: > 0 } currency)
        {
            options.Currency = currency;
        }

        BindVoicePricing(section.GetSection(nameof(AgentPrismPricingOptions.Voice)), options);

        foreach (var providerSection in section.GetChildren())
        {
            // Reserved keys. Since the section is bound by hand, this list is
            // the SINGLE source of truth; a forgotten key is mistaken for a provider name.
            if (string.Equals(
                    providerSection.Key,
                    nameof(AgentPrismPricingOptions.Currency),
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    providerSection.Key,
                    nameof(AgentPrismPricingOptions.Voice),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var models = new Dictionary<string, ModelPriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var input = ReadDecimal(modelSection, "Input");
                var output = ReadDecimal(modelSection, "Output");

                // "CachedInput" is OPTIONAL and, unlike Input/Output, its absence
                // is NOT a configuration fault: a model without a cache rate keeps
                // pricing its cached tokens at the plain input rate. It therefore
                // does NOT take part in the "at least one value" check below.
                var cachedInput = ReadDecimal(modelSection, "CachedInput");

                // 🚨 The record is ADDED even when both are null (K-034): a
                // price entry written with a key name other than
                // "Input"/"Output" (e.g. the C# property name
                // "InputCostPerMillionTokens") therefore does NOT get dropped
                // COMPLETELY SILENTLY - it still enters Providers as an empty
                // ModelPriceOverride with both fields null, and
                // AgentPrismOptionsValidator.ValidatePricing rejects it at
                // startup (MT-CORE-065).
                models[modelSection.Key] = new ModelPriceOverride
                {
                    InputCostPerMillionTokens = input,
                    OutputCostPerMillionTokens = output,
                    CachedInputCostPerMillionTokens = cachedInput,
                };
            }

            if (models.Count > 0)
            {
                options.Providers[providerSection.Key] = models;
            }
        }
    }

    /// <summary>
    /// Binds the <c>AgentPrism:Pricing:Voice</c> section.
    /// </summary>
    /// <remarks>
    /// Voice pricing is based on characters (generation) or duration
    /// (resolution), not tokens; this is why it is written to a separate
    /// dictionary and not summed together with token prices.
    /// </remarks>
    private static void BindVoicePricing(IConfigurationSection section, AgentPrismPricingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        foreach (var providerSection in section.GetChildren())
        {
            var models = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase);

            foreach (var modelSection in providerSection.GetChildren())
            {
                var perMillionCharacters = ReadDecimal(
                    modelSection,
                    nameof(VoicePriceOverride.PerMillionCharacters));

                var perMinute = ReadDecimal(modelSection, nameof(VoicePriceOverride.PerMinute));

                // 🚨 Same rationale: see the comment inside BindPricing (MT-CORE-065).
                models[modelSection.Key] = new VoicePriceOverride
                {
                    PerMillionCharacters = perMillionCharacters,
                    PerMinute = perMinute,
                };
            }

            if (models.Count > 0)
            {
                options.Voice[providerSection.Key] = models;
            }
        }
    }

    private static decimal? ReadDecimal(IConfiguration section, string key)
        => decimal.TryParse(section[key], NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>
    /// Reads the utility model binding from configuration.
    /// </summary>
    /// <remarks>
    /// <see cref="ModelBinding.Provider"/> and <see cref="ModelBinding.Model"/>
    /// are required; when both are not set, no binding is built. A partially
    /// filled binding would silently accept a configuration that was
    /// mistakenly written incomplete.
    /// </remarks>
    private static ModelBinding? BindUtilityModel(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        var provider = section[nameof(ModelBinding.Provider)];
        var model = section[nameof(ModelBinding.Model)];

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        float.TryParse(section[nameof(ModelBinding.Temperature)], NumberStyles.Float, CultureInfo.InvariantCulture, out var temperature);
        int.TryParse(section[nameof(ModelBinding.MaxOutputTokens)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOutputTokens);
        float.TryParse(section[nameof(ModelBinding.TopP)], NumberStyles.Float, CultureInfo.InvariantCulture, out var topP);

        return new ModelBinding
        {
            Provider = provider,
            Model = model,
            Temperature = section[nameof(ModelBinding.Temperature)] is { Length: > 0 } ? temperature : null,
            MaxOutputTokens = section[nameof(ModelBinding.MaxOutputTokens)] is { Length: > 0 } ? maxOutputTokens : null,
            TopP = section[nameof(ModelBinding.TopP)] is { Length: > 0 } ? topP : null,
            ReasoningEffort = section[nameof(ModelBinding.ReasoningEffort)],
        };
    }

    private static void BindAgentGraph(IConfigurationSection section, AgentPrismAgentGraphOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(AgentPrismAgentGraphOptions.MaxDepth)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxDepth))
        {
            options.MaxDepth = maxDepth;
        }

        if (long.TryParse(
                section[nameof(AgentPrismAgentGraphOptions.MaxTotalTokens)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxTokens))
        {
            options.MaxTotalTokens = maxTokens;
        }

        if (int.TryParse(
                section[nameof(AgentPrismAgentGraphOptions.MaxTotalRuns)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRuns))
        {
            options.MaxTotalRuns = maxRuns;
        }
    }

    private static void BindAttachments(IConfigurationSection section, AgentPrismAttachmentOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (long.TryParse(
                section[nameof(AgentPrismAttachmentOptions.MaxBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxBytes))
        {
            options.MaxBytes = maxBytes;
        }

        var allowed = section.GetSection(nameof(AgentPrismAttachmentOptions.AllowedMediaTypes))
            .GetChildren()
            .Select(static child => child.Value)
            .Where(static value => value is { Length: > 0 })
            .ToArray();

        if (allowed.Length > 0)
        {
            options.AllowedMediaTypes.Clear();

            foreach (var mediaType in allowed)
            {
                options.AllowedMediaTypes.Add(mediaType!);
            }
        }
    }

    private static void BindAudit(IConfigurationSection section, AgentPrismAuditOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismAuditOptions.ActorClaimType)] is { Length: > 0 } claimType)
        {
            options.ActorClaimType = claimType;
        }
    }

    private static void BindSkills(IConfigurationSection section, AgentPrismSkillOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxSkillsPerAgent)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSkills))
        {
            options.MaxSkillsPerAgent = maxSkills;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxInstructionsLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxInstructions))
        {
            options.MaxInstructionsLength = maxInstructions;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxResourceContentLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResourceContent))
        {
            options.MaxResourceContentLength = maxResourceContent;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillOptions.MaxResourcesPerSkill)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResources))
        {
            options.MaxResourcesPerSkill = maxResources;
        }

        BindSkillScripts(section.GetSection(nameof(AgentPrismSkillOptions.Scripts)), options.Scripts);
    }

    /// <summary>Binds script run settings from configuration.</summary>
    /// <remarks>
    /// <c>Enabled</c> and <c>PlatformIsolationAcknowledged</c> can deliberately
    /// also be read from here: a single deployment must be able to run script
    /// support on or off in different environments from the same image.
    /// Validation still requires both together; turning on only <c>Enabled</c> fails at startup.
    /// </remarks>
    private static void BindSkillScripts(IConfigurationSection section, AgentPrismSkillScriptOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismSkillScriptOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismSkillScriptOptions.PlatformIsolationAcknowledged), out var acknowledged))
        {
            options.PlatformIsolationAcknowledged = acknowledged;
        }

        // 🚨 AllowStoredScripts, SkillRoots and Interpreters are deliberately NOT
        // bound from configuration. All three widen what may be executed on the
        // server, and the shipped documentation states that script execution "can
        // only be turned on in code". Binding merged INTO the code-supplied values
        // instead of replacing them, so an environment variable such as
        // AgentPrism__Skills__Scripts__Interpreters__sh=/bin/sh added an
        // interpreter the application never approved, and K-087 had already
        // rejected configuration-supplied skill roots as arbitrary file system
        // reads. Enabled and PlatformIsolationAcknowledged stay bound on purpose
        // (see the comment above): they can only NARROW or acknowledge, never
        // widen the executable surface.

        if (TimeSpan.TryParse(section[nameof(AgentPrismSkillScriptOptions.Timeout)], CultureInfo.InvariantCulture, out var timeout))
        {
            options.Timeout = timeout;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxOutputBytes)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOutput))
        {
            options.MaxOutputBytes = maxOutput;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxArgumentBytes)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxArguments))
        {
            options.MaxArgumentBytes = maxArguments;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxScriptContentLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxContent))
        {
            options.MaxScriptContentLength = maxContent;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxScriptsPerSkill)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxScripts))
        {
            options.MaxScriptsPerSkill = maxScripts;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxConcurrentPerTenant)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var perTenant))
        {
            options.MaxConcurrentPerTenant = perTenant;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.MaxConcurrentTotal)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
        {
            options.MaxConcurrentTotal = total;
        }

        if (int.TryParse(section[nameof(AgentPrismSkillScriptOptions.SearchDepth)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth))
        {
            options.SearchDepth = depth;
        }

        BindList(section.GetSection(nameof(AgentPrismSkillScriptOptions.EnvironmentAllowList)), options.EnvironmentAllowList);
    }

    /// <summary>Writes a configuration array into an existing list.</summary>
    /// <remarks>
    /// When the list is defined in configuration, the default content is
    /// <strong>fully</strong> replaced. If a merge were done instead, it would
    /// be impossible to narrow the environment variable allow-list.
    /// </remarks>
    private static void BindList(IConfigurationSection section, IList<string> target)
    {
        if (!section.Exists())
        {
            return;
        }

        var values = section.GetChildren()
            .Select(static child => child.Value)
            .Where(static value => value is { Length: > 0 })
            .ToArray();

        if (values.Length == 0)
        {
            return;
        }

        target.Clear();

        foreach (var value in values)
        {
            target.Add(value!);
        }
    }

    private static void BindCircuitBreaker(IConfigurationSection section, AgentPrismCircuitBreakerOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismCircuitBreakerOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismCircuitBreakerOptions.FailureThreshold)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var failureThreshold))
        {
            options.FailureThreshold = failureThreshold;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismCircuitBreakerOptions.BreakDuration)],
                CultureInfo.InvariantCulture,
                out var breakDuration))
        {
            options.BreakDuration = breakDuration;
        }
    }

    /// <summary>Binds scheduling settings from configuration.</summary>
    private static void BindScheduling(IConfigurationSection section, AgentPrismSchedulingOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismSchedulingOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismSchedulingOptions.RunWorker), out var runWorker))
        {
            options.RunWorker = runWorker;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxConcurrentJobs)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxConcurrentJobs))
        {
            options.MaxConcurrentJobs = maxConcurrentJobs;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismSchedulingOptions.PollInterval)],
                CultureInfo.InvariantCulture,
                out var pollInterval))
        {
            options.PollInterval = pollInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismSchedulingOptions.LeaseDuration)],
                CultureInfo.InvariantCulture,
                out var leaseDuration))
        {
            options.LeaseDuration = leaseDuration;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }

        if (int.TryParse(
                section[nameof(AgentPrismSchedulingOptions.MaxItemsPerJob)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxItemsPerJob))
        {
            options.MaxItemsPerJob = maxItemsPerJob;
        }
    }

    /// <summary>Binds single-executor selection settings from configuration.</summary>
    private static void BindSingletonExecution(IConfigurationSection section, SingletonExecutionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(SingletonExecutionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(SingletonExecutionOptions.LeaseDuration)],
                CultureInfo.InvariantCulture,
                out var leaseDuration))
        {
            options.LeaseDuration = leaseDuration;
        }

        if (section[nameof(SingletonExecutionOptions.OwnerId)] is { Length: > 0 } ownerId)
        {
            options.OwnerId = ownerId;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Quotas</c> section.</summary>
    private static void BindQuotas(IConfigurationSection section, AgentPrismQuotaOptions options)
    {
        // Every sub-section is responsible for ITS OWN existence check: an
        // early return silently swallows later sections (see docs/hafiza/cekirdek-calistirma.md).
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismQuotaOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (section[nameof(AgentPrismQuotaOptions.TimeZone)] is { Length: > 0 } timeZone)
        {
            options.TimeZone = timeZone;
        }

        if (TryReadBool(section, nameof(AgentPrismQuotaOptions.AllowOnStoreFailure), out var allowOnFailure))
        {
            options.AllowOnStoreFailure = allowOnFailure;
        }

        var thresholds = section.GetSection(nameof(AgentPrismQuotaOptions.ThresholdPercents));

        if (thresholds.Exists())
        {
            var parsed = thresholds.GetChildren()
                .Select(child => int.TryParse(child.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) ? percent : -1)
                .Where(percent => percent > 0)
                .ToList();

            if (parsed.Count > 0)
            {
                options.ThresholdPercents.Clear();

                foreach (var percent in parsed)
                {
                    options.ThresholdPercents.Add(percent);
                }
            }
        }
    }

    /// <summary>Binds the <c>AgentPrism:RateLimit</c> section.</summary>
    private static void BindRateLimit(IConfigurationSection section, AgentPrismRateLimitOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismRateLimitOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRateLimitOptions.PermitLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var permitLimit))
        {
            options.PermitLimit = permitLimit;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismRateLimitOptions.Window)],
                CultureInfo.InvariantCulture,
                out var window))
        {
            options.Window = window;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRateLimitOptions.QueueLimit)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var queueLimit))
        {
            options.QueueLimit = queueLimit;
        }

        if (Enum.TryParse<RateLimitPartitionKind>(
                section[nameof(AgentPrismRateLimitOptions.Partition)],
                ignoreCase: true,
                out var partition))
        {
            options.Partition = partition;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Idempotency</c> section.</summary>
    private static void BindIdempotency(IConfigurationSection section, AgentPrismIdempotencyOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismIdempotencyOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismIdempotencyOptions.MaxKeyLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxKeyLength))
        {
            options.MaxKeyLength = maxKeyLength;
        }
    }

    /// <summary>Binds the <c>AgentPrism:AsyncRun</c> section.</summary>
    private static void BindAsyncRun(IConfigurationSection section, AgentPrismAsyncRunOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismAsyncRunOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismAsyncRunOptions.MaxAttempts)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAttempts))
        {
            options.MaxAttempts = maxAttempts;
        }
    }

    /// <summary>Binds the <c>AgentPrism:RunReconciliation</c> section.</summary>
    private static void BindRunReconciliation(IConfigurationSection section, RunReconciliationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(RunReconciliationOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(RunReconciliationOptions.HeartbeatInterval)],
                CultureInfo.InvariantCulture,
                out var heartbeatInterval))
        {
            options.HeartbeatInterval = heartbeatInterval;
        }

        if (TimeSpan.TryParse(
                section[nameof(RunReconciliationOptions.OrphanThreshold)],
                CultureInfo.InvariantCulture,
                out var orphanThreshold))
        {
            options.OrphanThreshold = orphanThreshold;
        }

        if (TimeSpan.TryParse(
                section[nameof(RunReconciliationOptions.ScanInterval)],
                CultureInfo.InvariantCulture,
                out var scanInterval))
        {
            options.ScanInterval = scanInterval;
        }

        if (int.TryParse(
                section[nameof(RunReconciliationOptions.MaxRunsPerScan)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRunsPerScan))
        {
            options.MaxRunsPerScan = maxRunsPerScan;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Approvals</c> section.</summary>
    private static void BindApproval(IConfigurationSection section, AgentPrismApprovalOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismApprovalOptions.DefaultExpiration)],
                CultureInfo.InvariantCulture,
                out var defaultExpiration))
        {
            options.DefaultExpiration = defaultExpiration;
        }

        if (TryReadBool(section, nameof(AgentPrismApprovalOptions.ExpirationEnabled), out var expirationEnabled))
        {
            options.ExpirationEnabled = expirationEnabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismApprovalOptions.ScanInterval)],
                CultureInfo.InvariantCulture,
                out var scanInterval))
        {
            options.ScanInterval = scanInterval;
        }

        if (int.TryParse(
                section[nameof(AgentPrismApprovalOptions.MaxPerScan)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxPerScan))
        {
            options.MaxPerScan = maxPerScan;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Canary</c> section.</summary>
    private static void BindCanary(IConfigurationSection section, CanaryOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(CanaryOptions.AutoRollbackEnabled), out var autoRollbackEnabled))
        {
            options.AutoRollbackEnabled = autoRollbackEnabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(CanaryOptions.ScanInterval)],
                CultureInfo.InvariantCulture,
                out var scanInterval))
        {
            options.ScanInterval = scanInterval;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Egress</c> section.</summary>
    private static void BindEgress(IConfigurationSection section, AgentPrismEgressOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismEgressOptions.AllowPrivateNetworkTargets), out var allowPrivate))
        {
            options.AllowPrivateNetworkTargets = allowPrivate;
        }
    }

    private static void BindMcpSecurity(IConfigurationSection section, AgentPrismMcpSecurityOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismMcpSecurityOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }
    }

    /// <summary>Binds the <c>AgentPrism:TenantProviders</c> section.</summary>
    private static void BindTenantProviders(IConfigurationSection section, AgentPrismTenantProviderOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismTenantProviderOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }
    }

    /// <summary>Binds the <c>AgentPrism:InboundTriggers</c> section.</summary>
    private static void BindInboundTriggers(IConfigurationSection section, AgentPrismInboundTriggerOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismInboundTriggerOptions.TimestampTolerance)],
                CultureInfo.InvariantCulture,
                out var timestampTolerance))
        {
            options.TimestampTolerance = timestampTolerance;
        }

        if (int.TryParse(
                section[nameof(AgentPrismInboundTriggerOptions.MaxBodyBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxBodyBytes))
        {
            options.MaxBodyBytes = maxBodyBytes;
        }

        if (int.TryParse(
                section[nameof(AgentPrismInboundTriggerOptions.MaxRequestsPerMinute)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRequestsPerMinute))
        {
            options.MaxRequestsPerMinute = maxRequestsPerMinute;
        }

        if (section[nameof(AgentPrismInboundTriggerOptions.AllowedConfigurationPrefix)] is { Length: > 0 } allowedPrefix)
        {
            options.AllowedConfigurationPrefix = allowedPrefix;
        }
    }

    /// <summary>Binds the <c>AgentPrism:ContentGuard</c> section.</summary>
    private static void BindContentGuard(IConfigurationSection section, AgentPrismContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.InspectInput), out var inspectInput))
        {
            options.InspectInput = inspectInput;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.InspectOutput), out var inspectOutput))
        {
            options.InspectOutput = inspectOutput;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.BufferStreamingOutput), out var buffer))
        {
            options.BufferStreamingOutput = buffer;
        }
    }

    /// <summary>Binds the <c>AgentPrism:ContentGuard:Pattern</c> section.</summary>
    /// <remarks>
    /// <see cref="PiiPatterns"/> is a <c>[Flags]</c> enum, written in
    /// configuration as a comma-separated name list (example:
    /// <c>"Email,CreditCard"</c>). <c>Enum.TryParse</c> is AOT-clean.
    /// </remarks>
    private static void BindPatternContentGuard(IConfigurationSection section, PatternContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        BindList(section.GetSection(nameof(PatternContentGuardOptions.DeniedTerms)), options.DeniedTerms);

        if (Enum.TryParse<PiiPatterns>(section[nameof(PatternContentGuardOptions.MaskedPii)], ignoreCase: true, out var pii))
        {
            options.MaskedPii = pii;
        }

        if (section[nameof(PatternContentGuardOptions.MaskReplacement)] is { Length: > 0 } replacement)
        {
            options.MaskReplacement = replacement;
        }
    }

    /// <summary>Binds the <c>AgentPrism:ContentProtection</c> section.</summary>
    /// <remarks>
    /// <see cref="ProtectedColumn"/> values are written in configuration as a
    /// list of names (example: <c>Columns:0 = "RunInput"</c>). <c>Enum.TryParse</c>
    /// is AOT-clean; an unrecognized name is skipped rather than failing the
    /// whole bind, matching <see cref="BindList"/>'s tolerance elsewhere.
    /// </remarks>
    private static void BindContentProtection(IConfigurationSection section, AgentPrismContentProtectionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismContentProtectionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (section[nameof(AgentPrismContentProtectionOptions.ActiveKeyId)] is { Length: > 0 } activeKeyId)
        {
            options.ActiveKeyId = activeKeyId;
        }

        var keysSection = section.GetSection(nameof(AgentPrismContentProtectionOptions.Keys));

        if (keysSection.Exists())
        {
            foreach (var child in keysSection.GetChildren())
            {
                if (child.Value is { Length: > 0 } configurationKeyName)
                {
                    options.Keys[child.Key] = configurationKeyName;
                }
            }
        }

        var columnsSection = section.GetSection(nameof(AgentPrismContentProtectionOptions.Columns));

        if (columnsSection.Exists())
        {
            var parsed = new HashSet<ProtectedColumn>();

            foreach (var child in columnsSection.GetChildren())
            {
                if (child.Value is { Length: > 0 } value && Enum.TryParse<ProtectedColumn>(value, ignoreCase: true, out var column))
                {
                    parsed.Add(column);
                }
            }

            if (parsed.Count > 0)
            {
                options.Columns.Clear();

                foreach (var column in parsed)
                {
                    options.Columns.Add(column);
                }
            }
        }
    }

    /// <summary>Binds the <c>AgentPrism:Webhooks</c> section.</summary>
    private static void BindWebhooks(IConfigurationSection section, AgentPrismWebhookOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.AllowPrivateNetworkTargets), out var allowPrivate))
        {
            options.AllowPrivateNetworkTargets = allowPrivate;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.AllowInsecureHttp), out var allowHttp))
        {
            options.AllowInsecureHttp = allowHttp;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismWebhookOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismWebhookOptions.SignatureTolerance)],
                CultureInfo.InvariantCulture,
                out var tolerance))
        {
            options.SignatureTolerance = tolerance;
        }

        if (section[nameof(AgentPrismWebhookOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.MaxExtraHeaders)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxExtraHeaders))
        {
            options.MaxExtraHeaders = maxExtraHeaders;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.MaxResponseBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResponseBytes))
        {
            options.MaxResponseBytes = maxResponseBytes;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.DisableAfterConsecutiveFailures)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var disableAfter))
        {
            options.DisableAfterConsecutiveFailures = disableAfter;
        }

        var delays = section.GetSection(nameof(AgentPrismWebhookOptions.RetryDelays));

        if (delays.Exists())
        {
            var parsed = delays.GetChildren()
                .Select(child => TimeSpan.TryParse(child.Value, CultureInfo.InvariantCulture, out var delay) ? delay : TimeSpan.Zero)
                .Where(delay => delay > TimeSpan.Zero)
                .ToList();

            if (parsed.Count > 0)
            {
                options.RetryDelays.Clear();

                foreach (var delay in parsed)
                {
                    options.RetryDelays.Add(delay);
                }
            }
        }
    }

    /// <summary>Binds the <c>AgentPrism:Retention</c> section.</summary>
    private static void BindRetention(IConfigurationSection section, AgentPrismRetentionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismRetentionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRetentionOptions.BatchSize)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var batchSize))
        {
            options.BatchSize = batchSize;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismRetentionOptions.BatchDelay)],
                CultureInfo.InvariantCulture,
                out var batchDelay))
        {
            options.BatchDelay = batchDelay;
        }

        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.RunEvents)), options.RunEvents);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.ToolInvocations)), options.ToolInvocations);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Spans)), options.Spans);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Jobs)), options.Jobs);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.WebhookDeliveries)), options.WebhookDeliveries);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.EvalCaseResults)), options.EvalCaseResults);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.WorkflowCheckpoints)), options.WorkflowCheckpoints);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.SkillScriptGrants)), options.SkillScriptGrants);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Attachments)), options.Attachments);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Sessions)), options.Sessions);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Conversations)), options.Conversations);

        // HATA-S1-005: these four targets were added to ForTarget but were NOT
        // added HERE - a signature change is not considered complete until
        // applied at EVERY call site of the body (see AGENTS.md, the Phase 20 note).
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.RunInputs)), options.RunInputs);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.VoiceSessions)), options.VoiceSessions);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.RunScores)), options.RunScores);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.DocumentEmbeddings)), options.DocumentEmbeddings);
    }

    private static void BindRetentionTarget(IConfigurationSection section, RetentionTargetOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(RetentionTargetOptions.MaxAgeDays)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAgeDays))
        {
            options.MaxAgeDays = maxAgeDays;
        }

        if (TryReadBool(section, nameof(RetentionTargetOptions.Archive), out var archive))
        {
            options.Archive = archive;
        }
    }

    private static void BindHealth(IConfigurationSection section, AgentPrismHealthOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismHealthOptions.CacheTtl)],
                CultureInfo.InvariantCulture,
                out var cacheTtl))
        {
            options.CacheTtl = cacheTtl;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismHealthOptions.BackgroundInterval)],
                CultureInfo.InvariantCulture,
                out var backgroundInterval))
        {
            options.BackgroundInterval = backgroundInterval;
        }
    }

    private static void BindRunRecording(IConfigurationSection recording, AgentPrismRunRecordingOptions options)
    {
        if (!recording.Exists())
        {
            return;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordRunInput), out var recordRunInput))
        {
            options.RecordRunInput = recordRunInput;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordMessageDeltas), out var recordDeltas))
        {
            options.RecordMessageDeltas = recordDeltas;
        }

        if (TryReadBool(recording, nameof(AgentPrismRunRecordingOptions.RecordToolPayloads), out var recordPayloads))
        {
            options.RecordToolPayloads = recordPayloads;
        }

        if (TryReadBool(
                recording,
                nameof(AgentPrismRunRecordingOptions.RecordReasoningDeltas),
                out var recordReasoningDeltas))
        {
            options.RecordReasoningDeltas = recordReasoningDeltas;
        }

        if (int.TryParse(
                recording[nameof(AgentPrismRunRecordingOptions.MaxPayloadLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxPayloadLength))
        {
            options.MaxPayloadLength = maxPayloadLength;
        }
    }

    private static void BindObservability(IConfigurationSection section, AgentPrismObservabilityOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismObservabilityOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismObservabilityOptions.PersistSpans), out var persistSpans))
        {
            options.PersistSpans = persistSpans;
        }

        if (TryReadBool(
                section,
                nameof(AgentPrismObservabilityOptions.AlwaysPersistFailures),
                out var alwaysPersistFailures))
        {
            options.AlwaysPersistFailures = alwaysPersistFailures;
        }

        if (TryReadBool(
                section,
                nameof(AgentPrismObservabilityOptions.RecordSensitiveData),
                out var recordSensitiveData))
        {
            options.RecordSensitiveData = recordSensitiveData;
        }

        if (double.TryParse(
                section[nameof(AgentPrismObservabilityOptions.SuccessSampleRatio)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var sampleRatio))
        {
            options.SuccessSampleRatio = sampleRatio;
        }

        if (int.TryParse(
                section[nameof(AgentPrismObservabilityOptions.MaxSpansPerRun)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxSpans))
        {
            options.MaxSpansPerRun = maxSpans;
        }

        if (TryReadBool(
                section,
                nameof(AgentPrismObservabilityOptions.IncludeAgentVersionTag),
                out var includeAgentVersionTag))
        {
            options.IncludeAgentVersionTag = includeAgentVersionTag;
        }

        if (TryReadBool(
                section,
                nameof(AgentPrismObservabilityOptions.EnableQuotaUsageGauge),
                out var enableQuotaUsageGauge))
        {
            options.EnableQuotaUsageGauge = enableQuotaUsageGauge;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismObservabilityOptions.QuotaUsageRefreshInterval)],
                CultureInfo.InvariantCulture,
                out var quotaUsageRefreshInterval))
        {
            options.QuotaUsageRefreshInterval = quotaUsageRefreshInterval;
        }
    }

    /// <summary>Binds the <c>AgentPrism:OnlineEvaluation</c> section.</summary>
    private static void BindOnlineEvaluation(IConfigurationSection section, OnlineEvaluationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(OnlineEvaluationOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (double.TryParse(
                section[nameof(OnlineEvaluationOptions.SampleRate)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var sampleRate))
        {
            options.SampleRate = sampleRate;
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.MaxScoresPerHour)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxScoresPerHour))
        {
            options.MaxScoresPerHour = maxScoresPerHour;
        }

        var agentNames = section.GetSection(nameof(OnlineEvaluationOptions.AgentNames));

        if (agentNames.Exists())
        {
            var parsed = agentNames.GetChildren()
                .Select(static child => child.Value)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (parsed.Count > 0)
            {
                options.AgentNames.Clear();

                foreach (var name in parsed)
                {
                    options.AgentNames.Add(name!);
                }
            }
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.LowScoreThreshold)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var lowScoreThreshold))
        {
            options.LowScoreThreshold = lowScoreThreshold;
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.MinSampleSize)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var minSampleSize))
        {
            options.MinSampleSize = minSampleSize;
        }

        if (TimeSpan.TryParse(
                section[nameof(OnlineEvaluationOptions.EvaluationWindow)],
                CultureInfo.InvariantCulture,
                out var evaluationWindow))
        {
            options.EvaluationWindow = evaluationWindow;
        }
    }

    private static bool TryReadBool(IConfiguration section, string key, out bool value)
        => bool.TryParse(section[key], out value);
}
