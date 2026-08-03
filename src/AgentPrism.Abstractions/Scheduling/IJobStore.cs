namespace AgentPrism;

/// <summary>
/// Kuyruktaki islerin ve ogelerinin deposu.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSql uygulamasi <c>FOR UPDATE SKIP LOCKED</c> ile kiralar: birden
/// fazla isci ayni veritabanina baglansa bile bir is yalnizca bir isci
/// tarafindan alinir. Bellek ici uygulama ayni sozlesmeyi bir kilit ve zaman
/// damgasiyla saglar.
/// </para>
/// <para>
/// <strong>Onemli:</strong> <see cref="ReportItemAsync"/> ve durum
/// gecisleri idempotent olmalidir — kira suresi dolup is yeniden
/// alindiginda ayni oge iki kez raporlanabilir.
/// </para>
/// </remarks>
public interface IJobStore
{
    /// <summary>
    /// Yeni bir is kuyruga ekler ve ogelerini olusturur.
    /// </summary>
    /// <param name="job">
    /// Is kaydi. <see cref="JobRecord.Status"/> yok sayilir; depo daima
    /// <see cref="JobStatus.Pending"/> ile baslatir.
    /// </param>
    /// <param name="items">Isin girdi listesi. Sira numaralari liste sirasina gore atanir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Olusturulan is kaydi.</returns>
    ValueTask<JobRecord> EnqueueAsync(
        JobRecord job,
        IReadOnlyList<string> items,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calismaya hazir en eski isi kiralar. Boyle bir is yoksa
    /// <see langword="null"/> doner.
    /// </summary>
    /// <param name="owner">Kiralayan iscinin kimligi.</param>
    /// <param name="leaseDuration">Kiranin gecerlilik suresi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kiralanan is; yoksa <see langword="null"/>.</returns>
    ValueTask<JobRecord?> LeaseAsync(
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>Devam eden bir isin kirasini uzatir. Isin durumunu degistirmez.</summary>
    /// <param name="jobId">Is kimligi.</param>
    /// <param name="owner">Kirayi tutan iscinin kimligi. Uyusmuyorsa islem yok sayilir.</param>
    /// <param name="leaseDuration">Yeni kira suresi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask RenewLeaseAsync(
        Guid jobId,
        string owner,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiralanmis (<see cref="JobStatus.Leased"/>) bir isi <see cref="JobStatus.Running"/>
    /// durumuna gecirir. Isci, kirayi aldiktan hemen sonra yururtmeye
    /// baslamadan once bunu cagirir.
    /// </summary>
    /// <param name="jobId">Is kimligi.</param>
    /// <param name="owner">Kirayi tutan iscinin kimligi. Uyusmuyorsa islem yok sayilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Gecis gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default);

    /// <summary>Bir isi sonlandirir.</summary>
    /// <param name="completion">Sonlandirma bilgileri.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir isi <see cref="JobStatus.Pending"/> durumuna geri dondurur; kirasini
    /// birakir. Yeniden deneme sayisi <see cref="JobRecord.Attempt"/> zaten
    /// kiralama anda arttigi icin burada degismez.
    /// </summary>
    /// <param name="jobId">Is kimligi.</param>
    /// <param name="errorMessage">Son deneme hatasi.</param>
    /// <param name="retryAfter">
    /// Bir sonraki denemeden once beklenecek sure. <see langword="null"/> veya
    /// sifir ise is hemen yeniden kiralanabilir (eski davranis).
    /// </param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// <para>
    /// <paramref name="retryAfter"/> <see cref="JobRecord.ScheduledFor"/> alanini
    /// ileri tasir; kiralama sorgusu zaten <c>scheduled_for &lt;= now</c> kosulunu
    /// uyguladigi icin geri adimli bekleme ek bir mekanizma gerektirmez.
    /// </para>
    /// <para>
    /// Webhook teslimi (Faz 21) bu parametreyle 1 dk / 5 dk / 30 dk / 2 sa / 6 sa
    /// merdivenini kurar. Ikinci bir kuyruk veya ikinci bir kiralama yazilmaz
    /// (K-160).
    /// </para>
    /// </remarks>
    ValueTask ReleaseForRetryAsync(
        Guid jobId,
        string errorMessage,
        TimeSpan? retryAfter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir isi iptal etmeye calisir. Yalnizca <see cref="JobStatus.Pending"/>,
    /// <see cref="JobStatus.Leased"/> veya <see cref="JobStatus.Running"/>
    /// durumundaki bir is iptal edilebilir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="jobId">Is kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Iptal gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Tek bir is kaydini getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="jobId">Is kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayit; yoksa veya baska bir kiraciya aitse <see langword="null"/>.</returns>
    ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>Isleri filtreleyerek listeler. En yeni kayit basta doner.</summary>
    /// <param name="query">Filtre.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default);

    /// <summary>Bir isin ogelerini sira numarasina gore listeler.</summary>
    /// <param name="jobId">Is kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Ogeler.</returns>
    ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir ogenin isleme sonucunu bildirir ve isin <see cref="JobRecord.DoneItems"/>/
    /// <see cref="JobRecord.FailedItems"/> sayaclarini gunceller.
    /// </summary>
    /// <param name="item">Oge sonucu.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default);
}
