namespace AgentPrism;

/// <summary>Bekleyen onay isteklerinin deposu.</summary>
/// <remarks>
/// <see cref="ListPendingAsync"/>, <see cref="GetAsync"/> ve <see cref="DecideAsync"/>
/// cagiranin kiracisiyla (<c>ITenantContext</c>) sinirlidir: bir kiracinin
/// operatoru baska kiracinin onayini goremez ve veremez. <see cref="ExpireAsync"/>
/// bir bakim islemidir ve butun kiracilari tarar — <c>IRunStore.ClaimOrphanedRunsAsync</c>
/// ile ayni gerekce.
/// </remarks>
public interface IPendingApprovalStore
{
    /// <summary>Yeni bir bekleyen onay istegi olusturur.</summary>
    /// <param name="approval">Kaydedilecek istek. <see cref="PendingApproval.Status"/> <see cref="ApprovalStatus.Pending"/> olmalidir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask CreateAsync(PendingApproval approval, CancellationToken cancellationToken = default);

    /// <summary>Cagiranin kiracisindaki bekleyen istekleri, en eskiden en yeniye siralanmis olarak listeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    ValueTask<IReadOnlyList<PendingApproval>> ListPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Tek bir bekleyen onay istegini getirir.</summary>
    /// <param name="id">Istek kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Istek; cagiranin kiracisinda bulunamazsa <see langword="null"/>.</returns>
    ValueTask<PendingApproval?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Bekleyen bir istege karar yazar.</summary>
    /// <param name="id">Istek kimligi.</param>
    /// <param name="approved">Onaylandi mi.</param>
    /// <param name="decidedBy">Karari veren aktor.</param>
    /// <param name="decidedAt">Karar ani.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Karar yazildiysa <see langword="true"/>; istek cagiranin kiracisinda
    /// bulunamazsa veya zaten <see cref="ApprovalStatus.Pending"/> durumunda
    /// degilse <see langword="false"/> (ikinci karar sessizce reddedilir).
    /// </returns>
    ValueTask<bool> DecideAsync(
        Guid id,
        bool approved,
        string decidedBy,
        DateTimeOffset decidedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Suresi <paramref name="olderThan"/>'dan once dolmus, hala <see cref="ApprovalStatus.Pending"/>
    /// durumundaki istekleri <see cref="ApprovalStatus.Expired"/> olarak kapatir.
    /// </summary>
    /// <param name="olderThan">Bu zamandan once suresi dolmus istekler kapatilir (UTC).</param>
    /// <param name="max">Bu turda kapatilacak ust istek sayisi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kapatilan isteklerin kayitlari.</returns>
    /// <remarks>
    /// 🚨 <c>[TenantAgnostic]</c>: bu bir bakim isidir ve butun kiracilarin
    /// suresi dolmus isteklerini tarar; ambient kiraciyla suzmek diger
    /// kiracilarin isteklerini sonsuza dek <see cref="ApprovalStatus.Pending"/>
    /// birakirdi.
    /// </remarks>
    ValueTask<IReadOnlyList<PendingApproval>> ExpireAsync(
        DateTimeOffset olderThan,
        int max,
        CancellationToken cancellationToken = default);
}
