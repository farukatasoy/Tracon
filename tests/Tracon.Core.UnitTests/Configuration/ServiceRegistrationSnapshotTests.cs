using Tracon;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Configuration;

/// <summary>
/// Phase 105 split — <c>TraconServiceCollectionExtensions</c> was one
/// 2,662-line file; the registration body now runs through four private
/// helpers spread across <c>Registration.*.cs</c> partial files, called in
/// their original order from <c>AddTracon()</c>.
/// </summary>
/// <remarks>
/// This snapshot is a durable regression gate, not a one-time proof: the
/// expected list below was captured from <c>AddTracon()</c> BEFORE the
/// split (via <c>git stash</c> against the pre-refactor file) and confirmed
/// byte-for-byte identical to the post-split output before being embedded
/// here. A future change that drops a registration, adds one, or changes a
/// lifetime breaks this test immediately — the failure diff names the
/// exact entry that moved, which a behavior test several layers away would not.
/// <para>
/// <b>Known limitation:</b> <see cref="Describe"/> collapses every
/// factory-based registration to the literal "Factory" rather than trying to
/// name the closure, so two <c>TryAdd*(factory)</c> calls for the same
/// service type and lifetime (for example the four <c>IJobHandler</c>
/// factories, or the three <c>IHostedService</c> factories) are
/// indistinguishable here — a reorder among only those cannot be detected
/// by this test. A richer representation was tried (distinguishing factories
/// by <see cref="System.Reflection.MethodInfo.Name"/>) but rejected: the
/// compiler assigns lambda-container ordinals per PARTIAL CLASS, not per
/// file, so adding or removing any unrelated private method anywhere in
/// <c>TraconServiceCollectionExtensions</c> renumbers every later
/// closure and turns unrelated changes into mass false positives —
/// strictly worse than the coverage gap it closes. Relative order among
/// <see cref="Microsoft.Extensions.Hosting.IHostedService"/> registrations
/// specifically (where shutdown order matters) is exercised at the
/// integration level instead, per
/// <c>.agents/ortak/test-seviyeleri.md</c>: a behavior that crosses a DI
/// boundary is not proven by a unit-level snapshot.
/// </para>
/// </remarks>
public sealed class ServiceRegistrationSnapshotTests
{
    [Fact]
    public void AddTracon_registers_the_same_descriptors_in_the_same_order()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        var snapshot = services.Select(Describe).ToArray();

        snapshot.ShouldBe(ExpectedDescriptors);
    }

    private static string Describe(ServiceDescriptor descriptor)
    {
        var implementation = descriptor.ImplementationType?.ToString()
            ?? (descriptor.ImplementationInstance is not null
                ? "Instance:" + descriptor.ImplementationInstance.GetType()
                : null)
            ?? (descriptor.ImplementationFactory is not null ? "Factory" : "Unknown");

        return $"{descriptor.ServiceType} | {descriptor.Lifetime} | {implementation}";
    }

    /// <summary>
    /// Captured pre-refactor, verified zero-diff post-refactor (see the class remarks).
    /// </summary>
    private static readonly string[] ExpectedDescriptors =
    [
        "Microsoft.Extensions.Options.IOptions`1[TOptions] | Singleton | Microsoft.Extensions.Options.UnnamedOptionsManager`1[TOptions]",
        "Microsoft.Extensions.Options.IOptionsSnapshot`1[TOptions] | Scoped | Microsoft.Extensions.Options.OptionsManager`1[TOptions]",
        "Microsoft.Extensions.Options.IOptionsMonitor`1[TOptions] | Singleton | Microsoft.Extensions.Options.OptionsMonitor`1[TOptions]",
        "Microsoft.Extensions.Options.IOptionsFactory`1[TOptions] | Transient | Microsoft.Extensions.Options.OptionsFactory`1[TOptions]",
        "Microsoft.Extensions.Options.IOptionsMonitorCache`1[TOptions] | Singleton | Microsoft.Extensions.Options.OptionsCache`1[TOptions]",
        "Microsoft.Extensions.Options.IStartupValidator | Transient | Microsoft.Extensions.Options.StartupValidator",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconSchedulingOptions] | Singleton | Tracon.TraconSchedulingOptionsValidator",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.SingletonExecutionOptions] | Singleton | Tracon.SingletonExecutionOptionsValidator",
        "Tracon.ISingletonLeaseStore | Singleton | Tracon.InMemorySingletonLeaseStore",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconQuotaOptions] | Singleton | Tracon.TraconQuotaOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconWebhookOptions] | Singleton | Tracon.TraconWebhookOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconRetentionOptions] | Singleton | Tracon.TraconRetentionOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.OnlineEvaluationOptions] | Singleton | Tracon.OnlineEvaluationOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.RunReconciliationOptions] | Singleton | Tracon.RunReconciliationOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconContentProtectionOptions] | Singleton | Tracon.TraconContentProtectionOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconRunContinuationOptions] | Singleton | Tracon.TraconRunContinuationOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconDrainOptions] | Singleton | Tracon.TraconDrainOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconImageOptions] | Singleton | Tracon.TraconImageOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconStructuredResponseOptions] | Singleton | Tracon.TraconStructuredResponseOptionsValidator",
        "Microsoft.Extensions.Logging.ILoggerFactory | Singleton | Microsoft.Extensions.Logging.LoggerFactory",
        "Microsoft.Extensions.Logging.ILogger`1[TCategoryName] | Singleton | Microsoft.Extensions.Logging.Logger`1[T]",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Logging.LoggerFilterOptions] | Singleton | Instance:Microsoft.Extensions.Logging.DefaultLoggerLevelConfigureOptions",
        "Microsoft.Extensions.Options.IValidateOptions`1[Tracon.TraconOptions] | Singleton | Tracon.TraconOptionsValidator",
        "Tracon.ITenantContext | Singleton | Tracon.SingleTenantContext",
        "Tracon.IRunAttributionContext | Singleton | Tracon.DefaultRunAttributionContext",
        "Tracon.IToolAuthorizationHandler | Singleton | Tracon.AllowAllToolAuthorizationHandler",
        "Tracon.IRunAuthorizationHandler | Singleton | Tracon.AllowAllRunAuthorizationHandler",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.RequiredBindingValidator",
        "Tracon.IToolArgumentsValidator | Singleton | Instance:Tracon.NoOpToolArgumentsValidator",
        "Tracon.IStructuredResponseValidator | Singleton | Instance:Tracon.NoOpStructuredResponseValidator",
        "Tracon.IToolRegistry | Singleton | Factory",
        "Tracon.ModelProviderCircuitBreaker | Singleton | Factory",
        "Tracon.ProviderConcurrencyLimiter | Singleton | Factory",
        "Tracon.ContentGuardPipeline | Singleton | Factory",
        "Tracon.ITenantProviderBindingStore | Singleton | Tracon.InMemoryTenantProviderBindingStore",
        "Tracon.ITenantEgressPolicyStore | Singleton | Tracon.InMemoryTenantEgressPolicyStore",
        "Tracon.TenantProviderCredentialResolver | Singleton | Factory",
        "Tracon.IProviderRetryClassifier | Singleton | Tracon.DefaultProviderRetryClassifier",
        "Tracon.IModelProviderRegistry | Singleton | Factory",
        "Tracon.ContextWindowEstimator | Singleton | Tracon.ContextWindowEstimator",
        "Tracon.IRunPricingResolver | Singleton | Tracon.RunPricingResolver",
        "Tracon.RunCostRecalculationService | Singleton | Tracon.RunCostRecalculationService",
        "Tracon.IRunErrorClassifier | Singleton | Tracon.DefaultRunErrorClassifier",
        "Tracon.IRunCancellationRegistry | Singleton | Tracon.RunCancellationRegistry",
        "Tracon.ModelProviderHealthCache | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.ModelProviderHealthBackgroundService",
        "Tracon.SchemaReadyGate | Singleton | Tracon.SchemaReadyGate",
        "Tracon.TraconDiagnosticsCollector | Singleton | Factory",
        "Microsoft.Agents.AI.ChatHistoryProvider | Singleton | Factory",
        "Microsoft.Agents.AI.AgentFileStore | Singleton | Factory",
        "Tracon.CompiledAgentCache | Singleton | Tracon.CompiledAgentCache",
        "Tracon.CallableAgentResolver | Singleton | Tracon.CallableAgentResolver",
        "Tracon.AgentDefinitionCompiler | Singleton | Factory",
        "Tracon.KnowledgeIngestionService | Singleton | Factory",
        "Tracon.AgentDefinitionValidator | Singleton | Factory",
        "Tracon.IAuditLog | Singleton | Tracon.InMemoryAuditLog",
        "Tracon.IAuditActorResolver | Singleton | Tracon.AmbientAuditActorResolver",
        "Tracon.IContentProtector | Singleton | Instance:Tracon.NullContentProtector",
        "Tracon.IAgentDefinitionStore | Singleton | Factory",
        "Tracon.IAgentSkillStore | Singleton | Factory",
        "Tracon.AgentSkillCatalog | Singleton | Factory",
        "Tracon.IRunScoreStore | Singleton | Factory",
        "Tracon.IRunStore | Singleton | Factory",
        "Tracon.IRunInputStore | Singleton | Tracon.InMemoryRunInputStore",
        "Tracon.IPendingApprovalStore | Singleton | Factory",
        "Tracon.ISkillScriptGrantStore | Singleton | Factory",
        "Tracon.ISessionStore | Singleton | Factory",
        "Tracon.ITraceStore | Singleton | Factory",
        "Tracon.IToolApprovalRuleStore | Singleton | Factory",
        "Tracon.IMcpServerStore | Singleton | Factory",
        "Tracon.AttachmentTypeGuard | Singleton | Tracon.AttachmentTypeGuard",
        "Tracon.IAttachmentStore | Singleton | Factory",
        "Tracon.ImagePricing | Singleton | Tracon.ImagePricing",
        "Tracon.ImageAttachmentWriter | Singleton | Tracon.ImageAttachmentWriter",
        "Tracon.ImageGeneratorResolver | Singleton | Tracon.ImageGeneratorResolver",
        "Tracon.IWorkflowDefinitionStore | Singleton | Factory",
        "Tracon.IWorkflowCheckpointStore | Singleton | Tracon.InMemoryWorkflowCheckpointStore",
        "Tracon.IJobStore | Singleton | Tracon.InMemoryJobStore",
        "Tracon.IJobScheduleStore | Singleton | Tracon.InMemoryJobScheduleStore",
        "Tracon.IInboundTriggerStore | Singleton | Tracon.InMemoryInboundTriggerStore",
        "Tracon.InboundTriggerSecretResolver | Singleton | Factory",
        "Tracon.InboundTriggerRateLimiter | Singleton | Factory",
        "Tracon.InboundTriggerDispatcher | Singleton | Factory",
        "Tracon.AgentBatchJobHandler | Scoped | Tracon.AgentBatchJobHandler",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.AgentRunJobHandler | Scoped | Tracon.AgentRunJobHandler",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.ApprovalResumeJobHandler | Scoped | Tracon.ApprovalResumeJobHandler",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.RunContinuationJobHandler | Scoped | Tracon.RunContinuationJobHandler",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.WorkflowJobHandler | Scoped | Factory",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.IEvalStore | Singleton | Tracon.InMemoryEvalStore",
        "Tracon.EvalCheckRegistry | Singleton | Tracon.EvalCheckRegistry",
        "Tracon.IEvalEvaluatorFactory | Singleton | Tracon.LocalEvalEvaluatorFactory",
        "Tracon.EvalJobHandler | Scoped | Tracon.EvalJobHandler",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.RunToCasePromoter | Singleton | Tracon.RunToCasePromoter",
        "Tracon.RunSampler | Singleton | Tracon.RunSampler",
        "Tracon.OnlineEvalSummaryService | Singleton | Tracon.OnlineEvalSummaryService",
        "Tracon.RunJudgeSet | Singleton | Tracon.RunJudgeSet",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.RunJudgeValidationService",
        "Tracon.OnlineEvalJobHandler | Scoped | Tracon.OnlineEvalJobHandler",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.IQuotaStore | Singleton | Tracon.InMemoryQuotaStore",
        "Tracon.IWebhookStore | Singleton | Tracon.InMemoryWebhookStore",
        "Tracon.IApiKeyStore | Singleton | Tracon.InMemoryApiKeyStore",
        "Tracon.EgressSocketGuard | Singleton | Factory",
        "Tracon.WebhookHttpClient | Singleton | Factory",
        "Tracon.IWebhookPublisher | Singleton | Factory",
        "Tracon.QuotaEnforcer | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "Tracon.WebhookDeliveryJobHandler | Scoped | Factory",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.IIdempotencyStore | Singleton | Tracon.InMemoryIdempotencyStore",
        "Tracon.IRetentionPolicyStore | Singleton | Tracon.InMemoryRetentionPolicyStore",
        "Tracon.IRetentionStore | Singleton | Tracon.NullRetentionStore",
        "Tracon.RetentionPolicyResolver | Singleton | Tracon.RetentionPolicyResolver",
        "Tracon.IDataSubjectStore | Singleton | Tracon.NullDataSubjectStore",
        "Tracon.SessionConversationResolver | Singleton | Tracon.SessionConversationResolver",
        "Tracon.RetentionExecutor | Singleton | Factory",
        "Tracon.RetentionJobHandler | Scoped | Factory",
        "Tracon.JobHandlerRegistration | Singleton | Instance:Tracon.JobHandlerRegistration",
        "Tracon.JobHandlerRegistry | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.JobHandlerRegistryValidator",
        "Tracon.IJobDispatcher | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.JobWorkerBackgroundService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.RunHeartbeatWriter",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.RunReconciliationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.ApprovalExpirationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.CanaryEvaluationService",
        "Tracon.IExperimentStore | Singleton | Factory",
        "Tracon.ExperimentAssignmentResolver | Singleton | Tracon.ExperimentAssignmentResolver",
        "Tracon.ITenantStore | Singleton | Factory",
        "Tracon.TraconMetrics | Singleton | Factory",
        "Tracon.RunTraceCollector | Singleton | Tracon.RunTraceCollector",
        "Tracon.ToolApprovalPolicyRegistry | Singleton | Tracon.ToolApprovalPolicyRegistry",
        "Tracon.ToolApprovalRuleEvaluator | Singleton | Tracon.ToolApprovalRuleEvaluator",
        "Tracon.IToolApprovalPresenter | Singleton | Instance:Tracon.NullToolApprovalPresenter",
        "Tracon.ToolApprovalPresenterRunner | Singleton | Tracon.ToolApprovalPresenterRunner",
        "Tracon.AgentSessionManager | Singleton | Factory",
        "Tracon.ConversationBranchService | Singleton | Factory",
        "Tracon.RunReplayService | Singleton | Factory",
        "Tracon.IAgentSource | Singleton | Tracon.CodeAgentSource",
        "Tracon.IAgentSource | Singleton | Tracon.DefinitionStoreAgentSource",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.AgentSourceValidationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Tracon.ToolRegistrationValidationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "Tracon.IAgentDecorator | Singleton | Factory",
        "Tracon.IAgentDecorator | Singleton | Tracon.OpenTelemetryAgentDecorator",
        "Tracon.IAgentDecorator | Singleton | Tracon.ToolApprovalAgentDecorator",
        "Tracon.IAgentDecorator | Singleton | Tracon.StructuredResponseValidatingAgentDecorator",
        "Tracon.IAgentCatalog | Singleton | Factory",
        "Tracon.TraconDrainService | Singleton | Tracon.TraconDrainService",
        "Tracon.ITraconDrainState | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
    ];
}
