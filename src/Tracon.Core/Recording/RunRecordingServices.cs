using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// The services and settings that every recorder of a run shares: the agent recorder, the workflow
/// runner and the model judge write their run rows with the same store, clock, metrics, quota counter,
/// webhook publisher and settings.
/// </summary>
/// <remarks>
/// <para>
/// This type holds what used to be a list of constructor parameters repeated by the recorder, the
/// decorator that builds it and the workflow runner. A new dependency of the run path is added here
/// once, instead of being added to each constructor and to each place that forwards it.
/// </para>
/// <para>
/// <strong>An optional member that is <see langword="null"/> switches a feature off without a sound.</strong>
/// A missing quota counter means consumption is not counted; a missing webhook publisher means no
/// <c>run.*</c> event leaves the process. The container fills every member
/// (<see cref="Create"/> resolves them all), so the build and the tests stay green when a member is
/// left out of a hand-built instance. A test that wants a feature builds it in on purpose.
/// </para>
/// <para>
/// The settings are read from <see cref="TraconOptions"/> when the instance is created, not at every
/// run, exactly as the options object itself is built once.
/// </para>
/// </remarks>
internal sealed class RunRecordingServices
{
    /// <summary>Gets the store that the events and the run row are written to.</summary>
    public required IRunStore RunStore { get; init; }

    /// <summary>Gets the tenant context. The tenant of a run is read from it when the run starts.</summary>
    public required ITenantContext TenantContext { get; init; }

    /// <summary>Gets the recording detail settings.</summary>
    public required TraconRunRecordingOptions Recording { get; init; }

    /// <summary>Gets the time source.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    /// <summary>Gets the metric instruments. When <see langword="null"/>, no metric is emitted.</summary>
    public TraconMetrics? Metrics { get; init; }

    /// <summary>Gets the span collector. When <see langword="null"/>, no span is written.</summary>
    public RunTraceCollector? TraceCollector { get; init; }

    /// <summary>Gets the cost resolver. When <see langword="null"/>, no cost is calculated.</summary>
    public IRunPricingResolver? PricingResolver { get; init; }

    /// <summary>
    /// Gets the quota accountant. When <see langword="null"/>, consumption is not counted. Only root
    /// runs are counted; the runs below a root are part of the same request.
    /// </summary>
    public QuotaEnforcer? QuotaEnforcer { get; init; }

    /// <summary>Gets the event publisher. When <see langword="null"/>, no <c>run.*</c> event is published.</summary>
    public IWebhookPublisher? WebhookPublisher { get; init; }

    /// <summary>
    /// Gets the cancellation registry. When <see langword="null"/>, a run cannot be canceled from outside.
    /// </summary>
    public IRunCancellationRegistry? CancellationRegistry { get; init; }

    /// <summary>
    /// Gets the error classifier. When <see langword="null"/>, a failed run carries no error class and no fingerprint.
    /// </summary>
    public IRunErrorClassifier? ErrorClassifier { get; init; }

    /// <summary>
    /// Gets the input store. When <see langword="null"/>, the input of a run is not recorded and the run cannot be replayed.
    /// </summary>
    public IRunInputStore? RunInputStore { get; init; }

    /// <summary>Gets the online evaluation sampler. When <see langword="null"/>, no run is sampled.</summary>
    public RunSampler? RunSampler { get; init; }

    /// <summary>
    /// Gets the content guard pipeline. When <see langword="null"/>, the input is recorded as it came.
    /// When it is set, the recorded input passes through the same inspection as the text that goes to the model.
    /// </summary>
    public ContentGuardPipeline? ContentGuardPipeline { get; init; }

    /// <summary>
    /// Gets the attribution context. When <see langword="null"/>, a run records no user and no labels.
    /// </summary>
    public IRunAttributionContext? AttributionContext { get; init; }

    /// <summary>Gets the run event observers. Empty when none is registered.</summary>
    public IReadOnlyList<IRunEventSink> Sinks { get; init; } = [];

    /// <summary>
    /// Gets the runner that resolves the presentation of a pending tool approval. When <see langword="null"/>,
    /// no presentation is resolved.
    /// </summary>
    public ToolApprovalPresenterRunner? ApprovalPresenterRunner { get; init; }

    /// <summary>Gets the limits of the call tree. The defaults apply when none is configured.</summary>
    public TraconAgentGraphOptions AgentGraph { get; init; } = new();

    /// <summary>
    /// Gets whether the agent version tag is added to the span and to the metrics. See
    /// <see cref="TraconObservabilityOptions.IncludeAgentVersionTag"/>.
    /// </summary>
    public bool IncludeAgentVersionTag { get; init; } = true;

    /// <summary>
    /// Gets the currency the amounts that a tool or a function node reports add up in.
    /// <see langword="null"/> when none is configured.
    /// </summary>
    public string? PricingCurrency { get; init; }

    /// <summary>
    /// Gets the run reconciliation settings. A run that waits for a concurrency slot writes its first
    /// heartbeat at once only when reconciliation is on; when this is <see langword="null"/> it does.
    /// </summary>
    public RunReconciliationOptions? Reconciliation { get; init; }

    /// <summary>
    /// Builds the services from the container.
    /// </summary>
    /// <remarks>
    /// Every member the run path needs is resolved with <c>GetRequiredService</c>. The built-in container
    /// does not fill a parameter that carries a default value, so a member left out here stays
    /// <see langword="null"/> and its feature is off while the build and the tests stay green: a missing
    /// quota counter was once exactly that.
    /// </remarks>
    /// <param name="provider">The container.</param>
    /// <returns>The services.</returns>
    public static RunRecordingServices Create(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var options = provider.GetRequiredService<IOptions<TraconOptions>>().Value;

        return new RunRecordingServices
        {
            RunStore = provider.GetRequiredService<IRunStore>(),
            TenantContext = provider.GetRequiredService<ITenantContext>(),
            Recording = options.RunRecording,

            // TimeProvider may not be registered: the clock of the system is the default.
            TimeProvider = provider.GetService<TimeProvider>() ?? TimeProvider.System,
            Metrics = provider.GetRequiredService<TraconMetrics>(),
            TraceCollector = provider.GetRequiredService<RunTraceCollector>(),
            PricingResolver = provider.GetRequiredService<IRunPricingResolver>(),

            // Without these two members the quota counter never increases and no run.* event is
            // published, while the build and the tests look green.
            QuotaEnforcer = provider.GetRequiredService<QuotaEnforcer>(),
            WebhookPublisher = provider.GetRequiredService<IWebhookPublisher>(),
            CancellationRegistry = provider.GetRequiredService<IRunCancellationRegistry>(),
            ErrorClassifier = provider.GetRequiredService<IRunErrorClassifier>(),
            RunInputStore = provider.GetRequiredService<IRunInputStore>(),

            // Being registered alone samples nothing; see the RunSampler and OnlineEvaluationOptions documentation.
            RunSampler = provider.GetRequiredService<RunSampler>(),

            // Without this member the input written to the RunStarted event and to the input store never
            // passes through the guards.
            ContentGuardPipeline = provider.GetRequiredService<ContentGuardPipeline>(),

            // Without this member every run records a null user and null labels.
            AttributionContext = provider.GetRequiredService<IRunAttributionContext>(),

            // GetServices never throws when no IRunEventSink is registered: it returns an empty sequence.
            Sinks = provider.GetServices<IRunEventSink>().ToArray(),

            // Without this member no ToolApprovalPresentation is resolved, even when a consumer
            // registered its own IToolApprovalPresenter.
            ApprovalPresenterRunner = provider.GetRequiredService<ToolApprovalPresenterRunner>(),
            AgentGraph = options.AgentGraph,
            IncludeAgentVersionTag = options.Observability.IncludeAgentVersionTag,
            PricingCurrency = options.Pricing.Currency,
            Reconciliation = provider.GetService<IOptions<RunReconciliationOptions>>()?.Value,
        };
    }

    /// <summary>
    /// Creates the writer of one run's events.
    /// </summary>
    /// <remarks>
    /// <strong>Call this in the body of the method that opens the run, not in an async helper.</strong> The
    /// scope that holds the writer is written into an <c>AsyncLocal</c>, and a write made inside an async
    /// method does not flow back to the caller: a writer made there would be invisible to a child call.
    /// </remarks>
    /// <param name="runId">The identifier of the run.</param>
    /// <param name="logger">The logger of the recorder that owns the run.</param>
    /// <param name="runIdIsNew">Whether the recorder made <paramref name="runId"/> itself, rather than taking one a caller chose.</param>
    /// <returns>The writer.</returns>
    public RunEventWriter CreateWriter(Guid runId, ILogger logger, bool runIdIsNew = false)
        => new(RunStore, Recording, logger, runId, Metrics, Sinks, TimeProvider, runIdIsNew);

    /// <summary>
    /// Returns the services for a run that the control plane starts on its own account, such as the
    /// call of a model judge.
    /// </summary>
    /// <remarks>
    /// The run still writes its row, its events, its cost and its metrics. It does not count against a
    /// quota, publish a webhook, register for cancellation, take part in online evaluation, reach the
    /// event sinks or the content guard, record an attribution or a tool approval, or carry a version tag:
    /// a judge that did any of these would charge the user for its own check, publish noise, or sample
    /// itself.
    /// </remarks>
    /// <returns>The services for such a run.</returns>
    public RunRecordingServices ForControlPlaneRun()
        => new()
        {
            RunStore = RunStore,
            TenantContext = TenantContext,
            Recording = Recording,
            TimeProvider = TimeProvider,
            Metrics = Metrics,
            TraceCollector = TraceCollector,
            PricingResolver = PricingResolver,
            ErrorClassifier = ErrorClassifier,
            RunInputStore = RunInputStore,
            AgentGraph = AgentGraph,
            IncludeAgentVersionTag = false,
        };
}
