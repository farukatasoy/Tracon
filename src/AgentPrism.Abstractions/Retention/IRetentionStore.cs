namespace AgentPrism;

/// <summary>
/// Fiili saklama verisi uzerinde sayma, parti parti silme ve arsiv icin okuma
/// yapan veri duzlemi.
/// </summary>
/// <remarks>
/// <para>
/// Hedef adi parametresi her zaman <see cref="RetentionTargets"/> beyaz
/// listesinden gelir; uygulamalar bilinmeyen bir hedef icin
/// <see cref="ArgumentException"/> firlatmalidir.
/// </para>
/// <para>
/// Hangi sutunun "yas" sayilacagi ve hangi ek kosulun (ornegin yalniz
/// tamamlanmis isler) uygulanacagi HER hedef icin sabittir ve uygulamanin
/// icinde gomulu kalir; cagiran taraf yalniz hedef adini ve kesim tarihini bilir.
/// </para>
/// <para>
/// Varsayilan (bellek ici) kurulumda <c>NullRetentionStore</c> kayitlidir ve
/// her zaman bos/sifir doner: saklama, yalniz bir SQL saglayicisi acikken
/// anlamlidir.
/// </para>
/// </remarks>
public interface IRetentionStore
{
    /// <summary>Kesim tarihinden eski, hedefte su an eslesen satir sayisini dondurur.</summary>
    /// <param name="target">Hedef adi.</param>
    /// <param name="cutoff">Kesim tarihi (UTC). Bu tarihten eski satirlar eslesir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Eslesen satir sayisi.</returns>
    ValueTask<long> CountOlderThanAsync(
        string target,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);

    /// <summary>Kesim tarihinden eski satirlardan bir parti okur (silmez).</summary>
    /// <param name="target">Hedef adi.</param>
    /// <param name="cutoff">Kesim tarihi (UTC).</param>
    /// <param name="batchSize">En fazla kac satir okunacagi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Okunan satirlar; eslesen kalmadiysa bos liste.</returns>
    ValueTask<IReadOnlyList<ArchiveRow>> ReadForArchiveAsync(
        string target,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kesim tarihinden eski satirlardan bir partiyi siler. Toplu tek bir
    /// <c>DELETE</c> DEGILDIR; saglayiciya gore parti bazli calisir.
    /// </summary>
    /// <param name="target">Hedef adi.</param>
    /// <param name="cutoff">Kesim tarihi (UTC).</param>
    /// <param name="batchSize">En fazla kac satirin silinecegi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silinen satir sayisi. Sifir donerse eslesen satir kalmamis demektir.</returns>
    ValueTask<int> DeleteBatchAsync(
        string target,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Silinmeden once satirlarin yazildigi soguk depolama genisleme noktasi.
/// </summary>
/// <remarks>
/// <para>
/// Varsayilan uygulama YOKTUR (karar K-007 — bulut SDK bagimliligi alinmaz).
/// Tuketici kendi sink'ini <c>samples/</c> altindaki dosya sistemi ornegini
/// temel alarak yazar ve <c>IArchiveSink</c> olarak kaydeder.
/// </para>
/// <para>
/// 🚨 Kayitli degilse <c>archive = true</c> olan bir politika hicbir satir
/// SILMEZ — arsivlenemeyen veri dusurulmez. Bu, sessiz veri kaybini engeller.
/// </para>
/// </remarks>
public interface IArchiveSink
{
    /// <summary>Bir parti satiri arsive yazar.</summary>
    /// <param name="target">Hedef adi.</param>
    /// <param name="partitionDate">
    /// Partinin ait oldugu tarih (UTC, gun hassasiyetinde). Sink bunu dosya/nesne
    /// yolunu bolumlemek icin kullanabilir (ornegin <c>run_events/2026-08-05.jsonl.gz</c>).
    /// </param>
    /// <param name="rows">Yazilacak satirlar.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask WriteAsync(
        string target,
        DateTimeOffset partitionDate,
        IReadOnlyList<ArchiveRow> rows,
        CancellationToken cancellationToken = default);
}
