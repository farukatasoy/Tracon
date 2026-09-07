using AgentPrism;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Configuration;

/// <summary>
/// Phase 105 split — <c>AgentPrismServiceCollectionExtensions</c> was one
/// 2,662-line file; the registration body now runs through four private
/// helpers spread across <c>Registration.*.cs</c> partial files, called in
/// their original order from <c>AddAgentPrism()</c>.
/// </summary>
/// <remarks>
/// This snapshot is a durable regression gate, not a one-time proof: the
/// expected list below was captured from <c>AddAgentPrism()</c> BEFORE the
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
/// <c>AgentPrismServiceCollectionExtensions</c> renumbers every later
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
    public void AddAgentPrism_registers_the_same_descriptors_in_the_same_order()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism();

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
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismSchedulingOptions] | Singleton | AgentPrism.AgentPrismSchedulingOptionsValidator",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Options.StartupValidatorOptions] | Transient | Factory",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.SingletonExecutionOptions] | Singleton | AgentPrism.SingletonExecutionOptionsValidator",
        "AgentPrism.ISingletonLeaseStore | Singleton | AgentPrism.InMemorySingletonLeaseStore",
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
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismQuotaOptions] | Singleton | AgentPrism.AgentPrismQuotaOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismWebhookOptions] | Singleton | AgentPrism.AgentPrismWebhookOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismRetentionOptions] | Singleton | AgentPrism.AgentPrismRetentionOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.OnlineEvaluationOptions] | Singleton | AgentPrism.OnlineEvaluationOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.RunReconciliationOptions] | Singleton | AgentPrism.RunReconciliationOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismContentProtectionOptions] | Singleton | AgentPrism.AgentPrismContentProtectionOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismRunContinuationOptions] | Singleton | AgentPrism.AgentPrismRunContinuationOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismDrainOptions] | Singleton | AgentPrism.AgentPrismDrainOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismImageOptions] | Singleton | AgentPrism.AgentPrismImageOptionsValidator",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismStructuredResponseOptions] | Singleton | AgentPrism.AgentPrismStructuredResponseOptionsValidator",
        "Microsoft.Extensions.Logging.ILoggerFactory | Singleton | Microsoft.Extensions.Logging.LoggerFactory",
        "Microsoft.Extensions.Logging.ILogger`1[TCategoryName] | Singleton | Microsoft.Extensions.Logging.Logger`1[T]",
        "Microsoft.Extensions.Options.IConfigureOptions`1[Microsoft.Extensions.Logging.LoggerFilterOptions] | Singleton | Instance:Microsoft.Extensions.Logging.DefaultLoggerLevelConfigureOptions",
        "Microsoft.Extensions.Options.IValidateOptions`1[AgentPrism.AgentPrismOptions] | Singleton | AgentPrism.AgentPrismOptionsValidator",
        "AgentPrism.ITenantContext | Singleton | AgentPrism.SingleTenantContext",
        "AgentPrism.IRunAttributionContext | Singleton | AgentPrism.DefaultRunAttributionContext",
        "AgentPrism.IToolAuthorizationHandler | Singleton | AgentPrism.AllowAllToolAuthorizationHandler",
        "AgentPrism.IRunAuthorizationHandler | Singleton | AgentPrism.AllowAllRunAuthorizationHandler",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.RequiredBindingValidator",
        "AgentPrism.IToolArgumentsValidator | Singleton | Instance:AgentPrism.NoOpToolArgumentsValidator",
        "AgentPrism.IStructuredResponseValidator | Singleton | Instance:AgentPrism.NoOpStructuredResponseValidator",
        "AgentPrism.IToolRegistry | Singleton | Factory",
        "AgentPrism.ModelProviderCircuitBreaker | Singleton | Factory",
        "AgentPrism.ProviderConcurrencyLimiter | Singleton | Factory",
        "AgentPrism.ContentGuardPipeline | Singleton | Factory",
        "AgentPrism.ITenantProviderBindingStore | Singleton | AgentPrism.InMemoryTenantProviderBindingStore",
        "AgentPrism.ITenantEgressPolicyStore | Singleton | AgentPrism.InMemoryTenantEgressPolicyStore",
        "AgentPrism.TenantProviderCredentialResolver | Singleton | Factory",
        "AgentPrism.IProviderRetryClassifier | Singleton | AgentPrism.DefaultProviderRetryClassifier",
        "AgentPrism.IModelProviderRegistry | Singleton | Factory",
        "AgentPrism.ContextWindowEstimator | Singleton | AgentPrism.ContextWindowEstimator",
        "AgentPrism.IRunPricingResolver | Singleton | AgentPrism.RunPricingResolver",
        "AgentPrism.RunCostRecalculationService | Singleton | AgentPrism.RunCostRecalculationService",
        "AgentPrism.IRunErrorClassifier | Singleton | AgentPrism.DefaultRunErrorClassifier",
        "AgentPrism.IRunCancellationRegistry | Singleton | AgentPrism.RunCancellationRegistry",
        "AgentPrism.ModelProviderHealthCache | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.ModelProviderHealthBackgroundService",
        "AgentPrism.SchemaReadyGate | Singleton | AgentPrism.SchemaReadyGate",
        "AgentPrism.AgentPrismDiagnosticsCollector | Singleton | Factory",
        "Microsoft.Agents.AI.ChatHistoryProvider | Singleton | Factory",
        "Microsoft.Agents.AI.AgentFileStore | Singleton | Factory",
        "AgentPrism.CompiledAgentCache | Singleton | AgentPrism.CompiledAgentCache",
        "AgentPrism.CallableAgentResolver | Singleton | AgentPrism.CallableAgentResolver",
        "AgentPrism.AgentDefinitionCompiler | Singleton | Factory",
        "AgentPrism.KnowledgeIngestionService | Singleton | Factory",
        "AgentPrism.AgentDefinitionValidator | Singleton | Factory",
        "AgentPrism.IAuditLog | Singleton | AgentPrism.InMemoryAuditLog",
        "AgentPrism.IAuditActorResolver | Singleton | AgentPrism.AmbientAuditActorResolver",
        "AgentPrism.IContentProtector | Singleton | Instance:AgentPrism.NullContentProtector",
        "AgentPrism.IAgentDefinitionStore | Singleton | Factory",
        "AgentPrism.IAgentSkillStore | Singleton | Factory",
        "AgentPrism.AgentSkillCatalog | Singleton | Factory",
        "AgentPrism.IRunScoreStore | Singleton | Factory",
        "AgentPrism.IRunStore | Singleton | Factory",
        "AgentPrism.IRunInputStore | Singleton | AgentPrism.InMemoryRunInputStore",
        "AgentPrism.IPendingApprovalStore | Singleton | Factory",
        "AgentPrism.ISkillScriptGrantStore | Singleton | Factory",
        "AgentPrism.ISessionStore | Singleton | Factory",
        "AgentPrism.ITraceStore | Singleton | Factory",
        "AgentPrism.IToolApprovalRuleStore | Singleton | Factory",
        "AgentPrism.IMcpServerStore | Singleton | Factory",
        "AgentPrism.AttachmentTypeGuard | Singleton | AgentPrism.AttachmentTypeGuard",
        "AgentPrism.IAttachmentStore | Singleton | Factory",
        "AgentPrism.ImagePricing | Singleton | AgentPrism.ImagePricing",
        "AgentPrism.ImageAttachmentWriter | Singleton | AgentPrism.ImageAttachmentWriter",
        "AgentPrism.ImageGeneratorResolver | Singleton | AgentPrism.ImageGeneratorResolver",
        "AgentPrism.IWorkflowDefinitionStore | Singleton | Factory",
        "AgentPrism.IWorkflowCheckpointStore | Singleton | AgentPrism.InMemoryWorkflowCheckpointStore",
        "AgentPrism.IJobStore | Singleton | AgentPrism.InMemoryJobStore",
        "AgentPrism.IJobScheduleStore | Singleton | AgentPrism.InMemoryJobScheduleStore",
        "AgentPrism.IInboundTriggerStore | Singleton | AgentPrism.InMemoryInboundTriggerStore",
        "AgentPrism.InboundTriggerSecretResolver | Singleton | Factory",
        "AgentPrism.InboundTriggerRateLimiter | Singleton | Factory",
        "AgentPrism.InboundTriggerDispatcher | Singleton | Factory",
        "AgentPrism.AgentBatchJobHandler | Scoped | AgentPrism.AgentBatchJobHandler",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.AgentRunJobHandler | Scoped | AgentPrism.AgentRunJobHandler",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.ApprovalResumeJobHandler | Scoped | AgentPrism.ApprovalResumeJobHandler",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.RunContinuationJobHandler | Scoped | AgentPrism.RunContinuationJobHandler",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.WorkflowJobHandler | Scoped | Factory",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.IEvalStore | Singleton | AgentPrism.InMemoryEvalStore",
        "AgentPrism.EvalCheckRegistry | Singleton | AgentPrism.EvalCheckRegistry",
        "AgentPrism.EvalJobHandler | Scoped | AgentPrism.EvalJobHandler",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.RunToCasePromoter | Singleton | AgentPrism.RunToCasePromoter",
        "AgentPrism.RunSampler | Singleton | AgentPrism.RunSampler",
        "AgentPrism.OnlineEvalSummaryService | Singleton | AgentPrism.OnlineEvalSummaryService",
        "AgentPrism.RunJudgeSet | Singleton | AgentPrism.RunJudgeSet",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.RunJudgeValidationService",
        "AgentPrism.OnlineEvalJobHandler | Scoped | AgentPrism.OnlineEvalJobHandler",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.IQuotaStore | Singleton | AgentPrism.InMemoryQuotaStore",
        "AgentPrism.IWebhookStore | Singleton | AgentPrism.InMemoryWebhookStore",
        "AgentPrism.IApiKeyStore | Singleton | AgentPrism.InMemoryApiKeyStore",
        "AgentPrism.EgressSocketGuard | Singleton | Factory",
        "AgentPrism.WebhookHttpClient | Singleton | Factory",
        "AgentPrism.IWebhookPublisher | Singleton | Factory",
        "AgentPrism.QuotaEnforcer | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "AgentPrism.WebhookDeliveryJobHandler | Scoped | Factory",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.IIdempotencyStore | Singleton | AgentPrism.InMemoryIdempotencyStore",
        "AgentPrism.IRetentionPolicyStore | Singleton | AgentPrism.InMemoryRetentionPolicyStore",
        "AgentPrism.IRetentionStore | Singleton | AgentPrism.NullRetentionStore",
        "AgentPrism.RetentionPolicyResolver | Singleton | AgentPrism.RetentionPolicyResolver",
        "AgentPrism.IDataSubjectStore | Singleton | AgentPrism.NullDataSubjectStore",
        "AgentPrism.SessionConversationResolver | Singleton | AgentPrism.SessionConversationResolver",
        "AgentPrism.RetentionExecutor | Singleton | Factory",
        "AgentPrism.RetentionJobHandler | Scoped | Factory",
        "AgentPrism.JobHandlerRegistration | Singleton | Instance:AgentPrism.JobHandlerRegistration",
        "AgentPrism.JobHandlerRegistry | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.JobHandlerRegistryValidator",
        "AgentPrism.IJobDispatcher | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.JobWorkerBackgroundService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.RunHeartbeatWriter",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.RunReconciliationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.ApprovalExpirationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.CanaryEvaluationService",
        "AgentPrism.IExperimentStore | Singleton | Factory",
        "AgentPrism.ExperimentAssignmentResolver | Singleton | AgentPrism.ExperimentAssignmentResolver",
        "AgentPrism.ITenantStore | Singleton | Factory",
        "AgentPrism.AgentPrismMetrics | Singleton | Factory",
        "AgentPrism.RunTraceCollector | Singleton | AgentPrism.RunTraceCollector",
        "AgentPrism.ToolApprovalPolicyRegistry | Singleton | AgentPrism.ToolApprovalPolicyRegistry",
        "AgentPrism.ToolApprovalRuleEvaluator | Singleton | AgentPrism.ToolApprovalRuleEvaluator",
        "AgentPrism.IToolApprovalPresenter | Singleton | Instance:AgentPrism.NullToolApprovalPresenter",
        "AgentPrism.ToolApprovalPresenterRunner | Singleton | AgentPrism.ToolApprovalPresenterRunner",
        "AgentPrism.AgentSessionManager | Singleton | Factory",
        "AgentPrism.ConversationBranchService | Singleton | Factory",
        "AgentPrism.RunReplayService | Singleton | Factory",
        "AgentPrism.IAgentSource | Singleton | AgentPrism.CodeAgentSource",
        "AgentPrism.IAgentSource | Singleton | AgentPrism.DefinitionStoreAgentSource",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.AgentSourceValidationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | AgentPrism.ToolRegistrationValidationService",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
        "AgentPrism.IAgentDecorator | Singleton | Factory",
        "AgentPrism.IAgentDecorator | Singleton | AgentPrism.OpenTelemetryAgentDecorator",
        "AgentPrism.IAgentDecorator | Singleton | AgentPrism.ToolApprovalAgentDecorator",
        "AgentPrism.IAgentDecorator | Singleton | AgentPrism.StructuredResponseValidatingAgentDecorator",
        "AgentPrism.IAgentCatalog | Singleton | Factory",
        "AgentPrism.AgentPrismDrainService | Singleton | AgentPrism.AgentPrismDrainService",
        "AgentPrism.IAgentPrismDrainState | Singleton | Factory",
        "Microsoft.Extensions.Hosting.IHostedService | Singleton | Factory",
    ];
}
