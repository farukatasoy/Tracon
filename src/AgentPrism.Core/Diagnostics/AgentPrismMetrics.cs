using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentPrism;

/// <summary>
/// AgentPrism'in yaydigi metrikler. Tuketici <c>AddMeter(AgentPrismDiagnostics.MeterName)</c>
/// ile toplar.
/// </summary>
/// <remarks>
/// <para>
/// Olcum aletleri <see cref="IMeterFactory"/> uzerinden kurulur; boylece test
/// icinde yalitilabilirler. Fabrika kayitli degilse dogrudan bir
/// <see cref="Meter"/> olusturulur — kutuphane, tuketiciyi
/// <c>AddMetrics()</c> cagirmaya zorlamaz.
/// </para>
/// <para>
/// Sureler <strong>saniye</strong> cinsindendir. OpenTelemetry semantic
/// convention'i histogram sureleri icin saniyeyi zorunlu kilar; milisaniye
/// yazmak hazir gosterge panolarini bozar.
/// </para>
/// </remarks>
public sealed class AgentPrismMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    /// <summary>Yeni bir metrik kumesi olusturur.</summary>
    /// <param name="meterFactory">
    /// Olcum fabrikasi. <see langword="null"/> ise kendi <see cref="Meter"/>
    /// ornegi olusturulur ve sahipligi bu nesneye ait olur.
    /// </param>
    public AgentPrismMetrics(IMeterFactory? meterFactory = null)
    {
        if (meterFactory is null)
        {
            _meter = new Meter(AgentPrismDiagnostics.MeterName);
            _ownsMeter = true;
        }
        else
        {
            _meter = meterFactory.Create(AgentPrismDiagnostics.MeterName);
        }

        Runs = _meter.CreateCounter<long>(
            AgentPrismDiagnostics.RunCounterName,
            unit: "{run}",
            description: "Sonuclanmis calistirma sayisi.");

        RunDuration = _meter.CreateHistogram<double>(
            AgentPrismDiagnostics.RunDurationName,
            unit: "s",
            description: "Calistirma suresi.");

        Tokens = _meter.CreateCounter<long>(
            AgentPrismDiagnostics.TokenCounterName,
            unit: "{token}",
            description: "Saglayicinin bildirdigi token sayisi.");

        ToolInvocations = _meter.CreateCounter<long>(
            AgentPrismDiagnostics.ToolCounterName,
            unit: "{call}",
            description: "Sonuclanmis tool cagrisi sayisi.");

        ToolDuration = _meter.CreateHistogram<double>(
            AgentPrismDiagnostics.ToolDurationName,
            unit: "s",
            description: "Tool cagri suresi.");

        RunCost = _meter.CreateCounter<double>(
            AgentPrismDiagnostics.RunCostCounterName,
            unit: "{cost}",
            description: "Calistirma basina para cinsinden maliyet. Agac toplamini icermez (K-151).");
    }

    /// <summary>Calistirma sayaci. Etiketler: agent, status, tenant.</summary>
    public Counter<long> Runs { get; }

    /// <summary>Calistirma suresi. Etiketler: agent, status.</summary>
    public Histogram<double> RunDuration { get; }

    /// <summary>Token sayaci. Etiketler: agent, model, direction.</summary>
    public Counter<long> Tokens { get; }

    /// <summary>Tool cagri sayaci. Etiketler: tool, status.</summary>
    public Counter<long> ToolInvocations { get; }

    /// <summary>Tool cagri suresi. Etiketler: tool.</summary>
    public Histogram<double> ToolDuration { get; }

    /// <summary>Maliyet sayaci. Etiketler: agent, model, tenant, currency.</summary>
    public Counter<double> RunCost { get; }

    /// <summary>Bir calistirmanin sonucunu kaydeder.</summary>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="status">Son durum.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="modelId">Kullanilan model.</param>
    /// <param name="duration">Calistirma suresi.</param>
    /// <param name="usage">Token kullanimi.</param>
    /// <param name="agentVersion">
    /// Olculen tanim surumu. <see langword="null"/> ise etiket eklenmez — bilinmedigi
    /// veya <see cref="AgentPrismObservabilityOptions.IncludeAgentVersionTag"/> kapali
    /// oldugu icin cagiran taraf zaten <see langword="null"/> geciyordur.
    /// </param>
    public void RecordRun(
        string agentName,
        RunStatus status,
        string tenantId,
        string? modelId,
        TimeSpan duration,
        RunUsage? usage,
        int? agentVersion = null)
    {
        var statusTag = status.ToString();

        var runTags = new TagList
        {
            { AgentPrismDiagnostics.Tags.AgentName, agentName },
            { AgentPrismDiagnostics.Tags.Status, statusTag },
            { AgentPrismDiagnostics.Tags.TenantId, tenantId },
        };

        var durationTags = new TagList
        {
            { AgentPrismDiagnostics.Tags.AgentName, agentName },
            { AgentPrismDiagnostics.Tags.Status, statusTag },
        };

        if (agentVersion is { } version)
        {
            runTags.Add(AgentPrismDiagnostics.Tags.AgentVersion, version);
            durationTags.Add(AgentPrismDiagnostics.Tags.AgentVersion, version);
        }

        Runs.Add(1, runTags);

        RunDuration.Record(duration.TotalSeconds, durationTags);

        if (usage is null)
        {
            return;
        }

        // Model etiketi bos gecilmez: OpenTelemetry'de eksik etiket ayri bir
        // zaman serisi uretir ve toplamalar sessizce ikiye bolunur.
        var model = modelId ?? "unknown";

        if (usage.InputTokens is { } input)
        {
            AddTokens(agentName, model, "input", input);
        }

        if (usage.OutputTokens is { } output)
        {
            AddTokens(agentName, model, "output", output);
        }
    }

    /// <summary>Bir calistirmanin kendi maliyetini kaydeder (fiyat tanimliyken).</summary>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="modelId">Kullanilan model. <see langword="null"/> ise <c>"unknown"</c> yazilir.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cost">Calistirmanin KENDI maliyeti (girdi + cikti). Agac toplami DEGILDIR (K-151).</param>
    /// <param name="currency">Para birimi.</param>
    public void RecordCost(string agentName, string? modelId, string tenantId, decimal cost, string currency)
        => RunCost.Add(
            (double)cost,
            new TagList
            {
                { AgentPrismDiagnostics.Tags.AgentName, agentName },
                { AgentPrismDiagnostics.Tags.ModelId, modelId ?? "unknown" },
                { AgentPrismDiagnostics.Tags.TenantId, tenantId },
                { AgentPrismDiagnostics.Tags.Currency, currency },
            });

    /// <summary>Bir tool cagrisinin sonucunu kaydeder.</summary>
    /// <param name="toolName">Tool adi.</param>
    /// <param name="succeeded">Cagri basarili mi bitti.</param>
    /// <param name="duration">Cagri suresi. Bilinmiyorsa <see langword="null"/>.</param>
    public void RecordToolInvocation(string toolName, bool succeeded, TimeSpan? duration)
    {
        var statusTag = succeeded ? "ok" : "error";

        ToolInvocations.Add(
            1,
            new KeyValuePair<string, object?>(AgentPrismDiagnostics.Tags.ToolName, toolName),
            new KeyValuePair<string, object?>(AgentPrismDiagnostics.Tags.Status, statusTag));

        if (duration is { } elapsed)
        {
            ToolDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>(AgentPrismDiagnostics.Tags.ToolName, toolName));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsMeter)
        {
            _meter.Dispose();
        }
    }

    private void AddTokens(string agentName, string modelId, string direction, long count)
        => Tokens.Add(
            count,
            new KeyValuePair<string, object?>(AgentPrismDiagnostics.Tags.AgentName, agentName),
            new KeyValuePair<string, object?>(AgentPrismDiagnostics.Tags.ModelId, modelId),
            new KeyValuePair<string, object?>(AgentPrismDiagnostics.Tags.Direction, direction));
}
