using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Wraps every agent resolved from the catalog so Microsoft Agent Framework's <see cref="OpenTelemetryAgent"/> writes its
/// telemetry.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Order"/> is 10. It is outside <see cref="RunRecordingAgentDecorator"/>
/// at 0 and inside tool approval at 20. The generated <c>invoke_agent</c> span is
/// therefore a child of <c>tracon.run</c>, and the waterfall view shows the right hierarchy.
/// </para>
/// <para>
/// The catalog decorates the agent again on every resolve, which is every run. The telemetry agent is therefore not built
/// here: building one makes a meter that lives as long as the process, and a run is not a reason to make a meter.
/// <see cref="TelemetryLanePool"/> lends the lanes, and <see cref="PooledOpenTelemetryAgent"/> takes one for the length of
/// each call.
/// </para>
/// <para>
/// The lanes pass <c>autoWireChatClient: false</c>. The chat client pipeline already uses
/// <c>UseOpenTelemetry(TraconDiagnostics.ActivitySourceName)</c>. See
/// <c>OpenAIChatClientFactory</c>. Automatic wiring would wrap the same client twice
/// and produce duplicate spans for each model call.
/// </para>
/// </remarks>
internal sealed class OpenTelemetryAgentDecorator : IAgentDecorator, IDisposable
{
    private readonly IOptions<TraconOptions> _options;
    private TelemetryLanePool? _pool;

    /// <summary>Initializes a new telemetry decorator.</summary>
    /// <param name="options">The Tracon options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public OpenTelemetryAgentDecorator(IOptions<TraconOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(agent);

        var settings = _options.Value.Observability;

        if (!settings.Enabled)
        {
            return agent;
        }

        var pool = Volatile.Read(ref _pool) ?? CreatePool(settings.RecordSensitiveData);

        return new PooledOpenTelemetryAgent(agent, pool);
    }

    /// <inheritdoc />
    public void Dispose() => _pool?.Dispose();

    private TelemetryLanePool CreatePool(bool recordSensitiveData)
    {
        var created = new TelemetryLanePool(TraconDiagnostics.ActivitySourceName, recordSensitiveData);
        var existing = Interlocked.CompareExchange(ref _pool, created, null);

        if (existing is null)
        {
            return created;
        }

        // A pool makes no meter until a lane is lent, so the loser of the race has nothing to give back.
        created.Dispose();

        return existing;
    }
}
