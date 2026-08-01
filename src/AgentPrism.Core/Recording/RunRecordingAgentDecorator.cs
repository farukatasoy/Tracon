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

    /// <summary>Yeni bir kayit dekoratoru olusturur.</summary>
    /// <param name="runStore">Olaylarin yazilacagi depo.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunRecordingAgentDecorator(
        IRunStore runStore,
        ITenantContext tenantContext,
        IOptions<AgentPrismOptions> options,
        ILogger<RunRecordingAgent> logger)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _runStore = runStore;
        _tenantContext = tenantContext;
        _options = options;
        _logger = logger;
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
            _logger);
    }
}
