using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon.Tests.Common;

/// <summary>
/// Builds the workflow runner with only the dependencies a test names: every optional one that is left out
/// stays off, which is the opposite of the container.
/// </summary>
internal static class TestWorkflowRunner
{
    /// <summary>Builds a workflow runner.</summary>
    /// <param name="catalog">The workflow catalog.</param>
    /// <param name="runStore">The run record store.</param>
    /// <param name="checkpointStore">The checkpoint store.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">The workflow settings.</param>
    /// <param name="traconOptions">The general Tracon settings. The recording and call tree settings are read from them.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric instruments.</param>
    /// <param name="traceCollector">The span collector.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="cancellationRegistry">The cancellation registry.</param>
    /// <param name="quotaEnforcer">The quota enforcer.</param>
    /// <param name="attributionContext">The attribution context.</param>
    /// <param name="sinks">The run event observers.</param>
    /// <param name="errorClassifier">The error classifier.</param>
    /// <param name="webhookPublisher">The webhook publisher.</param>
    /// <param name="runInputStore">The input store.</param>
    /// <param name="parameterFactory">The parameterized agent factory.</param>
    /// <param name="responseStore">The answer store.</param>
    /// <param name="requestStore">The request inbox.</param>
    /// <param name="approvalPresenter">The tool approval presenter runner.</param>
    /// <param name="agentCache">The workflow agent cache.</param>
    /// <param name="reconciliationOptions">The run reconciliation settings.</param>
    /// <returns>The runner.</returns>
    public static WorkflowRunner Create(
        WorkflowCatalog catalog,
        IRunStore runStore,
        IWorkflowCheckpointStore checkpointStore,
        ITenantContext tenantContext,
        IOptions<TraconWorkflowOptions> options,
        IOptions<TraconOptions> traconOptions,
        ILogger<WorkflowRunner> logger,
        TraconMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        TimeProvider? timeProvider = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        QuotaEnforcer? quotaEnforcer = null,
        IRunAttributionContext? attributionContext = null,
        IEnumerable<IRunEventSink>? sinks = null,
        IRunErrorClassifier? errorClassifier = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunInputStore? runInputStore = null,
        ParameterizedAgentFactory? parameterFactory = null,
        IWorkflowResponseStore? responseStore = null,
        IWorkflowRequestStore? requestStore = null,
        ToolApprovalPresenterRunner? approvalPresenter = null,
        WorkflowAgentCache? agentCache = null,
        IOptions<RunReconciliationOptions>? reconciliationOptions = null)
        => new(
            catalog,
            checkpointStore,
            options,
            traconOptions,
            logger,
            TestRunRecording.Services(
                runStore,
                tenantContext,
                traconOptions.Value.RunRecording,
                metrics,
                traceCollector,
                timeProvider,
                traconOptions.Value.AgentGraph,
                pricingResolver: null,
                quotaEnforcer: quotaEnforcer,
                webhookPublisher: webhookPublisher,
                cancellationRegistry: cancellationRegistry,
                errorClassifier: errorClassifier,
                runInputStore: runInputStore,
                attributionContext: attributionContext,
                sinks: sinks?.ToArray(),
                approvalPresenterRunner: approvalPresenter,
                reconciliation: reconciliationOptions?.Value),
            parameterFactory,
            responseStore,
            requestStore,
            agentCache);
}
