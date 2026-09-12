using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Wraps every agent resolved from the catalog with Microsoft Agent Framework's
/// <see cref="OpenTelemetryAgent"/> decorator.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Order"/> is 10. It is outside <see cref="RunRecordingAgentDecorator"/>
/// at 0 and inside tool approval at 20. The generated <c>invoke_agent</c> span is
/// therefore a child of <c>tracon.run</c>, and the waterfall view shows the right hierarchy.
/// </para>
/// <para>
/// Passes <c>autoWireChatClient: false</c>. The chat client pipeline already uses
/// <c>UseOpenTelemetry(TraconDiagnostics.ActivitySourceName)</c>. See
/// <c>OpenAIChatClientFactory</c>. Automatic wiring would wrap the same client twice
/// and produce duplicate spans for each model call.
/// </para>
/// </remarks>
internal sealed class OpenTelemetryAgentDecorator : IAgentDecorator
{
    private readonly IOptions<TraconOptions> _options;

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

        // MAAI001: OpenTelemetryAgent is marked "evaluation purposes only".
        // The suppression is deliberate. This is the only use, so a MAF API change
        // only requires an update here. Rationale: docs/KARARLAR.md, decision K-055.
#pragma warning disable MAAI001
        return new OpenTelemetryAgent(
            agent,
            TraconDiagnostics.ActivitySourceName,
            autoWireChatClient: false)
        {
            EnableSensitiveData = settings.RecordSensitiveData,
        };
#pragma warning restore MAAI001
    }
}
