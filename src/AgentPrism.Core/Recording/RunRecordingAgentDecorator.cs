using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Katalogdan cozulen her agent'i <see cref="RunRecordingAgent"/> ile sarar.
/// </summary>
/// <remarks>
/// <see cref="Order"/> degeri 0'dir; boylece calistirma kaydi en distaki
/// sarmalayici olur ve ic sarmalayicilarin harcadigi sureyi de olcer.
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

    /// <summary>Yeni bir kayit dekoratoru olusturur.</summary>
    /// <param name="runStore">Olaylarin yazilacagi depo.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="metrics">Metrik aletleri.</param>
    /// <param name="traceCollector">Span toplayici.</param>
    /// <param name="timeProvider">Zaman kaynagi.</param>
    /// <param name="pricingResolver">Maliyet cozumleyici. <see langword="null"/> ise maliyet hesaplanmaz.</param>
    /// <param name="quotaEnforcer">Kota muhasebecisi. <see langword="null"/> ise tuketim sayilmaz.</param>
    /// <param name="webhookPublisher">Olay yayincisi. <see langword="null"/> ise olay yayilmaz.</param>
    /// <param name="cancellationRegistry">Iptal defteri. <see langword="null"/> ise calistirma disaridan iptal edilemez.</param>
    /// <param name="errorClassifier">Hata siniflandirici. <see langword="null"/> ise hata sinifi/parmak izi hesaplanmaz.</param>
    /// <param name="runInputStore">Girdi deposu. <see langword="null"/> ise girdi kaydedilmez ve yeniden oynatma calismaz.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
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
        IRunInputStore? runInputStore = null)
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
            // Model adi katalog ozetinden gelir. Kod agent'larinda bilinmeyebilir;
            // o durumda calistirma kaydi model tasimaz ve model kirilimina girmez.
            descriptor?.Model?.Model,
            // Saglayici yalniz maliyet cozumlemesinde kullanilir, kalicilastirilmaz
            // (bkz. docs/KARARLAR.md K-154).
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
            _runInputStore);
    }
}
