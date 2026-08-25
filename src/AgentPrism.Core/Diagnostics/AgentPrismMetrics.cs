using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentPrism;

/// <summary>
/// Metrics emitted by AgentPrism. Consumers collect them with
/// <c>AddMeter(AgentPrismDiagnostics.MeterName)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Instruments are set up via <see cref="IMeterFactory"/>, which allows them
/// to be isolated in tests. When the factory is not registered, a
/// <see cref="Meter"/> is created directly - the library does not force the
/// consumer to call <c>AddMetrics()</c>.
/// </para>
/// <para>
/// Durations are in <strong>seconds</strong>. The OpenTelemetry semantic
/// convention mandates seconds for histogram durations; writing milliseconds
/// would break ready-made dashboards.
/// </para>
/// </remarks>
public sealed class AgentPrismMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly bool _ownsMeter;

    /// <summary>Creates a new metric set.</summary>
    /// <param name="meterFactory">
    /// Meter factory. When <see langword="null"/>, a new <see cref="Meter"/>
    /// instance is created and owned by this object.
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
            description: "Number of completed runs.");

        RunDuration = _meter.CreateHistogram<double>(
            AgentPrismDiagnostics.RunDurationName,
            unit: "s",
            description: "Run duration.");

        Tokens = _meter.CreateCounter<long>(
            AgentPrismDiagnostics.TokenCounterName,
            unit: "{token}",
            description: "Token count reported by the provider.");

        ToolInvocations = _meter.CreateCounter<long>(
            AgentPrismDiagnostics.ToolCounterName,
            unit: "{call}",
            description: "Number of completed tool calls.");

        ToolDuration = _meter.CreateHistogram<double>(
            AgentPrismDiagnostics.ToolDurationName,
            unit: "s",
            description: "Tool call duration.");

        RunCost = _meter.CreateCounter<double>(
            AgentPrismDiagnostics.RunCostCounterName,
            unit: "{cost}",
            description: "Cost per run, in currency units. Does not include the tree total (K-151).");

        JudgeCost = _meter.CreateCounter<double>(
            AgentPrismDiagnostics.JudgeCostCounterName,
            unit: "{cost}",
            description: "The OWN cost of a single IRunJudge call (Phase 49). Not included in the scored agent's cost.");

        JudgeScore = _meter.CreateHistogram<double>(
            AgentPrismDiagnostics.JudgeScoreHistogramName,
            unit: "{score}",
            description: "Score given by an IRunJudge, 0-100 (Phase 49).");

        ModelCacheLookups = _meter.CreateCounter<long>(
            AgentPrismDiagnostics.ModelCacheLookupCounterName,
            unit: "{lookup}",
            description: "Response-cache lookups, tagged hit or miss.");

        AgentSourceFailures = _meter.CreateCounter<long>(
            AgentPrismDiagnostics.AgentSourceFailureCounterName,
            unit: "{failure}",
            description: "Agent-source failures, tagged by source and operation.");
    }

    /// <summary>Run counter. Tags: agent, status, tenant.</summary>
    public Counter<long> Runs { get; }

    /// <summary>Run duration. Tags: agent, status.</summary>
    public Histogram<double> RunDuration { get; }

    /// <summary>Token counter. Tags: agent, model, direction.</summary>
    public Counter<long> Tokens { get; }

    /// <summary>Tool call counter. Tags: tool, status.</summary>
    public Counter<long> ToolInvocations { get; }

    /// <summary>Tool call duration. Tags: tool.</summary>
    public Histogram<double> ToolDuration { get; }

    /// <summary>Cost counter. Tags: agent, model, tenant, currency.</summary>
    public Counter<double> RunCost { get; }

    /// <summary>Judge cost counter. Tags: judge, model, tenant, currency.</summary>
    public Counter<double> JudgeCost { get; }

    /// <summary>Judge score histogram. Tags: judge, agent, tenant.</summary>
    public Histogram<double> JudgeScore { get; }

    /// <summary>Response-cache lookup counter. Tags: provider, tenant, result (hit/miss).</summary>
    public Counter<long> ModelCacheLookups { get; }

    /// <summary>Agent-source failure counter. Tags: source, operation.</summary>
    public Counter<long> AgentSourceFailures { get; }

    /// <summary>Records the result of a run.</summary>
    /// <param name="agentName">Agent name.</param>
    /// <param name="status">Final status.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="modelId">Model used.</param>
    /// <param name="duration">Run duration.</param>
    /// <param name="usage">Token usage.</param>
    /// <param name="agentVersion">
    /// The measured definition version. When <see langword="null"/>, no tag is
    /// added - either it is unknown, or the caller is already passing
    /// <see langword="null"/> because <see cref="AgentPrismObservabilityOptions.IncludeAgentVersionTag"/>
    /// is disabled.
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

        // The model tag is never left empty: in OpenTelemetry a missing tag
        // produces a separate time series and aggregations get silently split.
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

    /// <summary>Records a run's own cost (when a price is defined).</summary>
    /// <param name="agentName">Agent name.</param>
    /// <param name="modelId">Model used. When <see langword="null"/>, <c>"unknown"</c> is written.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="cost">The run's OWN cost (input + output). NOT the tree total.</param>
    /// <param name="currency">Currency.</param>
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

    /// <summary>Records a judge call's OWN cost.</summary>
    /// <param name="judgeName">The judge's name (<see cref="IRunJudge.Name"/>).</param>
    /// <param name="modelId">The judge's model. When <see langword="null"/>, <c>"unknown"</c> is written.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="cost">The judge's OWN cost (input + output).</param>
    /// <param name="currency">Currency.</param>
    public void RecordJudgeCost(string judgeName, string? modelId, string tenantId, decimal cost, string currency)
        => JudgeCost.Add(
            (double)cost,
            new TagList
            {
                { AgentPrismDiagnostics.Tags.JudgeName, judgeName },
                { AgentPrismDiagnostics.Tags.ModelId, modelId ?? "unknown" },
                { AgentPrismDiagnostics.Tags.TenantId, tenantId },
                { AgentPrismDiagnostics.Tags.Currency, currency },
            });

    /// <summary>Records the score given by a judge.</summary>
    /// <param name="judgeName">The judge's name (<see cref="IRunJudge.Name"/>).</param>
    /// <param name="agentName">Name of the scored agent.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="score">Score, 0-100.</param>
    public void RecordJudgeScore(string judgeName, string agentName, string tenantId, int score)
        => JudgeScore.Record(
            score,
            new TagList
            {
                { AgentPrismDiagnostics.Tags.JudgeName, judgeName },
                { AgentPrismDiagnostics.Tags.AgentName, agentName },
                { AgentPrismDiagnostics.Tags.TenantId, tenantId },
            });

    /// <summary>Records the result of a tool call.</summary>
    /// <param name="toolName">Tool name.</param>
    /// <param name="succeeded">Whether the call completed successfully.</param>
    /// <param name="duration">Call duration. <see langword="null"/> when unknown.</param>
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

    /// <summary>Records a response-cache lookup.</summary>
    /// <param name="provider">Model provider name.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="hit">Whether the lookup found a cached response.</param>
    public void RecordModelCacheLookup(string provider, string tenantId, bool hit)
        => ModelCacheLookups.Add(
            1,
            new TagList
            {
                { AgentPrismDiagnostics.Tags.Provider, provider },
                { AgentPrismDiagnostics.Tags.TenantId, tenantId },
                { AgentPrismDiagnostics.Tags.CacheResult, hit ? "hit" : "miss" },
            });

    /// <summary>Records an agent-source failure or contract violation.</summary>
    /// <param name="sourceName">The source name.</param>
    /// <param name="operation">The operation: list, resolve, or consistency.</param>
    public void RecordAgentSourceFailure(string sourceName, string operation)
        => AgentSourceFailures.Add(
            1,
            new TagList
            {
                { AgentPrismDiagnostics.Tags.AgentSourceName, sourceName },
                { AgentPrismDiagnostics.Tags.AgentSourceOperation, operation },
            });

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
