using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Katalogdan cozulen her agent'i Microsoft Agent Framework'un
/// <see cref="OpenTelemetryAgent"/> sarmalayicisiyla sarar.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Order"/> degeri 10'dur: <see cref="RunRecordingAgentDecorator"/>
/// (0) disinda, tool onayi (20) icinde kalir. Boylece uretilen
/// <c>invoke_agent</c> span'i <c>agentprism.run</c> span'inin cocugu olur ve
/// waterfall gorunumu dogru hiyerarsiyi gosterir.
/// </para>
/// <para>
/// <c>autoWireChatClient: false</c> geciliyor. Sohbet istemcisi boru hattinda
/// zaten <c>UseOpenTelemetry(AgentPrismDiagnostics.ActivitySourceName)</c> var
/// (bkz. <c>OpenAIChatClientFactory</c>); otomatik baglama ayni istemciyi ikinci
/// kez sarar ve her model cagrisi icin cift span uretirdi.
/// </para>
/// </remarks>
public sealed class OpenTelemetryAgentDecorator : IAgentDecorator
{
    private readonly IOptions<AgentPrismOptions> _options;

    /// <summary>Yeni bir telemetri dekoratoru olusturur.</summary>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> <see langword="null"/> ise.</exception>
    public OpenTelemetryAgentDecorator(IOptions<AgentPrismOptions> options)
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

        // MAAI001: OpenTelemetryAgent "evaluation purposes only" isaretli.
        // Bastirma bilincli: kullanim tek bir dosyada toplandi, MAF bu API'yi
        // degistirirse yalnizca burasi guncellenir.
        // Gerekce: docs/KARARLAR.md, karar K-055.
#pragma warning disable MAAI001
        return new OpenTelemetryAgent(
            agent,
            AgentPrismDiagnostics.ActivitySourceName,
            autoWireChatClient: false)
        {
            EnableSensitiveData = settings.RecordSensitiveData,
        };
#pragma warning restore MAAI001
    }
}
