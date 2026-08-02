namespace AgentPrism;

/// <summary>
/// Kalicilastirilmis span'lerin deposu.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Bu deponun hatalari calistirmayi kesmez.</strong> Gozlemlenebilirlik
/// islevselligi bozmamalidir; cagiran taraf (<c>RunTraceCollector</c>) hatalari
/// yakalar ve loglar.
/// </para>
/// <para>
/// Yazma yolu <em>orneklenir</em>. Her span'i yazmak yuksek hacimde veritabanini
/// darbogaza sokar; ornekleme karari depoya degil, cagirana aittir
/// (<c>AgentPrismObservabilityOptions</c>).
/// </para>
/// </remarks>
public interface ITraceStore
{
    /// <summary>
    /// Bir trace'in span'lerini yazar. Ayni trace icin birden cok kez
    /// cagrilabilir; span'ler kimliklerine gore birlestirilir.
    /// </summary>
    /// <param name="batch">Yazilacak span kumesi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask WriteSpansAsync(TraceSpanBatch batch, CancellationToken cancellationToken = default);

    /// <summary>Bir calistirmanin span agacini getirir.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Trace; kayit yoksa <see langword="null"/>.</returns>
    ValueTask<RunTrace?> GetTraceByRunAsync(Guid runId, CancellationToken cancellationToken = default);
}

/// <summary>Tek bir trace'e ait span kumesi.</summary>
public sealed record TraceSpanBatch
{
    /// <summary>W3C trace kimligi.</summary>
    public required string TraceId { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Iliskili calistirma.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Yazilacak span'ler.</summary>
    public required IReadOnlyList<TraceSpan> Spans { get; init; }
}
