using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Metrics emitted by Tracon. Consumers collect them with
/// <c>AddMeter(TraconDiagnostics.MeterName)</c>.
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
public sealed class TraconMetrics : IDisposable
{
    /// <summary>The lane tag written once the cardinality guard has tripped.</summary>
    private const string OtherLane = "other";

    private readonly Meter _meter;
    private readonly bool _ownsMeter;
    private readonly IOptionsMonitor<TraconOptions>? _options;

    // The lane names this process has already given a series of their own.
    // It only ever GROWS: a lane that once earned its name keeps it for the
    // life of the process. A shrinking set would write the same lane under its
    // own name one day and under "other" the next, producing a series nobody
    // can read on a dashboard.
    private readonly ConcurrentDictionary<string, byte> _knownLanes = new(StringComparer.Ordinal);
    private int _knownLaneCount;

    /// <summary>Creates a new metric set.</summary>
    /// <param name="meterFactory">
    /// Meter factory. When <see langword="null"/>, a new <see cref="Meter"/>
    /// instance is created and owned by this object.
    /// </param>
    /// <param name="options">
    /// The root options type that <see cref="TraconOptions.Observability"/>
    /// — which carries <see cref="TraconObservabilityOptions.MaxJobLaneCardinality"/>
    /// — hangs off. When <see langword="null"/>, that setting's own default is used.
    /// The nested type is never injected standalone: it is not registered with
    /// <c>services.Configure&lt;TraconObservabilityOptions&gt;</c> anywhere.
    /// </param>
    public TraconMetrics(IMeterFactory? meterFactory = null, IOptionsMonitor<TraconOptions>? options = null)
    {
        _options = options;

        if (meterFactory is null)
        {
            _meter = new Meter(TraconDiagnostics.MeterName);
            _ownsMeter = true;
        }
        else
        {
            _meter = meterFactory.Create(TraconDiagnostics.MeterName);
        }

        Runs = _meter.CreateCounter<long>(
            TraconDiagnostics.RunCounterName,
            unit: "{run}",
            description: "Number of completed runs.");

        RunDuration = _meter.CreateHistogram<double>(
            TraconDiagnostics.RunDurationName,
            unit: "s",
            description: "Run duration.");

        Tokens = _meter.CreateCounter<long>(
            TraconDiagnostics.TokenCounterName,
            unit: "{token}",
            description: "Token count reported by the provider.");

        ToolInvocations = _meter.CreateCounter<long>(
            TraconDiagnostics.ToolCounterName,
            unit: "{call}",
            description: "Number of completed tool calls.");

        ToolDuration = _meter.CreateHistogram<double>(
            TraconDiagnostics.ToolDurationName,
            unit: "s",
            description: "Tool call duration.");

        RunCost = _meter.CreateCounter<double>(
            TraconDiagnostics.RunCostCounterName,
            unit: "{cost}",
            description: "Cost per run, in currency units. Does not include the tree total (K-151).");

        JudgeCost = _meter.CreateCounter<double>(
            TraconDiagnostics.JudgeCostCounterName,
            unit: "{cost}",
            description: "The OWN cost of a single IRunJudge call (Phase 49). Not included in the scored agent's cost.");

        JudgeScore = _meter.CreateHistogram<double>(
            TraconDiagnostics.JudgeScoreHistogramName,
            unit: "{score}",
            description: "Score given by an IRunJudge, 0-100 (Phase 49).");

        ModelCacheLookups = _meter.CreateCounter<long>(
            TraconDiagnostics.ModelCacheLookupCounterName,
            unit: "{lookup}",
            description: "Response-cache lookups, tagged hit or miss.");

        AgentSourceFailures = _meter.CreateCounter<long>(
            TraconDiagnostics.AgentSourceFailureCounterName,
            unit: "{failure}",
            description: "Agent-source failures, tagged by source and operation.");

        JobExecutions = _meter.CreateCounter<long>(
            TraconDiagnostics.JobCounterName,
            unit: "{job}",
            description: "Number of background jobs that reached a terminal status.");

        JobDuration = _meter.CreateHistogram<double>(
            TraconDiagnostics.JobDurationName,
            unit: "s",
            description: "Duration of a single background-job ATTEMPT, not of the job across all of its attempts.");

        AuditWriteFailures = _meter.CreateCounter<long>(
            TraconDiagnostics.AuditWriteFailureCounterName,
            unit: "{failure}",
            description: "Audit entries that could not be written, tagged by tenant, action and outcome.");
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

    /// <summary>Audit write-failure counter. Tags: tenant, action, outcome (swallowed/refused).</summary>
    public Counter<long> AuditWriteFailures { get; }

    /// <summary>Finished-job counter. Tags: lane, kind, status, tenant.</summary>
    /// <remarks>
    /// Only a TERMINAL status is counted. A lease renewal or a release for
    /// retry is not an outcome, and counting it would turn this instrument's
    /// meaning from "how many jobs finished" into "how many things happened".
    /// </remarks>
    public Counter<long> JobExecutions { get; }

    /// <summary>Job-attempt duration. Tags: lane, kind, status.</summary>
    /// <remarks>
    /// One measurement covers ONE attempt. A job that fails twice and then
    /// succeeds records a single measurement, for its last attempt — the
    /// earlier attempts ended in a retry, which is not a terminal status.
    /// </remarks>
    public Histogram<double> JobDuration { get; }

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
    /// <see langword="null"/> because <see cref="TraconObservabilityOptions.IncludeAgentVersionTag"/>
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
            { TraconDiagnostics.Tags.AgentName, agentName },
            { TraconDiagnostics.Tags.Status, statusTag },
            { TraconDiagnostics.Tags.TenantId, tenantId },
        };

        var durationTags = new TagList
        {
            { TraconDiagnostics.Tags.AgentName, agentName },
            { TraconDiagnostics.Tags.Status, statusTag },
        };

        if (agentVersion is { } version)
        {
            runTags.Add(TraconDiagnostics.Tags.AgentVersion, version);
            durationTags.Add(TraconDiagnostics.Tags.AgentVersion, version);
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
                { TraconDiagnostics.Tags.AgentName, agentName },
                { TraconDiagnostics.Tags.ModelId, modelId ?? "unknown" },
                { TraconDiagnostics.Tags.TenantId, tenantId },
                { TraconDiagnostics.Tags.Currency, currency },
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
                { TraconDiagnostics.Tags.JudgeName, judgeName },
                { TraconDiagnostics.Tags.ModelId, modelId ?? "unknown" },
                { TraconDiagnostics.Tags.TenantId, tenantId },
                { TraconDiagnostics.Tags.Currency, currency },
            });

    /// <summary>Records the score given by a judge.</summary>
    /// <param name="judgeName">The judge's name (<see cref="IRunJudge.Name"/>).</param>
    /// <param name="agentName">Name of the scored agent.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="score">Score, 0-100.</param>
    public void RecordJudgeScore(string judgeName, string agentName, string tenantId, double score)
        => JudgeScore.Record(
            score,
            new TagList
            {
                { TraconDiagnostics.Tags.JudgeName, judgeName },
                { TraconDiagnostics.Tags.AgentName, agentName },
                { TraconDiagnostics.Tags.TenantId, tenantId },
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
            new KeyValuePair<string, object?>(TraconDiagnostics.Tags.ToolName, toolName),
            new KeyValuePair<string, object?>(TraconDiagnostics.Tags.Status, statusTag));

        if (duration is { } elapsed)
        {
            ToolDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>(TraconDiagnostics.Tags.ToolName, toolName));
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
                { TraconDiagnostics.Tags.Provider, provider },
                { TraconDiagnostics.Tags.TenantId, tenantId },
                { TraconDiagnostics.Tags.CacheResult, hit ? "hit" : "miss" },
            });

    /// <summary>Records an agent-source failure or contract violation.</summary>
    /// <param name="sourceName">The source name.</param>
    /// <param name="operation">The operation: list, resolve, or consistency.</param>
    public void RecordAgentSourceFailure(string sourceName, string operation)
        => AgentSourceFailures.Add(
            1,
            new TagList
            {
                { TraconDiagnostics.Tags.AgentSourceName, sourceName },
                { TraconDiagnostics.Tags.AgentSourceOperation, operation },
            });

    /// <summary>Records an audit entry that could not be written.</summary>
    /// <param name="tenantId">The tenant the entry belonged to.</param>
    /// <param name="action">The action name, such as <c>approval.decision</c>.</param>
    /// <param name="outcome">
    /// <c>swallowed</c> when the operation continued anyway, <c>refused</c> when the failed
    /// write stopped it.
    /// </param>
    public void RecordAuditWriteFailure(string tenantId, string action, string outcome)
        => AuditWriteFailures.Add(
            1,
            new TagList
            {
                { TraconDiagnostics.Tags.TenantId, tenantId },
                { TraconDiagnostics.Tags.AuditAction, action },
                { TraconDiagnostics.Tags.AuditOutcome, outcome },
            });

    /// <summary>Records a background job that reached a terminal status.</summary>
    /// <param name="lane">
    /// The job's lane (<see cref="JobRecord.Lane"/>). Guarded against runaway
    /// cardinality: see <see cref="TraconObservabilityOptions.MaxJobLaneCardinality"/>.
    /// </param>
    /// <param name="handlerKey">The job's handler key (<see cref="JobRecord.HandlerKey"/>).</param>
    /// <param name="status">
    /// The terminal status: <see cref="JobStatus.Completed"/>,
    /// <see cref="JobStatus.Failed"/>, or <see cref="JobStatus.Cancelled"/>.
    /// </param>
    /// <param name="tenantId">The tenant the job belongs to. <c>"unknown"</c> is written when null.</param>
    /// <param name="duration">
    /// How long THIS attempt took, measured on a monotonic clock. It is not the
    /// job's total time across every attempt.
    /// </param>
    public void RecordJob(string lane, string handlerKey, JobStatus status, string? tenantId, TimeSpan duration)
    {
        var laneTag = ResolveLaneTag(lane);

        var statusTag = status.ToString();

        JobExecutions.Add(
            1,
            new TagList
            {
                { TraconDiagnostics.Tags.Lane, laneTag },
                { TraconDiagnostics.Tags.JobHandlerKey, handlerKey },
                { TraconDiagnostics.Tags.JobStatus, statusTag },
                { TraconDiagnostics.Tags.TenantId, tenantId ?? "unknown" },
            });

        JobDuration.Record(
            duration.TotalSeconds,
            new TagList
            {
                { TraconDiagnostics.Tags.Lane, laneTag },
                { TraconDiagnostics.Tags.JobHandlerKey, handlerKey },
                { TraconDiagnostics.Tags.JobStatus, statusTag },
            });
    }

    /// <summary>
    /// Maps a lane name onto the tag value to publish, keeping the number of
    /// distinct lane series bounded.
    /// </summary>
    /// <param name="lane">The job's lane name.</param>
    /// <returns>
    /// <paramref name="lane"/> itself while the process is still below
    /// <see cref="TraconObservabilityOptions.MaxJobLaneCardinality"/> distinct
    /// lanes; <c>"other"</c> afterwards.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A lane name is chosen by the consumer and Tracon does not bound how
    /// many exist — a consumer that derives one lane per user would otherwise
    /// flood the metric backend. The slot is reserved BEFORE the name is
    /// recorded, so concurrent completions cannot push the set past the limit.
    /// A consumer whose own lane is literally named <c>other</c> shares the
    /// overflow series; the name is reserved for this purpose.
    /// </para>
    /// <para>
    /// <c>internal</c> rather than private because the queue-depth gauge tags the
    /// same lane values and has to share THIS object's set. Two independent sets
    /// would let the counter publish a lane under its own name while the gauge
    /// called it <c>other</c>, which is the unreadable series the guard exists
    /// to prevent.
    /// </para>
    /// </remarks>
    internal string ResolveLaneTag(string lane)
    {
        if (_knownLanes.ContainsKey(lane))
        {
            return lane;
        }

        var max = Math.Max(1, _options?.CurrentValue.Observability.MaxJobLaneCardinality ?? 64);

        while (true)
        {
            var current = Volatile.Read(ref _knownLaneCount);

            if (current >= max)
            {
                return OtherLane;
            }

            if (Interlocked.CompareExchange(ref _knownLaneCount, current + 1, current) != current)
            {
                continue;
            }

            if (!_knownLanes.TryAdd(lane, 0))
            {
                // Another thread registered the same lane while this one held a
                // reserved slot; hand the slot back so the budget is not spent twice.
                Interlocked.Decrement(ref _knownLaneCount);
            }

            return lane;
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
            new KeyValuePair<string, object?>(TraconDiagnostics.Tags.AgentName, agentName),
            new KeyValuePair<string, object?>(TraconDiagnostics.Tags.ModelId, modelId),
            new KeyValuePair<string, object?>(TraconDiagnostics.Tags.Direction, direction));
}
