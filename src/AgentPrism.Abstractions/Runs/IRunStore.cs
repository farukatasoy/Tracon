namespace AgentPrism;

/// <summary>
/// Calistirma kayitlarinin ve olay akisinin deposu.
/// </summary>
/// <remarks>
/// <para>
/// Olaylar <em>append-only</em>'dir. Uygulamalar bir olayi hicbir zaman
/// guncellememeli veya silmemelidir; yalnizca <see cref="AppendEventAsync"/>
/// ile eklemelidir.
/// </para>
/// <para>
/// <strong>Onemli:</strong> Bu deponun hatalari calistirmayi kesmez.
/// Gozlemlenebilirlik, islevselligi bozmamalidir. Cagiran taraf
/// (<c>RunEventWriter</c>) hatalari yakalar ve loglar.
/// </para>
/// </remarks>
public interface IRunStore
{
    /// <summary>Yeni bir calistirma kaydi acar.</summary>
    /// <param name="info">Baslangic bilgileri.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Olusturulan kayit.</returns>
    ValueTask<RunRecord> StartRunAsync(RunStartInfo info, CancellationToken cancellationToken = default);

    /// <summary>Calistirma akisina bir olay ekler.</summary>
    /// <param name="runEvent">Eklenecek olay. Sira numarasi cagiran tarafindan atanir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask AppendEventAsync(RunEvent runEvent, CancellationToken cancellationToken = default);

    /// <summary>Calistirmayi sonlandirir ve ozetini gunceller.</summary>
    /// <param name="completion">Sonlandirma bilgileri.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask CompleteRunAsync(RunCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>Bir calistirma kaydini getirir.</summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa <see langword="null"/>.</returns>
    ValueTask<RunRecord?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Calistirmalari filtreleyerek listeler. En yeni kayit basta doner.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    ValueTask<IReadOnlyList<RunRecord>> QueryRunsAsync(RunQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir calistirmanin olaylarini sira numarasina gore okur.
    /// Canli akis ve gecmise donuk yeniden oynatma ayni yoldan gecer.
    /// </summary>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="fromSequence">Bu sira numarasindan itibaren okunur (dahil).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sirali olay akisi.</returns>
    IAsyncEnumerable<RunEvent> ReadEventsAsync(
        Guid runId,
        long fromSequence = 0,
        CancellationToken cancellationToken = default);
}
