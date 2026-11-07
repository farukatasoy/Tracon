using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;

namespace Tracon.Tests.Common;

/// <summary>
/// Builds the run recorder and the services it shares the way a test wants them: the required
/// dependencies are named, and every optional one is left out unless the test asks for it.
/// </summary>
/// <remarks>
/// A member the test does not pass stays <see langword="null"/> and its feature stays off: no quota
/// counting, no webhook, no cancellation from outside. That is the opposite of the container, which
/// builds all of them; a test that exercises one of them passes it by name.
/// </remarks>
internal static class TestRunRecording
{
    /// <summary>Builds the services a recorder shares.</summary>
    /// <param name="runStore">The store the events are written to.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">The recording detail settings.</param>
    /// <param name="metrics">The metric instruments.</param>
    /// <param name="traceCollector">The span collector.</param>
    /// <param name="timeProvider">The time source. The system clock when <see langword="null"/>.</param>
    /// <param name="graphOptions">The call tree limits. The defaults when <see langword="null"/>.</param>
    /// <param name="includeAgentVersionTag">Whether the agent version tag is added to spans and metrics.</param>
    /// <param name="pricingResolver">The cost resolver.</param>
    /// <param name="quotaEnforcer">The quota accountant.</param>
    /// <param name="webhookPublisher">The event publisher.</param>
    /// <param name="cancellationRegistry">The cancellation registry.</param>
    /// <param name="errorClassifier">The error classifier.</param>
    /// <param name="runInputStore">The input store.</param>
    /// <param name="runSampler">The online evaluation sampler.</param>
    /// <param name="contentGuardPipeline">The content guard pipeline.</param>
    /// <param name="attributionContext">The attribution context.</param>
    /// <param name="sinks">The run event observers.</param>
    /// <param name="approvalPresenterRunner">The tool approval presenter runner.</param>
    /// <param name="pricingCurrency">The currency non-token amounts add up in.</param>
    /// <param name="reconciliation">The run reconciliation settings.</param>
    /// <returns>The services.</returns>
    public static RunRecordingServices Services(
        IRunStore runStore,
        ITenantContext tenantContext,
        TraconRunRecordingOptions options,
        TraconMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        TimeProvider? timeProvider = null,
        TraconAgentGraphOptions? graphOptions = null,
        bool includeAgentVersionTag = true,
        IRunPricingResolver? pricingResolver = null,
        QuotaEnforcer? quotaEnforcer = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        IRunErrorClassifier? errorClassifier = null,
        IRunInputStore? runInputStore = null,
        RunSampler? runSampler = null,
        ContentGuardPipeline? contentGuardPipeline = null,
        IRunAttributionContext? attributionContext = null,
        IReadOnlyList<IRunEventSink>? sinks = null,
        ToolApprovalPresenterRunner? approvalPresenterRunner = null,
        string? pricingCurrency = null,
        RunReconciliationOptions? reconciliation = null)
        => new()
        {
            RunStore = runStore,
            TenantContext = tenantContext,
            Recording = options,
            Metrics = metrics,
            TraceCollector = traceCollector,
            TimeProvider = timeProvider ?? TimeProvider.System,
            AgentGraph = graphOptions ?? new TraconAgentGraphOptions(),
            IncludeAgentVersionTag = includeAgentVersionTag,
            PricingResolver = pricingResolver,
            QuotaEnforcer = quotaEnforcer,
            WebhookPublisher = webhookPublisher,
            CancellationRegistry = cancellationRegistry,
            ErrorClassifier = errorClassifier,
            RunInputStore = runInputStore,
            RunSampler = runSampler,
            ContentGuardPipeline = contentGuardPipeline,
            AttributionContext = attributionContext,
            Sinks = sinks is { Count: > 0 } ? sinks : [],
            ApprovalPresenterRunner = approvalPresenterRunner,
            PricingCurrency = pricingCurrency,
            Reconciliation = reconciliation,
        };

    /// <summary>Builds a run recorder around <paramref name="innerAgent"/>.</summary>
    /// <param name="innerAgent">The wrapped agent.</param>
    /// <param name="runStore">The store the events are written to.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">The recording detail settings.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric instruments.</param>
    /// <param name="traceCollector">The span collector.</param>
    /// <param name="modelId">The model the agent is bound to.</param>
    /// <param name="modelProvider">The provider of the model.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="graphOptions">The call tree limits.</param>
    /// <param name="agentVersion">The version of the agent's definition.</param>
    /// <param name="includeAgentVersionTag">Whether the agent version tag is added to spans and metrics.</param>
    /// <param name="pricingResolver">The cost resolver.</param>
    /// <param name="quotaEnforcer">The quota accountant.</param>
    /// <param name="webhookPublisher">The event publisher.</param>
    /// <param name="cancellationRegistry">The cancellation registry.</param>
    /// <param name="errorClassifier">The error classifier.</param>
    /// <param name="runInputStore">The input store.</param>
    /// <param name="runSampler">The online evaluation sampler.</param>
    /// <param name="contentGuardPipeline">The content guard pipeline.</param>
    /// <param name="attributionContext">The attribution context.</param>
    /// <param name="sinks">The run event observers.</param>
    /// <param name="approvalPresenterRunner">The tool approval presenter runner.</param>
    /// <param name="pricingCurrency">The currency non-token amounts add up in.</param>
    /// <returns>The recorder.</returns>
    public static RunRecordingAgent Agent(
        AIAgent innerAgent,
        IRunStore runStore,
        ITenantContext tenantContext,
        TraconRunRecordingOptions options,
        ILogger<RunRecordingAgent> logger,
        TraconMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        string? modelId = null,
        string? modelProvider = null,
        TimeProvider? timeProvider = null,
        TraconAgentGraphOptions? graphOptions = null,
        int? agentVersion = null,
        bool includeAgentVersionTag = true,
        IRunPricingResolver? pricingResolver = null,
        QuotaEnforcer? quotaEnforcer = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        IRunErrorClassifier? errorClassifier = null,
        IRunInputStore? runInputStore = null,
        RunSampler? runSampler = null,
        ContentGuardPipeline? contentGuardPipeline = null,
        IRunAttributionContext? attributionContext = null,
        IReadOnlyList<IRunEventSink>? sinks = null,
        ToolApprovalPresenterRunner? approvalPresenterRunner = null,
        string? pricingCurrency = null)
        => new(
            innerAgent,
            Services(
                runStore,
                tenantContext,
                options,
                metrics,
                traceCollector,
                timeProvider,
                graphOptions,
                includeAgentVersionTag,
                pricingResolver,
                quotaEnforcer,
                webhookPublisher,
                cancellationRegistry,
                errorClassifier,
                runInputStore,
                runSampler,
                contentGuardPipeline,
                attributionContext,
                sinks,
                approvalPresenterRunner,
                pricingCurrency),
            new RecordedAgentIdentity(modelId, modelProvider, agentVersion),
            logger);
}
