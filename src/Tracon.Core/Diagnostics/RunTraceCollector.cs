using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Listens to Tracon spans, buffers them per run, and writes them to
/// <see cref="ITraceStore"/> when the run completes, based on the sampling
/// decision.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why scoped to a run's lifetime?</strong> Spans are spread out over
/// time and there is no general way to know a trace has ended; a
/// timeout-based buffer produces both leaks and late writes. Here, the owner
/// of the buffer is <see cref="RunRecordingAgent"/>: it opens the run, closes
/// it, and drains the buffer in both cases (success or failure). A leak is
/// therefore structurally impossible.
/// </para>
/// <para>
/// <strong>Why sample at the end?</strong> The spans of failed runs are the
/// most valuable input for debugging, but whether a run will fail is not
/// known at the start. The decision is made at the end; the cost of this is
/// that spans are held in memory until then, bounded by
/// <see cref="TraconObservabilityOptions.MaxSpansPerRun"/>.
/// </para>
/// <para>
/// <strong>Does not take over the stream.</strong> <see cref="ActivityListener"/>
/// is a passive listener; the consumer's own OpenTelemetry SDK keeps sending
/// the same spans to its own exporter.
/// </para>
/// </remarks>
internal sealed class RunTraceCollector : IDisposable
{
    private readonly ITraceStore _store;
    private readonly IOptions<TraconOptions> _options;
    private readonly ILogger<RunTraceCollector> _logger;
    private readonly ConcurrentDictionary<TraceBufferKey, RunSpanBuffer> _buffers = new();
    private readonly ActivityListener? _listener;

    /// <summary>Creates a new collector and starts listening.</summary>
    /// <param name="store">Span store.</param>
    /// <param name="options">Tracon settings.</param>
    /// <param name="logger">Logger.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public RunTraceCollector(
        ITraceStore store,
        IOptions<TraconOptions> options,
        ILogger<RunTraceCollector> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _options = options;
        _logger = logger;

        var settings = options.Value.Observability;

        if (!settings.Enabled || !settings.PersistSpans)
        {
            // The listener is never set up. A set-up listener would cause every
            // Activity to be created even if the consumer has no OpenTelemetry
            // SDK; we must not pay that cost while disabled.
            return;
        }

        _listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, TraconDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Whether the collector is persisting spans.</summary>
    public bool IsCollecting => _listener is not null;

    /// <summary>
    /// Starts collecting spans for a run.
    /// </summary>
    /// <param name="traceId">The W3C trace id of the run's root span.</param>
    /// <param name="rootSpanId">
    /// The span id of the activity this run opened. A trace id is inherited from
    /// the incoming request, so it alone does not identify one run.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when collection is disabled or this run is
    /// already being tracked.
    /// </returns>
    public bool BeginRun(string traceId, string rootSpanId)
    {
        if (_listener is null || string.IsNullOrEmpty(traceId) || string.IsNullOrEmpty(rootSpanId))
        {
            return false;
        }

        return _buffers.TryAdd(new TraceBufferKey(traceId, rootSpanId), new RunSpanBuffer());
    }

    /// <summary>
    /// Ends collection and writes the spans if the sampling decision is
    /// positive. The buffer is released in every case.
    /// </summary>
    /// <param name="traceId">The W3C trace id of the root span.</param>
    /// <param name="rootSpanId">The span id of the activity this run opened.</param>
    /// <param name="runId">Run identifier.</param>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="status">The run's final status.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when spans were written.</returns>
    public async ValueTask<bool> CompleteRunAsync(
        string traceId,
        string rootSpanId,
        Guid runId,
        string tenantId,
        RunStatus status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(traceId)
            || string.IsNullOrEmpty(rootSpanId)
            || !_buffers.TryRemove(new TraceBufferKey(traceId, rootSpanId), out var buffer))
        {
            return false;
        }

        var spans = buffer.Drain();

        if (spans.Length == 0 || !ShouldPersist(status))
        {
            return false;
        }

        try
        {
            await _store.WriteSpansAsync(
                new TraceSpanBatch
                {
                    TraceId = traceId,
                    TenantId = tenantId,
                    RunId = runId,
                    Spans = spans,
                },
                cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            // Observability does not break functionality: the run has already
            // completed, a failure to write spans does not surface to the user.
            _logger.LogWarning(
                ex,
                "Could not write Tracon spans. Run {RunId} was not affected.",
                runId);

            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _listener?.Dispose();
        _buffers.Clear();
    }

    private bool ShouldPersist(RunStatus status)
    {
        var settings = _options.Value.Observability;

        if (status == RunStatus.Failed && settings.AlwaysPersistFailures)
        {
            return true;
        }

        if (settings.SuccessSampleRatio >= 1.0)
        {
            return true;
        }

        if (settings.SuccessSampleRatio <= 0.0)
        {
            return false;
        }

        return Random.Shared.NextDouble() < settings.SuccessSampleRatio;
    }

    private void OnActivityStopped(Activity activity)
    {
        var traceId = activity.TraceId.ToString();

        if (!TryFindBuffer(activity, traceId, out var buffer) || buffer is null)
        {
            // This trace is not being tracked: either it is a span produced
            // outside a run, or the run has already closed. Either way it is
            // ignored.
            return;
        }

        var limit = _options.Value.Observability.MaxSpansPerRun;

        if (!buffer.TryReserve(limit))
        {
            if (buffer.ReportOverflowOnce())
            {
                _logger.LogWarning(
                    "A run's span count exceeded the {Limit} limit; the excess is being dropped. " +
                    "The limit is changed via Tracon:Observability:MaxSpansPerRun.",
                    limit);
            }

            return;
        }

        buffer.Add(ToSpan(activity, traceId, _options.Value.Observability.RecordSensitiveData));
    }

    private static TraceSpan ToSpan(Activity activity, string traceId, bool includeSensitiveData)
    {
        var parentSpanId = activity.ParentSpanId.ToString();

        // The root span's parent is a span id whose bytes are all zero.
        var hasParent = !string.IsNullOrEmpty(parentSpanId)
            && !parentSpanId.AsSpan().TrimStart('0').IsEmpty;

        return new TraceSpan
        {
            Id = TraceSpanIdentity.ForSpan(traceId, activity.SpanId.ToString()),
            ParentId = hasParent ? TraceSpanIdentity.ForSpan(traceId, parentSpanId) : null,
            SpanId = activity.SpanId.ToString(),
            Name = activity.DisplayName,
            Kind = ToKind(activity.Kind),
            // MA0132: implicit DateTime -> DateTimeOffset conversion is
            // forbidden; Activity always carries UTC, the offset is given as
            // zero explicitly.
            StartedAt = new DateTimeOffset(activity.StartTimeUtc, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(activity.StartTimeUtc + activity.Duration, TimeSpan.Zero),
            Status = ToStatus(activity.Status),
            Attributes = ToAttributes(activity, includeSensitiveData),
        };
    }

    private static Dictionary<string, string> ToAttributes(Activity activity, bool includeSensitiveData)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tag in activity.TagObjects)
        {
            if (tag.Value is null)
            {
                continue;
            }

            // Prompt and response content may carry personal data. The GenAI
            // semantic convention carries these under `gen_ai.*.message`; they
            // are not written to the span by default.
            if (!includeSensitiveData && IsSensitive(tag.Key))
            {
                continue;
            }

            attributes[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // 🚨 This ran OUTSIDE the loop above, so it skipped the very filter that
        // covers its own key: IsSensitive("error.message") is true, so the value
        // would have been dropped had it gone through the loop. StatusDescription
        // is set unconditionally from RunError.Message, which carries the message
        // of ANY tool or provider exception - a message that can hold personal
        // data. The trace store applies no further redaction and the span is
        // readable through GET /api/runs/{runId}/trace, so RecordSensitiveData
        // =false did not protect this field at all.
        if (activity.StatusDescription is { Length: > 0 } description
            && (includeSensitiveData || !IsSensitive("error.message")))
        {
            attributes["error.message"] = description;
        }

        return attributes;
    }

    private static bool IsSensitive(string key)
        => key.Contains("message", StringComparison.OrdinalIgnoreCase)
            || key.Contains("prompt", StringComparison.OrdinalIgnoreCase)
            || key.Contains("completion", StringComparison.OrdinalIgnoreCase);

    // 🚨 The buffer is keyed by the trace AND the local root span, not by the
    // trace alone. A W3C trace id is INHERITED from the incoming traceparent
    // header, so two runs - possibly two different tenants' runs - can share one
    // trace id whenever a gateway propagates distributed tracing. Keyed by trace
    // id alone they shared one buffer, and whichever run completed first wrote
    // BOTH runs' spans under its own run id and tenant id.
    private readonly record struct TraceBufferKey(string TraceId, string RootSpanId);

    /// <summary>Finds the nearest tracked run that owns this span.</summary>
    /// <remarks>
    /// A run span can have an in-process HTTP server span above it. Therefore,
    /// walking to the topmost local parent does not identify the run: in a real
    /// ASP.NET host it identifies the request. The first ancestor whose
    /// trace/span pair is present in <see cref="_buffers"/> is the owning run.
    /// This also preserves nested-agent behavior: a child run has no buffer of
    /// its own, so the walk continues to the tracked root run.
    /// </remarks>
    private bool TryFindBuffer(Activity activity, string traceId, out RunSpanBuffer? buffer)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (_buffers.TryGetValue(new TraceBufferKey(traceId, current.SpanId.ToString()), out buffer))
            {
                return true;
            }
        }

        buffer = null;
        return false;
    }

    private static TraceSpanKind ToKind(ActivityKind kind) => kind switch
    {
        ActivityKind.Server => TraceSpanKind.Server,
        ActivityKind.Client => TraceSpanKind.Client,
        ActivityKind.Producer => TraceSpanKind.Producer,
        ActivityKind.Consumer => TraceSpanKind.Consumer,
        _ => TraceSpanKind.Internal,
    };

    private static TraceSpanStatus ToStatus(ActivityStatusCode status) => status switch
    {
        ActivityStatusCode.Ok => TraceSpanStatus.Ok,
        ActivityStatusCode.Error => TraceSpanStatus.Error,
        _ => TraceSpanStatus.Unset,
    };

    /// <summary>
    /// Span buffer for a single run. Spans can arrive from multiple threads
    /// (streaming, tool calls), so access is locked.
    /// </summary>
    private sealed class RunSpanBuffer
    {
        // Locking on the list itself: net8.0 is also targeted, and
        // System.Threading.Lock arrived with .NET 9. A separate `object` field
        // would trigger MA0158.
        private readonly List<TraceSpan> _spans = [];
        private int _reserved;
        private int _overflowReported;

        public bool TryReserve(int limit)
        {
            if (limit <= 0)
            {
                return false;
            }

            return Interlocked.Increment(ref _reserved) <= limit;
        }

        public bool ReportOverflowOnce()
            => Interlocked.CompareExchange(ref _overflowReported, 1, 0) == 0;

        public void Add(TraceSpan span)
        {
            lock (_spans)
            {
                _spans.Add(span);
            }
        }

        public TraceSpan[] Drain()
        {
            lock (_spans)
            {
                var result = _spans.ToArray();
                _spans.Clear();
                return result;
            }
        }
    }
}
