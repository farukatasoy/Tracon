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
    private static void RegisterOptions(IServiceCollection services, IConfiguration? configurationSection)
    {
        services.AddOptions<TraconOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            // Configuration is bound BY HAND. `optionsBuilder.Bind(section)` relies
            // on reflection and produces IL2026 + IL3050; the source generator
            // hides this during the build, but the diagnostics resurface in
            // `dotnet format`'s analyzer pass. Manual binding is clean on both
            // gates and removes a package dependency (Options.ConfigurationExtensions).
            // Rationale: docs/KARARLAR.md, decision K-021.
            services.Configure<TraconOptions>(options => Bind(configurationSection, options));
        }

        // Batch and scheduled run (Phase 17). A separate section: it carries
        // its own SectionName like the PostgreSql package's
        // TraconPostgreSqlOptions, but unlike TraconOptions it is
        // always registered even without a separate Use...() call (K-018 -
        // stores are first-class).
        services.AddOptions<TraconSchedulingOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<TraconSchedulingOptions>(
                options => BindScheduling(configurationSection.GetSection("Scheduling"), options));
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconSchedulingOptions>, TraconSchedulingOptionsValidator>());

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
        services.TryAddTraconDefault<ISingletonLeaseStore, InMemorySingletonLeaseStore>();

        // Quotas and event publishing (Phase 21). Same rationale as
        // scheduling: carries its own SectionName and requires no separate
        // Use...() call.
        services.AddOptions<TraconQuotaOptions>().ValidateOnStart();
        services.AddOptions<TraconWebhookOptions>().ValidateOnStart();
        services.AddOptions<TraconRateLimitOptions>().ValidateOnStart();

        // Data retention and archiving (Phase 25). Same rationale: carries its
        // own SectionName, requires no separate Use...() call.
        services.AddOptions<TraconRetentionOptions>().ValidateOnStart();

        // Idempotency-Key support (Phase 43). Same rationale: carries its own
        // SectionName, requires no separate Use...() call.
        services.AddOptions<TraconIdempotencyOptions>().ValidateOnStart();

        // Queued (durable) run (Phase 46). Same rationale: carries its own
        // SectionName, requires no separate Use...() call.
        services.AddOptions<TraconAsyncRunOptions>().ValidateOnStart();

        // Orphaned run reconciliation (Phase 54). Same rationale: carries its
        // own SectionName, requires no separate Use...() call. Default
        // Enabled=false; while disabled, none of the heartbeat/reconciliation
        // queries are issued (K1).
        services.AddOptions<RunReconciliationOptions>().ValidateOnStart();

        // Async approval inbox (Phase 55). Same rationale: carries its own
        // SectionName, requires no separate Use...() call.
        services.AddOptions<TraconApprovalOptions>().ValidateOnStart();

        // Knowledge base / semantic search (Phase 51). Same rationale: carries
        // its own SectionName, requires no separate Use...() call. Becomes
        // functional only when an IVectorSearchStore (today only PostgreSQL)
        // AND an IEmbeddingGenerator are both registered together
        // (KnowledgeIngestionService.IsSupported).
        services.AddOptions<TraconKnowledgeOptions>().ValidateOnStart();

        // Content moderation (Phase 48). Settings are always registered, but
        // when no IContentGuard is registered they are never read at all: the
        // moderation wrapper is not added to the pipeline. K1's gate is the
        // registration itself, not a flag.
        services.AddOptions<TraconContentGuardOptions>().ValidateOnStart();
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
        services.AddOptions<TraconTenantProviderOptions>().ValidateOnStart();

        // Inbound triggers (Phase 66). Same rationale as tenant providers:
        // carries its own section, requires no separate Use...() call. The
        // default prefix is restrictive on its own (section 66.2).
        services.AddOptions<TraconInboundTriggerOptions>().ValidateOnStart();

        // Outbound network guard (Phase 77). Governs all three outbound
        // surfaces: webhook delivery, MCP connections and model provider
        // calls. AllowPrivateNetworkTargets defaults to false, so the guard
        // arrives open and the private network arrives closed.
        services.AddOptions<TraconEgressOptions>().ValidateOnStart();

        // The prefix that bounds which configuration key an MCP server
        // definition may name (Phase 77). Lives in the abstractions package
        // because the saving endpoint and the connecting transport are in two
        // packages that do not see each other.
        services.AddOptions<TraconMcpSecurityOptions>().ValidateOnStart();

        // At-rest content protection (Phase 82). Same rationale: carries its
        // own section, requires no separate Use...() call. Enabled defaults
        // to false (K1); the default IContentProtector below writes plaintext
        // unchanged until AddContentProtection(...) replaces it.
        services.AddOptions<TraconContentProtectionOptions>().ValidateOnStart();

        // Interrupted-run continuation and graceful-shutdown drain (Phase
        // 87). Same rationale: carry their own section, require no separate
        // Use...() call. Both default Enabled=false (K1) - see the
        // RunReconciliation comment above; this is the same pattern.
        services.AddOptions<TraconRunContinuationOptions>().ValidateOnStart();
        services.AddOptions<TraconDrainOptions>().ValidateOnStart();

        // Image generation (Phase 88). The option exists even when no provider
        // package is installed; its default is disabled, so this registration has
        // no runtime effect until both the option and an IImageGenerator are present.
        services.AddOptions<TraconImageOptions>().ValidateOnStart();

        // Structured response validation (Phase 131) and bounded repair (Phase
        // 134). Same rationale: carries its own section, requires no separate
        // Use...() call. Enabled=false and MaxRepairAttempts=0 (K1) - repair
        // never fires unless a setup opts into both.
        services.AddOptions<TraconStructuredResponseOptions>().ValidateOnStart();

        // Per-user session ownership (Phase 148). Same rationale: carries its
        // own SectionName, requires no separate Use...() call. Enabled=false
        // (K1) - while off, no owner is written, no listing is narrowed and
        // sessions.owner_id stays NULL, so a setup that configures nothing
        // keeps today's behaviour exactly.
        services.AddOptions<TraconSessionOwnershipOptions>().ValidateOnStart();

        if (configurationSection is not null)
        {
            services.Configure<TraconQuotaOptions>(
                options => BindQuotas(configurationSection.GetSection("Quotas"), options));
            services.Configure<TraconWebhookOptions>(
                options => BindWebhooks(configurationSection.GetSection("Webhooks"), options));
            services.Configure<TraconRateLimitOptions>(
                options => BindRateLimit(configurationSection.GetSection("RateLimit"), options));
            services.Configure<TraconRetentionOptions>(
                options => BindRetention(configurationSection.GetSection("Retention"), options));
            services.Configure<TraconIdempotencyOptions>(
                options => BindIdempotency(configurationSection.GetSection("Idempotency"), options));
            services.Configure<TraconAsyncRunOptions>(
                options => BindAsyncRun(configurationSection.GetSection("AsyncRun"), options));
            services.Configure<RunReconciliationOptions>(
                options => BindRunReconciliation(configurationSection.GetSection("RunReconciliation"), options));
            services.Configure<TraconApprovalOptions>(
                options => BindApproval(configurationSection.GetSection("Approvals"), options));
            services.Configure<OnlineEvaluationOptions>(
                options => BindOnlineEvaluation(configurationSection.GetSection("OnlineEvaluation"), options));
            services.Configure<CanaryOptions>(
                options => BindCanary(configurationSection.GetSection("Canary"), options));
            services.Configure<TraconTenantProviderOptions>(
                options => BindTenantProviders(configurationSection.GetSection("TenantProviders"), options));
            services.Configure<TraconInboundTriggerOptions>(
                options => BindInboundTriggers(configurationSection.GetSection("InboundTriggers"), options));
            services.Configure<TraconEgressOptions>(
                options => BindEgress(configurationSection.GetSection("Egress"), options));
            services.Configure<TraconMcpSecurityOptions>(
                options => BindMcpSecurity(configurationSection.GetSection("Mcp"), options));
            services.Configure<TraconContentProtectionOptions>(
                options => BindContentProtection(configurationSection.GetSection("ContentProtection"), options));
            services.Configure<TraconRunContinuationOptions>(
                options => BindRunContinuation(configurationSection.GetSection("RunContinuation"), options));
            services.Configure<TraconDrainOptions>(
                options => BindDrain(configurationSection.GetSection("Drain"), options));
            services.Configure<TraconImageOptions>(
                options => BindImages(configurationSection.GetSection("Images"), options));
            services.Configure<TraconStructuredResponseOptions>(
                options => BindStructuredResponse(configurationSection.GetSection("StructuredResponse"), options));
            services.Configure<TraconSessionOwnershipOptions>(
                options => BindSessionOwnership(configurationSection.GetSection("SessionOwnership"), options));

            var contentGuardSection = configurationSection.GetSection("ContentGuard");

            services.Configure<TraconContentGuardOptions>(
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
            ServiceDescriptor.Singleton<IValidateOptions<TraconQuotaOptions>, TraconQuotaOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconWebhookOptions>, TraconWebhookOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconRetentionOptions>, TraconRetentionOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<OnlineEvaluationOptions>, OnlineEvaluationOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<RunReconciliationOptions>, RunReconciliationOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconApprovalOptions>, TraconApprovalOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<CanaryOptions>, CanaryOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconContentProtectionOptions>, TraconContentProtectionOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconRunContinuationOptions>, TraconRunContinuationOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconDrainOptions>, TraconDrainOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconImageOptions>, TraconImageOptionsValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<TraconStructuredResponseOptions>, TraconStructuredResponseOptionsValidator>());
    }
}
