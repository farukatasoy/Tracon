using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// AgentPrism span'lerini dinler, calistirma basina toplar ve calistirma
/// bittiginde ornekleme kararina gore <see cref="ITraceStore"/> icine yazar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Neden calistirma omruyle sinirli?</strong> Span'ler zaman icinde
/// dagilir ve bir trace'in bittigini anlamanin genel bir yolu yoktur; zaman
/// asimina dayali bir tampon hem sizinti hem de gec yazma uretir. Burada
/// tamponun sahibi <see cref="RunRecordingAgent"/>'tir: calistirmayi acar,
/// kapatir ve her iki durumda da (basari veya hata) tamponu bosaltir.
/// Sizinti bu yuzden yapisal olarak mumkun degildir.
/// </para>
/// <para>
/// <strong>Ornekleme neden sonda?</strong> Hatali calistirmalarin span'leri
/// hata ayiklamanin en degerli girdisidir, ancak bir calistirmanin hata verip
/// vermeyecegi baslangicta bilinmez. Karar sonda verilir; bunun bedeli
/// span'lerin o ana kadar bellekte tutulmasidir ve
/// <see cref="AgentPrismObservabilityOptions.MaxSpansPerRun"/> ile sinirlidir.
/// </para>
/// <para>
/// <strong>Akisi ele gecirmez.</strong> <see cref="ActivityListener"/> pasif bir
/// dinleyicidir; tuketicinin OpenTelemetry SDK'si ayni span'leri kendi
/// exporter'ina gondermeye devam eder.
/// </para>
/// </remarks>
public sealed class RunTraceCollector : IDisposable
{
    private readonly ITraceStore _store;
    private readonly IOptions<AgentPrismOptions> _options;
    private readonly ILogger<RunTraceCollector> _logger;
    private readonly ConcurrentDictionary<string, RunSpanBuffer> _buffers = new(StringComparer.Ordinal);
    private readonly ActivityListener? _listener;

    /// <summary>Yeni bir toplayici olusturur ve dinlemeye baslar.</summary>
    /// <param name="store">Span deposu.</param>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunTraceCollector(
        ITraceStore store,
        IOptions<AgentPrismOptions> options,
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
            // Dinleyici hic kurulmaz. Kurulmus bir dinleyici, tuketicinin
            // OpenTelemetry SDK'si olmasa bile her Activity'nin olusturulmasina
            // sebep olur; kapaliyken bu maliyeti odememeliyiz.
            return;
        }

        _listener = new ActivityListener
        {
            ShouldListenTo = static source =>
                string.Equals(source.Name, AgentPrismDiagnostics.ActivitySourceName, StringComparison.Ordinal),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnActivityStopped,
        };

        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Toplayici span kalicilastiriyor mu.</summary>
    public bool IsCollecting => _listener is not null;

    /// <summary>
    /// Bir calistirma icin span toplamayi baslatir.
    /// </summary>
    /// <param name="traceId">Calistirmanin kok span'inin W3C trace kimligi.</param>
    /// <returns>
    /// Toplama kapaliysa veya bu trace zaten izleniyorsa <see langword="false"/>.
    /// </returns>
    public bool BeginRun(string traceId)
    {
        if (_listener is null || string.IsNullOrEmpty(traceId))
        {
            return false;
        }

        return _buffers.TryAdd(traceId, new RunSpanBuffer());
    }

    /// <summary>
    /// Toplamayi bitirir ve ornekleme karari olumluysa span'leri yazar.
    /// Her durumda tampon serbest birakilir.
    /// </summary>
    /// <param name="traceId">Kok span'in W3C trace kimligi.</param>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="status">Calistirmanin son durumu.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Span'ler yazildiysa <see langword="true"/>.</returns>
    public async ValueTask<bool> CompleteRunAsync(
        string traceId,
        Guid runId,
        string tenantId,
        RunStatus status,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(traceId) || !_buffers.TryRemove(traceId, out var buffer))
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Gozlemlenebilirlik islevselligi bozmaz: calistirma zaten bitti,
            // span yazilamamasi kullaniciya yansimaz.
            _logger.LogWarning(
                ex,
                "AgentPrism span'leri yazilamadi. Calistirma {RunId} etkilenmedi.",
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

        if (!_buffers.TryGetValue(traceId, out var buffer))
        {
            // Bu trace izlenmiyor: ya calistirma disinda uretilmis bir span'dir
            // ya da calistirma zaten kapanmistir. Iki durumda da yok sayilir.
            return;
        }

        var limit = _options.Value.Observability.MaxSpansPerRun;

        if (!buffer.TryReserve(limit))
        {
            if (buffer.ReportOverflowOnce())
            {
                _logger.LogWarning(
                    "Bir calistirmanin span sayisi {Limit} sinirini asti; fazlasi atiliyor. " +
                    "Sinir AgentPrism:Observability:MaxSpansPerRun ile degistirilir.",
                    limit);
            }

            return;
        }

        buffer.Add(ToSpan(activity, traceId, _options.Value.Observability.RecordSensitiveData));
    }

    private static TraceSpan ToSpan(Activity activity, string traceId, bool includeSensitiveData)
    {
        var parentSpanId = activity.ParentSpanId.ToString();

        // Kok span'in ebeveyni tumu sifir olan span kimligidir.
        var hasParent = !string.IsNullOrEmpty(parentSpanId)
            && !parentSpanId.AsSpan().TrimStart('0').IsEmpty;

        return new TraceSpan
        {
            Id = TraceSpanIdentity.ForSpan(traceId, activity.SpanId.ToString()),
            ParentId = hasParent ? TraceSpanIdentity.ForSpan(traceId, parentSpanId) : null,
            SpanId = activity.SpanId.ToString(),
            Name = activity.DisplayName,
            Kind = ToKind(activity.Kind),
            // MA0132: DateTime -> DateTimeOffset ortuk cevrimi yasak; Activity
            // her zaman UTC tasir, ofset acikca sifir verilir.
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

            // Istem ve yanit icerikleri kisisel veri tasiyabilir. GenAI semantic
            // convention bunlari `gen_ai.*.message` altinda tasir; varsayilan
            // olarak span'e yazilmazlar.
            if (!includeSensitiveData && IsSensitive(tag.Key))
            {
                continue;
            }

            attributes[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        if (activity.StatusDescription is { Length: > 0 } description)
        {
            attributes["error.message"] = description;
        }

        return attributes;
    }

    private static bool IsSensitive(string key)
        => key.Contains("message", StringComparison.OrdinalIgnoreCase)
            || key.Contains("prompt", StringComparison.OrdinalIgnoreCase)
            || key.Contains("completion", StringComparison.OrdinalIgnoreCase);

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
    /// Tek bir calistirmanin span tamponu. Span'ler birden cok is parcacigindan
    /// gelebilir (akis, tool cagrilari), bu yuzden erisim kilitlidir.
    /// </summary>
    private sealed class RunSpanBuffer
    {
        // Kilit listenin kendisi uzerinde: net8.0 da hedefleniyor ve
        // System.Threading.Lock .NET 9 ile geldi. Ayri bir `object` alani
        // MA0158'i tetiklerdi.
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
