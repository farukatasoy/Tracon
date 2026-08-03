namespace AgentPrism;

/// <summary>Zamanlama tanimlarinin deposu.</summary>
/// <remarks>
/// <see cref="IJobStore"/>'dan ayridir: bu depo yalnizca <em>tanimlari</em>
/// (ne zaman, ne calisir) tutar; kuyruktaki fiili isler <see cref="IJobStore"/>
/// icindedir. Ayrim, <see cref="IWorkflowDefinitionStore"/> ile
/// <see cref="IWorkflowCheckpointStore"/> arasindakiyle aynidir.
/// </remarks>
public interface IJobScheduleStore
{
    /// <summary>Adi verilen zamanlamayi getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Zamanlama adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Zamanlama; yoksa <see langword="null"/>.</returns>
    ValueTask<JobSchedule?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Bir kiracinin tum zamanlamalarini ada gore listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Zamanlamalar.</returns>
    ValueTask<IReadOnlyList<JobSchedule>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Zamanlamayi olusturur veya gunceller (ad + kiraci benzersizdir).</summary>
    /// <param name="schedule">Kaydedilecek zamanlama.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaydedilmis zamanlama.</returns>
    ValueTask<JobSchedule> SaveAsync(JobSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>Zamanlamayi siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Zamanlama adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calisma zamani gelmis (<c>NextRunAt &lt;= asOfUtc</c>), etkin ve cron
    /// tasiyan tum kiracilarin zamanlamalarini listeler.
    /// </summary>
    /// <param name="asOfUtc">Karsilastirma ani (UTC).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Sirasi gelen zamanlamalar.</returns>
    ValueTask<IReadOnlyList<JobSchedule>> ListDueAsync(DateTimeOffset asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir zamanlamanin <see cref="JobSchedule.NextRunAt"/> alanini atomik
    /// olarak ilerletmeye calisir (compare-and-swap).
    /// </summary>
    /// <param name="scheduleId">Zamanlama kimligi.</param>
    /// <param name="expectedNextRunAt">
    /// Beklenen mevcut deger. Baska bir ornegin zaten ilerlettigi durumda
    /// deger uyusmaz ve islem basarisiz olur — cakisan iki cizelgeleme
    /// tetiklemesi boyle onlenir.
    /// </param>
    /// <param name="newNextRunAt">Yeni bir sonraki calisma zamani.</param>
    /// <param name="ranAt">Bu tetiklemenin gerceklestigi zaman (UTC).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Iddia basarili oldugunda <see langword="true"/>.</returns>
    ValueTask<bool> TryClaimNextRunAsync(
        Guid scheduleId,
        DateTimeOffset expectedNextRunAt,
        DateTimeOffset newNextRunAt,
        DateTimeOffset ranAt,
        CancellationToken cancellationToken = default);
}
