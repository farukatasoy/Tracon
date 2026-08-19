using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Wraps every agent resolved from the catalog with <see cref="RunRecordingAgent"/>.
/// </summary>
/// <remarks>
/// <see cref="Order"/> is 0, so run recording is the outermost wrapper and
/// also measures the time spent by inner wrappers.
/// </remarks>
public sealed class RunRecordingAgentDecorator : IAgentDecorator
{
    private readonly IRunStore _runStore;
    private readonly ITenantContext _tenantContext;
    private readonly IOptions<AgentPrismOptions> _options;
    private readonly ILogger<RunRecordingAgent> _logger;
    private readonly AgentPrismMetrics? _metrics;
    private readonly RunTraceCollector? _traceCollector;
    private readonly TimeProvider? _timeProvider;
    private readonly IRunPricingResolver? _pricingResolver;
    private readonly QuotaEnforcer? _quotaEnforcer;
    private readonly IWebhookPublisher? _webhookPublisher;
    private readonly IRunCancellationRegistry? _cancellationRegistry;
    private readonly IRunErrorClassifier? _errorClassifier;
    private readonly IRunInputStore? _runInputStore;
    private readonly RunSampler? _runSampler;
    private readonly ContentGuardPipeline? _contentGuardPipeline;
    private readonly IRunAttributionContext? _attributionContext;

    /// <summary>Creates a new recording decorator.</summary>
    /// <param name="runStore">The store the events are written to.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">The AgentPrism settings.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="metrics">The metric instruments.</param>
    /// <param name="traceCollector">The span collector.</param>
    /// <param name="timeProvider">The time source.</param>
    /// <param name="pricingResolver">The cost resolver. If <see langword="null"/>, cost is not computed.</param>
    /// <param name="quotaEnforcer">The quota accountant. If <see langword="null"/>, consumption is not counted.</param>
    /// <param name="webhookPublisher">The event publisher. If <see langword="null"/>, no events are published.</param>
    /// <param name="cancellationRegistry">The cancellation registry. If <see langword="null"/>, the run cannot be canceled from outside.</param>
    /// <param name="errorClassifier">The error classifier. If <see langword="null"/>, no error class/fingerprint is computed.</param>
    /// <param name="runInputStore">The input store. If <see langword="null"/>, input is not recorded and replay does not work.</param>
    /// <param name="runSampler">The online evaluation sampler (Phase 49). If <see langword="null"/>, no run is sampled.</param>
    /// <param name="contentGuardPipeline">
    /// The content guard pipeline (Phase 48). If <see langword="null"/>, recorded
    /// input is written without inspection. See the note in the <see cref="RunRecordingAgent"/> constructor (HATA-S3-006).
    /// </param>
    /// <param name="attributionContext">
    /// The attribution context (Phase 68). If <see langword="null"/>, the run records no user and no labels.
    /// </param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    public RunRecordingAgentDecorator(
        IRunStore runStore,
        ITenantContext tenantContext,
        IOptions<AgentPrismOptions> options,
        ILogger<RunRecordingAgent> logger,
        AgentPrismMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        TimeProvider? timeProvider = null,
        IRunPricingResolver? pricingResolver = null,
        QuotaEnforcer? quotaEnforcer = null,
        IWebhookPublisher? webhookPublisher = null,
        IRunCancellationRegistry? cancellationRegistry = null,
        IRunErrorClassifier? errorClassifier = null,
        IRunInputStore? runInputStore = null,
        RunSampler? runSampler = null,
        ContentGuardPipeline? contentGuardPipeline = null,
        IRunAttributionContext? attributionContext = null)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _runStore = runStore;
        _tenantContext = tenantContext;
        _options = options;
        _logger = logger;
        _metrics = metrics;
        _traceCollector = traceCollector;
        _timeProvider = timeProvider;
        _pricingResolver = pricingResolver;
        _quotaEnforcer = quotaEnforcer;
        _webhookPublisher = webhookPublisher;
        _cancellationRegistry = cancellationRegistry;
        _errorClassifier = errorClassifier;
        _runInputStore = runInputStore;
        _runSampler = runSampler;
        _contentGuardPipeline = contentGuardPipeline;
        _attributionContext = attributionContext;
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(agent);

        return new RunRecordingAgent(
            agent,
            _runStore,
            _tenantContext,
            _options.Value.RunRecording,
            _logger,
            _metrics,
            _traceCollector,
            // The model name comes from the catalog descriptor. It may be
            // unknown for code-defined agents; in that case the run record
            // carries no model and is excluded from the model breakdown.
            descriptor?.Model?.Model,
            // The provider is used only for cost resolution and is not
            // persisted (see docs/KARARLAR.md K-154).
            descriptor?.Model?.Provider,
            _timeProvider,
            _options.Value.AgentGraph,
            descriptor?.Version,
            _options.Value.Observability.IncludeAgentVersionTag,
            _pricingResolver,
            _quotaEnforcer,
            _webhookPublisher,
            _cancellationRegistry,
            _errorClassifier,
            _runInputStore,
            _runSampler,
            _contentGuardPipeline,
            _attributionContext);
    }
}
