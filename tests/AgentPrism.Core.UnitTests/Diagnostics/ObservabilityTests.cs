using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Diagnostics;

/// <summary>
/// Telemetri toplama ve ornekleme davranisi.
/// </summary>
public sealed class ObservabilityTests
{
    [Fact]
    public void Metrik_adlari_kararli_kalir()
    {
        // Bu adlar tuketicinin gosterge panolarinda ve uyari kurallarinda yazilidir.
        // Degistirmek kirici bir degisikliktir; test bunu gorunur kilar.
        AgentPrismDiagnostics.ActivitySourceName.ShouldBe("AgentPrism");
        AgentPrismDiagnostics.MeterName.ShouldBe("AgentPrism");
        AgentPrismDiagnostics.RunCounterName.ShouldBe("agentprism.runs");
        AgentPrismDiagnostics.RunDurationName.ShouldBe("agentprism.run.duration");
        AgentPrismDiagnostics.TokenCounterName.ShouldBe("agentprism.tokens");
        AgentPrismDiagnostics.ToolCounterName.ShouldBe("agentprism.tool.invocations");
        AgentPrismDiagnostics.ToolDurationName.ShouldBe("agentprism.tool.duration");
    }

    [Fact]
    public void Calistirma_metrikleri_yayilir()
    {
        using var metrics = new AgentPrismMetrics();
        using var collector = new MetricCollector(AgentPrismDiagnostics.MeterName);

        metrics.RecordRun(
            "support",
            RunStatus.Completed,
            "kiraci-a",
            "gpt-5.4-mini",
            TimeSpan.FromSeconds(2),
            new RunUsage { InputTokens = 10, OutputTokens = 4, TotalTokens = 14 });

        collector.LongValues(AgentPrismDiagnostics.RunCounterName).ShouldBe([1]);

        // Sureler SANIYE cinsindendir: OpenTelemetry semantic convention'i bunu
        // zorunlu kilar ve milisaniye yazmak hazir panolari bozardi.
        collector.DoubleValues(AgentPrismDiagnostics.RunDurationName).ShouldBe([2.0]);

        collector.LongValues(AgentPrismDiagnostics.TokenCounterName).ShouldBe([10, 4]);
    }

    [Fact]
    public void Model_bilinmiyorsa_etiket_bos_gecilmez()
    {
        // Eksik etiket OpenTelemetry'de AYRI bir zaman serisi uretir ve
        // toplamalar sessizce ikiye bolunur.
        using var metrics = new AgentPrismMetrics();
        using var collector = new MetricCollector(AgentPrismDiagnostics.MeterName);

        metrics.RecordRun(
            "support",
            RunStatus.Completed,
            "kiraci-a",
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
    public void Tool_metrikleri_sure_yoksa_histograma_yazmaz()
    {
        using var metrics = new AgentPrismMetrics();
        using var collector = new MetricCollector(AgentPrismDiagnostics.MeterName);

        metrics.RecordToolInvocation("get_order_status", succeeded: true, duration: null);

        collector.LongValues(AgentPrismDiagnostics.ToolCounterName).ShouldBe([1]);
        collector.DoubleValues(AgentPrismDiagnostics.ToolDurationName).ShouldBeEmpty();
    }

    [Fact]
    public async Task Span_kalicilastirma_kapaliyken_dinleyici_kurulmaz()
    {
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, options => options.PersistSpans = false);

        collector.IsCollecting.ShouldBeFalse();
        collector.BeginRun("trace").ShouldBeFalse();

        (await collector.CompleteRunAsync("trace", AgentPrismId.NewId(), "default", RunStatus.Completed))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Hatali_calistirmanin_spanleri_ornekleme_disinda_yazilir()
    {
        var store = new InMemoryTraceStore();

        // Basari orani sifir: yalnizca hata korumasi span yazdirabilir.
        using var collector = CreateCollector(store, options =>
        {
            options.SuccessSampleRatio = 0;
            options.AlwaysPersistFailures = true;
        });

        var runId = AgentPrismId.NewId();
        var traceId = StartAndStopActivity();

        collector.BeginRun(traceId).ShouldBeTrue();

        // Etkinlik kok span'i tamponlanmis olmali; toplayici onu yazmalidir.
        var activity = StartActivityInTrace(traceId);
        activity?.Stop();

        (await collector.CompleteRunAsync(traceId, runId, "default", RunStatus.Failed)).ShouldBeTrue();

        var trace = await store.GetTraceByRunAsync(runId);

        trace.ShouldNotBeNull();
        trace.Spans.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Basarili_calistirma_ornekleme_disinda_yazilmaz()
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
    public async Task Izlenmeyen_trace_tampon_birakmaz()
    {
        // Sizinti korumasi: BeginRun cagrilmamis bir trace icin CompleteRunAsync
        // sessizce false donmelidir.
        var store = new InMemoryTraceStore();

        using var collector = CreateCollector(store, static _ => { });

        (await collector.CompleteRunAsync("bilinmeyen", AgentPrismId.NewId(), "default", RunStatus.Completed))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Ic_spanler_kok_spanin_cocugu_olur()
    {
        // 🚨 Regresyon korumasi. Activity.Current bir AsyncLocal'dir: kok span
        // bir async yardimci metotta acilirsa atama cagirana GERI AKMAZ ve ic
        // span'ler kok span'in cocugu degil KARDESI olur. Bu yasandi; waterfall
        // gorunumu duz bir liste cizdi.
        var traceStore = new InMemoryTraceStore();
        var runStore = new InMemoryRunStore();

        using var collector = CreateCollector(traceStore, static options => options.SuccessSampleRatio = 1);

        // Sarmalanan agent, calistirma sirasinda kendi span'ini acar. Kok span
        // dogru kurulmussa bu span onun cocugu olmalidir.
        var inner = new ActivityStartingAgent(new FakeChatClient());

        var agent = new RunRecordingAgent(
            inner,
            runStore,
            new FixedTenantContext(),
            new AgentPrismRunRecordingOptions(),
            NullLogger<RunRecordingAgent>.Instance,
            traceCollector: collector);

        await agent.RunAsync("merhaba");

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
    /// Toplayicinin dinledigi kaynaktan bir etkinlik acar ve kapatir; trace
    /// kimligini dondurur.
    /// </summary>
    private static string StartAndStopActivity()
    {
        using var source = new ActivitySource(AgentPrismDiagnostics.ActivitySourceName);
        using var activity = source.StartActivity("test.root");

        // Dinleyici yoksa Activity null olur; testin anlamli olmasi icin bir
        // kimlik uretilir ve senaryo yine de yurutulur.
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
    /// Calistirma sirasinda AgentPrism kaynagindan kendi span'ini acan agent.
    /// </summary>
    /// <remarks>
    /// Microsoft Agent Framework'un <c>OpenTelemetryAgent</c> sarmalayicisinin
    /// yaptigi seyin en kucuk taklidi; hiyerarsiyi test etmek icin gercek bir
    /// model cagrisina gerek yoktur.
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
            // Span, ic cagridan ONCE ve ayni metot govdesinde acilir; kapanmasi
            // icin gorev beklenir (MA0100).
            using var activity = Source.StartActivity(SpanName);

            return await base.RunCoreAsync(messages, session, options, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Belirli bir <c>Meter</c>'in olcumlerini toplayan basit dinleyici.
    /// </summary>
    private sealed class MetricCollector : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> _longs = [];
        private readonly List<(string Name, double Value)> _doubles = [];
        private readonly Lock _gate = new();

        public MetricCollector(string meterName)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, meterName, StringComparison.Ordinal))
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
