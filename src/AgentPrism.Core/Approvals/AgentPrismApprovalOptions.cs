namespace AgentPrism;

/// <summary>Asenkron onay kutusu (Faz 55) ayarlari.</summary>
/// <remarks>
/// <c>AgentPrism:Approvals</c> yapilandirma bolumunden okunur. Bkz.
/// <c>AgentPrismServiceCollectionExtensions.AddAgentPrism</c>.
/// </remarks>
public sealed class AgentPrismApprovalOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Approvals";

    /// <summary>
    /// Bir bekleyen onay istegi olusturuldugunda bu sureden sonra
    /// <see cref="ApprovalStatus.Expired"/> sayilir.
    /// </summary>
    /// <remarks>
    /// Varsayilan 24 saat: onay cogu zaman bir insana gider ve kisa bir
    /// sinir (ornegin bir saat) gece calisan bir vardiyada yetmez.
    /// </remarks>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Suresi dolmus istekleri kapatan arka plan taramasi acik mi.
    /// </summary>
    /// <remarks>
    /// 🚨 Varsayilan <see langword="true"/>: <see cref="RunReconciliationOptions"/>'in
    /// aksine bu bir bakim kolayligi degil, bir guvenlik geregidir — suresiz
    /// bekleyen bir onay istegi bir sizintidir (bkz. <c>IPendingApprovalStore.ExpireAsync</c>).
    /// </remarks>
    public bool ExpirationEnabled { get; set; } = true;

    /// <summary>Sure sonu taramalari arasindaki bekleme.</summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Bir turda kapatilacak ust istek sayisi.</summary>
    public int MaxPerScan { get; set; } = 100;
}
