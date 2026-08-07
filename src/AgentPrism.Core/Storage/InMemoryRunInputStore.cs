using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// Calistirma girdilerini bellekte tutan varsayilan depo.
/// </summary>
/// <remarks>
/// <para>
/// Bellek ici uygulama <strong>birinci sinif</strong>tir (K-018): SQL saglayicisi
/// olmayan bir kurulumda da yeniden oynatma calisir. Kayitlar surec omrunce
/// yasar; <see cref="MaxRuns"/> asildiginda en eski kayit dusurulur.
/// </para>
/// <para>
/// Mesajlar burada <em>nesne olarak</em> tutulur; serilestirme yalnizca SQL
/// uygulamasinda devreye girer. Bu, polimorfik icerigin bellek ici yolda hicbir
/// zaman kaybolmamasini saglar.
/// </para>
/// </remarks>
public sealed class InMemoryRunInputStore : IRunInputStore
{
    private readonly ConcurrentDictionary<Guid, RunInputRecord> _inputs = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();

    /// <summary>
    /// Bellekte tutulacak ust girdi sayisi. Asilinca en eski girdi dusurulur.
    /// </summary>
    public int MaxRuns { get; init; } = 1_000;

    /// <inheritdoc />
    public ValueTask SaveAsync(RunInputRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Ikinci yazim yok sayilir: kuyruga alinan bir calistirma (Faz 46) ayni
        // kimlikle iki kez baslar ve girdi degismemelidir.
        if (_inputs.TryAdd(record.RunId, record))
        {
            _insertionOrder.Enqueue(record.RunId);
            TrimIfNeeded();
        }

        return default;
    }

    /// <inheritdoc />
    public ValueTask<RunInputRecord?> GetAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (!_inputs.TryGetValue(runId, out var record))
        {
            return new ValueTask<RunInputRecord?>((RunInputRecord?)null);
        }

        // Kiraci sinirini depo da zorlar: "yok" ile "baskasinin" cagiran icin
        // ayni sonuctur ve varlik sizdirmaz.
        return new ValueTask<RunInputRecord?>(
            string.Equals(record.TenantId, tenantId, StringComparison.Ordinal) ? record : null);
    }

    private void TrimIfNeeded()
    {
        while (_inputs.Count > MaxRuns && _insertionOrder.TryDequeue(out var oldest))
        {
            _inputs.TryRemove(oldest, out _);
        }
    }
}
