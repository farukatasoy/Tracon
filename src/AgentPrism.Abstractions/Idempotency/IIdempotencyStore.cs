namespace AgentPrism;

/// <summary>
/// Idempotency kayitlarinin deposu.
/// </summary>
/// <remarks>
/// <para>
/// Bir <c>Idempotency-Key</c> basligi tasiyan istek, aynen HTTP <c>Idempotency-Key</c>
/// standardinin (Stripe'in izledigi desen) ongordugu gibi <strong>en fazla bir kez</strong>
/// islenir: ilk istek calisirken ayni anahtarla gelen ikinci istek ya saklanan yaniti
/// alir ya da (hala isleniyorsa) <c>409</c> alir. Gerekce ve durum tablosu:
/// <c>docs/43-IDEMPOTENCY-KEY.md</c>, bolum 43.2.
/// </para>
/// <para>
/// Varsayilan (bellek ici) kurulumda birinci sinif <c>InMemoryIdempotencyStore</c>
/// kayitlidir (K-018); tek ornekli dagitimda yeterlidir. Cok ornekli bir dagitimda
/// bir SQL saglayicisi gerekir — aksi halde her ornek kendi anahtar kumesini tutar
/// ve tekillestirme ornekten ornege kaybolur.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
    /// <summary>
    /// Anahtari <c>Reserved</c> olarak ayirmayi dener. Anahtar zaten varsa
    /// mevcut kaydin durumu dondurulur ve yeni kayit ACILMAZ.
    /// </summary>
    /// <param name="request">Ayirma istegi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ayirma sonucu.</returns>
    /// <remarks>
    /// Ayirma <strong>atomik</strong> olmalidir: iki eszamanli istek ayni anahtarla
    /// gelirse yalnizca biri <see cref="IdempotencyState.Reserved"/> almalidir, digeri
    /// <see cref="IdempotencyState.InProgress"/>.
    /// </remarks>
    ValueTask<IdempotencyReservation> ReserveAsync(
        IdempotencyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Tamamlanan yaniti kaydeder ve anahtarin durumunu <c>Completed</c> yapar.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="key">Anahtar.</param>
    /// <param name="response">Saklanacak yanit.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask CompleteAsync(
        string tenantId,
        string key,
        IdempotencyResponse response,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Basarisiz istek sonrasi kaydi siler; ayni anahtarla yeniden deneme serbest kalir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="key">Anahtar.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <remarks>
    /// 🚨 Basarisiz bir calistirmanin kaydi SAKLANMAZ. Idempotency'nin amaci
    /// yeniden denemeyi guvenli kilmaktir; bir hatayi saklamak istemcinin gecici
    /// bir hatadan sonra hic yeniden deneyememesi demektir.
    /// </remarks>
    ValueTask ReleaseAsync(
        string tenantId,
        string key,
        CancellationToken cancellationToken = default);
}
