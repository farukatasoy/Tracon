using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Span'leri surec bellegi icinde tutan depo.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Sinirlari:</strong> surec omru, tek dugum ve sinirli kapasite.
/// <see cref="MaxTraces"/> asilinca en eski trace dusurulur.
/// Uretimde <c>AgentPrism.PostgreSql</c> kullanin.
/// </para>
/// <para>
/// 🚨 Okuma gecerli kiraciyla sinirlidir. Yazma kiraciyi partiden alir; okuma
/// <see cref="ITenantContext"/>'ten (Faz 41) -- SQL uygulamasiyla ayni kural.
/// </para>
/// </remarks>
public sealed class InMemoryTraceStore : ITraceStore
{
    private readonly ConcurrentDictionary<string, RunTrace> _byTraceId = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> _traceIdByRun = new();
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly ITenantContext _tenantContext;

    /// <summary>Yeni bir bellek ici span deposu olusturur.</summary>
    /// <param name="tenantContext">
    /// Gecerli kiracinin baglami. Verilmezse depo tek kiracili davranir.
    /// </param>
    public InMemoryTraceStore(ITenantContext? tenantContext = null)
        => _tenantContext = tenantContext ?? FixedTenantContext.Default;

    /// <summary>Bellekte tutulacak ust trace sayisi.</summary>
    public int MaxTraces { get; init; } = 500;

    /// <inheritdoc />
    public ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Spans.Count == 0)
        {
            return default;
        }

        _byTraceId.AddOrUpdate(
            batch.TraceId,
            static (traceId, state) => Create(traceId, state),
            static (traceId, existing, state) => Merge(existing, state),
            batch);

        if (batch.RunId is { } runId)
        {
            _traceIdByRun[runId] = batch.TraceId;
        }

        _insertionOrder.Enqueue(batch.TraceId);
        TrimIfNeeded();

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (_traceIdByRun.TryGetValue(runId, out var traceId)
            && _byTraceId.TryGetValue(traceId, out var trace)
            && string.Equals(trace.TenantId, _tenantContext.TenantId, StringComparison.Ordinal))
        {
            return new ValueTask<RunTrace?>(trace);
        }

        return new ValueTask<RunTrace?>((RunTrace?)null);
    }

    private static RunTrace Create(string traceId, TraceSpanBatch batch)
    {
        var spans = Sort(batch.Spans);

        return new RunTrace
        {
            Id = AgentPrismId.NewId(),
            TraceId = traceId,
            RunId = batch.RunId,
            TenantId = batch.TenantId,
            StartedAt = spans[0].StartedAt,
            EndedAt = LatestEnd(spans),
            Spans = spans,
        };
    }

    private static RunTrace Merge(RunTrace existing, TraceSpanBatch batch)
    {
        // Ayni span iki kez yazilabilir (kimlikler turetilmistir); kimlige gore
        // birlestirmek tekrari yok eder.
        var byId = existing.Spans.ToDictionary(static span => span.Id);

        foreach (var span in batch.Spans)
        {
            byId[span.Id] = span;
        }

        var spans = Sort([.. byId.Values]);

        return existing with
        {
            RunId = batch.RunId ?? existing.RunId,
            StartedAt = spans[0].StartedAt,
            EndedAt = LatestEnd(spans),
            Spans = spans,
        };
    }

    private static TraceSpan[] Sort(IReadOnlyList<TraceSpan> spans)
    {
        var sorted = spans.ToArray();
        Array.Sort(sorted, static (left, right) => left.StartedAt.CompareTo(right.StartedAt));
        return sorted;
    }

    private static DateTimeOffset? LatestEnd(IReadOnlyList<TraceSpan> spans)
    {
        DateTimeOffset? latest = null;

        foreach (var span in spans)
        {
            if (span.EndedAt is { } ended && (latest is null || ended > latest))
            {
                latest = ended;
            }
        }

        return latest;
    }

    private void TrimIfNeeded()
    {
        while (_byTraceId.Count > MaxTraces && _insertionOrder.TryDequeue(out var oldest))
        {
            if (!_byTraceId.TryRemove(oldest, out var removed))
            {
                continue;
            }

            if (removed.RunId is { } runId)
            {
                _traceIdByRun.TryRemove(runId, out _);
            }
        }
    }
}
