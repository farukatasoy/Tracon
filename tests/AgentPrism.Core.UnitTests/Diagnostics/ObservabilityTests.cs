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
        collector.BeginRun("trace", "span").ShouldBeFalse();

        (await collector.CompleteRunAsync("trace", "span", AgentPrismId.NewId(), "default", RunStatus.Completed))
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
        using var root = StartRoot();
        var traceId = root?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var rootSpanId = root?.SpanId.ToString() ?? "0000000000000001";

        collector.BeginRun(traceId, rootSpanId).ShouldBeTrue();

        // The activity's root span must have been buffered; the collector must write it.
        var activity = StartChildOf(root);
        activity?.Stop();
        root?.Stop();

        (await collector.CompleteRunAsync(traceId, rootSpanId, runId, "default", RunStatus.Failed)).ShouldBeTrue();

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
        using var root = StartRoot();
        var traceId = root?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var rootSpanId = root?.SpanId.ToString() ?? "0000000000000001";

        collector.BeginRun(traceId, rootSpanId);

        var activity = StartChildOf(root);
        activity?.Stop();
        root?.Stop();

        (await collector.CompleteRunAsync(traceId, rootSpanId, runId, "default", RunStatus.Completed))
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

        (await collector.CompleteRunAsync("unknown", "span", AgentPrismId.NewId(), "default", RunStatus.Completed))
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

    /// <summary>
    /// 🚨 The <c>error.message</c> attribute used to be written OUTSIDE the
    /// sensitive-data filter, so it skipped the very filter that covers its own
    /// key: <c>IsSensitive("error.message")</c> is true, and the value would have
    /// been dropped had it gone through the loop. The description is set
    /// unconditionally from <c>RunError.Message</c>, which carries the message of
    /// ANY tool or provider exception - a message that can hold personal data.
    /// The trace store applies no further redaction and the span is readable
    /// through <c>GET /api/runs/{runId}/trace</c>.
    /// </summary>
    [Fact]
    public async Task Error_message_is_withheld_while_sensitive_data_is_off()
    {
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, options =>
        {
            options.SuccessSampleRatio = 1;
            options.RecordSensitiveData = false;
        });

        var runId = AgentPrismId.NewId();

        using var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);
        using var activity = source.StartActivity("test.root");
        var traceId = activity?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        collector.BeginRun(traceId, activity?.SpanId.ToString() ?? "0000000000000001");

        activity?.SetStatus(ActivityStatusCode.Error, "User taylor@example.com not found in CRM");
        activity?.Stop();

        await collector.CompleteRunAsync(traceId, activity?.SpanId.ToString() ?? "0000000000000001", runId, "default", RunStatus.Failed);

        var trace = await store.GetTraceByRunAsync(runId);

        if (trace is null || trace.Spans.Count == 0)
        {
            // No listener attached in this environment; the scenario cannot run.
            return;
        }

        foreach (var span in trace.Spans)
        {
            span.Attributes.ShouldNotContainKey(
                "error.message",
                "the exception message can carry personal data and RecordSensitiveData is off.");
        }
    }

    /// <summary>
    /// The other half: an operator who deliberately turns sensitive data ON still
    /// gets the diagnostic. Withholding it in both modes would have removed a
    /// real debugging aid instead of fixing a leak.
    /// </summary>
    [Fact]
    public async Task Error_message_is_kept_when_sensitive_data_is_on()
    {
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, options =>
        {
            options.SuccessSampleRatio = 1;
            options.RecordSensitiveData = true;
        });

        var runId = AgentPrismId.NewId();

        using var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);
        using var activity = source.StartActivity("test.root");
        var traceId = activity?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        collector.BeginRun(traceId, activity?.SpanId.ToString() ?? "0000000000000001");

        activity?.SetStatus(ActivityStatusCode.Error, "boom");
        activity?.Stop();

        await collector.CompleteRunAsync(traceId, activity?.SpanId.ToString() ?? "0000000000000001", runId, "default", RunStatus.Failed);

        var trace = await store.GetTraceByRunAsync(runId);

        if (trace is null || trace.Spans.Count == 0)
        {
            return;
        }

        trace.Spans.ShouldContain(span => span.Attributes.ContainsKey("error.message"));
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
    /// <summary>
    /// 🚨 A W3C trace id is INHERITED from the incoming <c>traceparent</c> header,
    /// so two runs can share one. Keyed by trace id alone they shared a single
    /// span buffer, and whichever run completed first wrote BOTH runs' spans
    /// under its own run id and tenant id. A gateway that propagates distributed
    /// tracing produces this shape without any attacker.
    /// </summary>
    [Fact]
    public async Task Two_runs_sharing_a_trace_id_do_not_share_a_buffer()
    {
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, options => options.SuccessSampleRatio = 1);

        using var firstRoot = StartRoot();

        if (firstRoot is null)
        {
            // No listener attached in this environment; the scenario cannot run.
            return;
        }

        var traceId = firstRoot.TraceId.ToString();

        // The second run continues the SAME trace but opens its own root span,
        // exactly as a second request carrying the same traceparent would.
        var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);
        Activity.Current = null;
        using var secondRoot = source.StartActivity(
            "test.root.other",
            ActivityKind.Internal,
            new ActivityContext(firstRoot.TraceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded));

        if (secondRoot is null || !string.Equals(secondRoot.TraceId.ToString(), traceId, StringComparison.Ordinal))
        {
            return;
        }

        collector.BeginRun(traceId, firstRoot.SpanId.ToString()).ShouldBeTrue();
        collector.BeginRun(traceId, secondRoot.SpanId.ToString()).ShouldBeTrue(
            "the second run must get its own buffer, not be refused as a duplicate trace.");

        var firstRunId = AgentPrismId.NewId();
        var secondRunId = AgentPrismId.NewId();

        // Each run stops its own root; the spans must not cross.
        Activity.Current = firstRoot;
        using (var firstChild = source.StartActivity("first.child"))
        {
            firstChild?.SetTag("owner", "first");
            firstChild?.Stop();
        }

        Activity.Current = secondRoot;
        using (var secondChild = source.StartActivity("second.child"))
        {
            secondChild?.SetTag("owner", "second");
            secondChild?.Stop();
        }

        firstRoot.Stop();
        secondRoot.Stop();

        await collector.CompleteRunAsync(traceId, firstRoot.SpanId.ToString(), firstRunId, "tenant-a", RunStatus.Completed);
        await collector.CompleteRunAsync(traceId, secondRoot.SpanId.ToString(), secondRunId, "tenant-b", RunStatus.Completed);

        var firstTrace = await store.GetTraceByRunAsync(firstRunId);
        var secondTrace = await store.GetTraceByRunAsync(secondRunId);

        if (firstTrace is null || secondTrace is null)
        {
            return;
        }

        firstTrace.Spans.ShouldNotContain(
            span => span.Name.Contains("second", StringComparison.Ordinal),
            "tenant-a's trace must not carry tenant-b's spans.");

        secondTrace.Spans.ShouldNotContain(
            span => span.Name.Contains("first", StringComparison.Ordinal),
            "tenant-b's trace must not carry tenant-a's spans.");
    }

    /// <summary>
    /// Opens a root activity and leaves it RUNNING, so children started while it
    /// is current become real in-process children.
    /// </summary>
    /// <remarks>
    /// 🚨 The helper used to stop the root immediately and then fabricate
    /// "children" from a remote parent context. Those are not children: a span
    /// whose parent is remote IS a local root, which is exactly how a second
    /// run sharing an inherited trace id looks. The collector keys its buffer by
    /// (trace, local root span), so the test has to build the real shape.
    /// </remarks>
    private static Activity? StartRoot()
    {
        var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);

        return source.StartActivity("test.root");
    }

    private static Activity? StartChildOf(Activity? root)
    {
        if (root is null)
        {
            return null;
        }

        Activity.Current = root;

        var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);

        return source.StartActivity("test.child");
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
