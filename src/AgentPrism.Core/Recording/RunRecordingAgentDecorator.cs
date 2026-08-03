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

    /// <summary>Yeni bir kayit dekoratoru olusturur.</summary>
    /// <param name="runStore">Olaylarin yazilacagi depo.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="metrics">Metrik aletleri.</param>
    /// <param name="traceCollector">Span toplayici.</param>
    /// <param name="timeProvider">Zaman kaynagi.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunRecordingAgentDecorator(
        IRunStore runStore,
        ITenantContext tenantContext,
        IOptions<AgentPrismOptions> options,
        ILogger<RunRecordingAgent> logger,
        AgentPrismMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        TimeProvider? timeProvider = null)
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
            _timeProvider,
            _options.Value.AgentGraph,
            descriptor?.Version,
            _options.Value.Observability.IncludeAgentVersionTag);
    }
}
