using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// Telemetry collection and sampling behavior.
/// </summary>
public sealed class ObservabilityTests
{
    [Fact]
    public void Metric_names_stay_stable()
    {
        // These names are written into consumers' dashboards and alert rules.
        // Changing them is a breaking change; this test makes that visible.
        AgentPrismDiagnostics.ActivitySourceName.ShouldBe("AgentPrism");
        AgentPrismDiagnostics.MeterName.ShouldBe("AgentPrism");
        AgentPrismDiagnostics.RunCounterName.ShouldBe("agentprism.runs");
        AgentPrismDiagnostics.RunDurationName.ShouldBe("agentprism.run.duration");
        AgentPrismDiagnostics.TokenCounterName.ShouldBe("agentprism.tokens");
        AgentPrismDiagnostics.ToolCounterName.ShouldBe("agentprism.tool.invocations");
        AgentPrismDiagnostics.ToolDurationName.ShouldBe("agentprism.tool.duration");
    }

    [Fact]
    public void Run_metrics_are_emitted()
    {
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        metrics.RecordRun(
            "support",
            RunStatus.Completed,
            "tenant-a",
            "gpt-5.4-mini",
            TimeSpan.FromSeconds(2),
            new RunUsage { InputTokens = 10, OutputTokens = 4, TotalTokens = 14 });

        collector.LongValues(AgentPrismDiagnostics.RunCounterName).ShouldBe([1]);

        // Durations are in SECONDS: the OpenTelemetry semantic convention requires
        // this, and writing milliseconds would break pre-built dashboards.
        collector.DoubleValues(AgentPrismDiagnostics.RunDurationName).ShouldBe([2.0]);

        collector.LongValues(AgentPrismDiagnostics.TokenCounterName).ShouldBe([10, 4]);
    }

    [Fact]
    public void Unknown_model_does_not_leave_tag_empty()
    {
        // A missing tag produces a SEPARATE time series in OpenTelemetry, and
        // aggregations silently split in two.
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        metrics.RecordRun(
            "support",
            RunStatus.Completed,
            "tenant-a",
            modelId: null,
            TimeSpan.FromSeconds(1),
            new RunUsage { InputTokens = 5 });

        var modelTags = collector.Tags(AgentPrismDiagnostics.TokenCounterName)
            .Select(static tags => Convert.ToString(
                tags.GetValueOrDefault(AgentPrismDiagnostics.Tags.ModelId),
                System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        modelTags.ShouldContain(value => string.Equals(value, "unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void Tool_metrics_do_not_write_histogram_without_duration()
    {
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        metrics.RecordToolInvocation("get_order_status", succeeded: true, duration: null);

        collector.LongValues(AgentPrismDiagnostics.ToolCounterName).ShouldBe([1]);
        collector.DoubleValues(AgentPrismDiagnostics.ToolDurationName).ShouldBeEmpty();
    }

    [Fact]
    public async Task No_listener_is_set_up_while_span_persistence_is_disabled()
    {
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, options => options.PersistSpans = false);

        collector.IsCollecting.ShouldBeFalse();
        collector.BeginRun("trace").ShouldBeFalse();

        (await collector.CompleteRunAsync("trace", AgentPrismId.NewId(), "default", RunStatus.Completed))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Failed_run_spans_are_written_outside_sampling()
    {
        var store = new InMemoryTraceStore();

        // Success ratio is zero: only failure protection can write a span.
        using var collector = CreateCollector(store, options =>
        {
            options.SuccessSampleRatio = 0;
            options.AlwaysPersistFailures = true;
        });

        var runId = AgentPrismId.NewId();
        var traceId = StartAndStopActivity();

        collector.BeginRun(traceId).ShouldBeTrue();

        // The activity's root span must have been buffered; the collector must write it.
        var activity = StartActivityInTrace(traceId);
        activity?.Stop();

        (await collector.CompleteRunAsync(traceId, runId, "default", RunStatus.Failed)).ShouldBeTrue();

        var trace = await store.GetTraceByRunAsync(runId);

        trace.ShouldNotBeNull();
        trace.Spans.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Successful_run_is_not_written_outside_sampling()
    {
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, options => options.SuccessSampleRatio = 0);

        var runId = AgentPrismId.NewId();
        var traceId = StartAndStopActivity();

        collector.BeginRun(traceId);

        var activity = StartActivityInTrace(traceId);
        activity?.Stop();

        (await collector.CompleteRunAsync(traceId, runId, "default", RunStatus.Completed))
            .ShouldBeFalse();

        (await store.GetTraceByRunAsync(runId)).ShouldBeNull();
    }

    [Fact]
    public async Task Untracked_trace_leaves_no_buffer()
    {
        // Leak protection: for a trace where BeginRun was never called, CompleteRunAsync
        // must silently return false.
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, static _ => { });

        (await collector.CompleteRunAsync("unknown", AgentPrismId.NewId(), "default", RunStatus.Completed))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Inner_spans_become_children_of_root_span()
    {
        // 🚨 Regression protection. Activity.Current is an AsyncLocal: if the root
        // span is opened in an async helper method, the assignment does NOT flow back
        // to the caller, and inner spans become SIBLINGS of the root span, not children.
        // This happened; the waterfall view rendered a flat list.
        var traceStore = new InMemoryTraceStore();
        var runStore = new InMemoryRunStore();

        using var collector = CreateCollector(traceStore, static options => options.SuccessSampleRatio = 1);

        // The wrapped agent opens its own span during the run. If the root span
        // was set up correctly, this span must be its child.
        var inner = new ActivityStartingAgent(new FakeChatClient());

        var agent = new RunRecordingAgent(
            inner,
            runStore,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            traceCollector: collector);

        await agent.RunAsync("hello");

        var run = (await runStore.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        var trace = await traceStore.GetTraceByRunAsync(run.Id);

        trace.ShouldNotBeNull();

        var root = trace.Spans
            .Single(span => string.Equals(span.Name, AgentPrismDiagnostics.RunActivityName, StringComparison.Ordinal));

        var child = trace.Spans
            .Single(span => string.Equals(span.Name, ActivityStartingAgent.SpanName, StringComparison.Ordinal));

        child.ParentId.ShouldBe(root.Id);
    }

    private static RunTraceCollector CreateCollector(
        ITraceStore store,
        Action<AgentPrismObservabilityOptions> configure)
    {
        var options = new AgentPrismOptions();

        configure(options.Observability);

        return new RunTraceCollector(
            store,
            Options.Create(options),
            NullLogger<RunTraceCollector>.Instance);
    }

    /// <summary>
    /// Opens and closes an activity from the source the collector listens to;
    /// returns the trace id.
    /// </summary>
    private static string StartAndStopActivity()
    {
        using var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);
        using var activity = source.StartActivity("test.root");

        // Activity is null if there is no listener; an id is generated so the
        // test remains meaningful and the scenario still runs.
        return activity?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
    }

    private static Activity? StartActivityInTrace(string traceId)
    {
        var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);

        return ActivityTraceId.CreateFromString(traceId.AsSpan()) is var parsed
            ? source.StartActivity(
                "test.child",
                ActivityKind.Internal,
                new ActivityContext(parsed, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded))
            : null;
    }

    private sealed class FixedTenantContext : ITenantContext
    {
        public string TenantId => "default";
    }

    /// <summary>
    /// Agent that opens its own span from the AgentPrism source during a run.
    /// </summary>
    /// <remarks>
    /// The smallest imitation of what Microsoft Agent Framework's <c>OpenTelemetryAgent</c>
    /// wrapper does; a real model call is not needed to test the hierarchy.
    /// </remarks>
    private sealed class ActivityStartingAgent : Microsoft.Agents.AI.DelegatingAIAgent
    {
        public const string SpanName = "test.inner";

        private static readonly ActivitySource Source = new(AgentPrismDiagnostics.ActivitySourceName);

        public ActivityStartingAgent(FakeChatClient client)
            : base(client.AsAIAgent())
        {
        }

        protected override async Task<Microsoft.Agents.AI.AgentResponse> RunCoreAsync(
            IEnumerable<ChatMessage> messages,
            Microsoft.Agents.AI.AgentSession? session = null,
            Microsoft.Agents.AI.AgentRunOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            // The span is opened BEFORE the inner call, in the same method body; the
            // task is awaited so it closes (MA0100).
            using var activity = Source.StartActivity(SpanName);

            return await base.RunCoreAsync(messages, session, options, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Produces a <see cref="Meter"/> with a unique identity, private to the test's
    /// own <see cref="AgentPrismMetrics"/> instance.
    /// </summary>
    /// <remarks>
    /// 🚨 <see cref="MeterListener"/> runs process-wide: filtering by name
    /// (<c>instrument.Meter.Name == "AgentPrism"</c>) also catches measurements from
    /// a DIFFERENT <see cref="Meter"/> instance with the SAME name in another test
    /// class running in parallel. Since tests in the same assembly run in parallel by
    /// default, this was a real cross-test leak, not a hypothetical one.
    /// </remarks>
    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Meter { get; } = new(AgentPrismDiagnostics.MeterName);

        public Meter Create(MeterOptions options) => Meter;

        public void Dispose() => Meter.Dispose();
    }

    /// <summary>
    /// Simple listener that collects measurements of a specific <c>Meter</c>
    /// <strong>instance</strong>. Filtering is by reference, not by name (see the
    /// <see cref="TestMeterFactory"/> note above).
    /// </summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> _longs = [];
        private readonly List<(string Name, double Value)> _doubles = [];
        private readonly Lock _gate = new();

        public MetricCollector(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            {
                lock (_gate)
                {
                    _longs.Add((instrument.Name, value, ToDictionary(tags)));
                }
            });

            _listener.SetMeasurementEventCallback<double>((instrument, value, _, _) =>
            {
                lock (_gate)
                {
                    _doubles.Add((instrument.Name, value));
                }
            });

            _listener.Start();
        }

        public List<long> LongValues(string name)
        {
            lock (_gate)
            {
                return [.. _longs.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Value)];
            }
        }

        public List<double> DoubleValues(string name)
        {
            lock (_gate)
            {
                return [.. _doubles.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Value)];
            }
        }

        public List<Dictionary<string, object?>> Tags(string name)
        {
            lock (_gate)
            {
                return [.. _longs.Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).Select(m => m.Tags)];
            }
        }

        public void Dispose() => _listener.Dispose();

        private static Dictionary<string, object?> ToDictionary(ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);

            foreach (var tag in tags)
            {
                result[tag.Key] = tag.Value;
            }

            return result;
        }
    }
}
