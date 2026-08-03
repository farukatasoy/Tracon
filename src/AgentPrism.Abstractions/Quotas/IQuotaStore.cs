namespace AgentPrism;

/// <summary>Kota kurallarinin ve tuketim sayaclarinin deposu.</summary>
/// <remarks>
/// <para>
/// Iki farkli veri yasar: <em>kurallar</em> (yonetici tanimlar, seyrek degisir)
/// ve <em>sayaclar</em> (her calistirma sonunda artar). Ikisi ayni sozlesmede
/// durur cunku denetim her zaman ikisini birlikte okur.
/// </para>
/// <para>
/// <strong>Onemli:</strong> <see cref="AddUsageAsync"/> atomik olmalidir —
/// PostgreSQL uygulamasi <c>INSERT ... ON CONFLICT DO UPDATE</c> kullanir.
/// Eszamanli calistirmalar ayni satiri artirir ve hicbir artis kaybolmaz.
/// </para>
/// </remarks>
public interface IQuotaStore
{
    /// <summary>Bir kiracinin kota kurallarini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kurallar.</returns>
    ValueTask<IReadOnlyList<QuotaDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir kural getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="id">Kural kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kural; yoksa veya baska bir kiraciya aitse <see langword="null"/>.</returns>
    ValueTask<QuotaDefinition?> GetAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir kurali kaydeder. Ayni kapsam (kiraci + agent + donem) icin kural
    /// zaten varsa uzerine yazar.
    /// </summary>
    /// <param name="definition">Kural.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaydedilen kural.</returns>
    /// <remarks>
    /// Kapsam benzersizligi <c>COALESCE(agent_name, '')</c> uzerinden kurulur;
    /// duz bir <c>UNIQUE</c> kisiti NULL'lari birbirinden ayirt eder ve ayni
    /// kuralin sinirsiz kez eklenmesine izin verirdi.
    /// </remarks>
    ValueTask<QuotaDefinition> SaveAsync(
        QuotaDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kurali siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="id">Kural kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>Gecerli donemin sayaclarini getirir.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sayaclar. Hic tuketim yoksa bos liste.</returns>
    ValueTask<IReadOnlyList<QuotaUsageRecord>> GetUsageAsync(
        QuotaUsageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir tuketimi hem agent sayacina hem kiraci geneli sayacina ekler.
    /// </summary>
    /// <param name="consumption">Tuketim.</param>
    /// <param name="periodStarts">
    /// Her donem icin, tuketimin dustugu donemin ilk gunu. Cagiran bunu
    /// yapilandirilmis saat dilimine gore hesaplar; depo saat dilimi bilmez.
    /// </param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// Depo saat dilimi <strong>tasimaz</strong>: donem sinirini cagiran
    /// hesaplar ve hazir olarak verir. Boylece ayni depo farkli saat
    /// dilimlerinde calisan tuketicilerde dogru davranir.
    /// </remarks>
    ValueTask AddUsageAsync(
        QuotaConsumption consumption,
        IReadOnlyDictionary<QuotaPeriod, DateOnly> periodStarts,
        CancellationToken cancellationToken = default);
}
