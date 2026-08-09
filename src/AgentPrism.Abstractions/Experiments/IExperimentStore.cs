namespace AgentPrism;

/// <summary>A/B deneylerinin deposu.</summary>
public interface IExperimentStore
{
    /// <summary>Kiracinin tum deneylerini adina gore listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Deneyler.</returns>
    ValueTask<IReadOnlyList<Experiment>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Adi verilen deneyi getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Deney adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Deney; yoksa <see langword="null"/>.</returns>
    ValueTask<Experiment?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bu agent icin <see cref="ExperimentStatus.Running"/> durumunda olan deneyi getirir.
    /// Yoksa <see langword="null"/>. Bir agent icin ayni anda en fazla bir calisan deney olabilir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="agentName">Agent adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Calisan deney; yoksa <see langword="null"/>.</returns>
    ValueTask<Experiment?> GetRunningAsync(string tenantId, string agentName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deneyi olusturur veya gunceller. Yalnizca <see cref="ExperimentStatus.Draft"/>
    /// durumundaki bir deney guncellenebilir; baslatilmis bir deneyi guncellemeye
    /// calismak <see cref="AgentPrismException"/> firlatir.
    /// </summary>
    /// <param name="experiment">Kaydedilecek deney.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaydedilen deney.</returns>
    ValueTask<Experiment> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default);

    /// <summary>Deneyi siler. Calisan bir deney silinemez; once durdurulmalidir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Deney adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deneyi <see cref="ExperimentStatus.Running"/>'e gecirir. Ayni agent icin baska bir
    /// calisan deney varsa <see cref="AgentPrismException"/> firlatir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Deney adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Guncellenmis deney.</returns>
    ValueTask<Experiment> StartAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deneyi <see cref="ExperimentStatus.Stopped"/>'a gecirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Deney adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Guncellenmis deney.</returns>
    ValueTask<Experiment> StopAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Butun kiracilardaki, <see cref="ExperimentStatus.Running"/> durumunda VE
    /// <see cref="Experiment.Canary"/> tanimli deneyleri listeler.
    /// </summary>
    /// <remarks>
    /// Bir bakim islemidir (kanarya degerlendiricisi) ve <c>IPendingApprovalStore.ExpireAsync</c>
    /// ile AYNI gerekceyle butun kiracilari tarar.
    /// </remarks>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kanarya kurali tanimli, calisan deneyler.</returns>
    ValueTask<IReadOnlyList<Experiment>> ListRunningWithCanaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deneyin kanarya kuralini tanimlar veya kaldirir (<paramref name="policy"/> <see langword="null"/>).
    /// </summary>
    /// <remarks>
    /// <see cref="SaveAsync"/>'in aksine deneyin durumundan BAGIMSIZ calisir (Draft
    /// veya Running) — bir kanarya kurali, deney zaten trafik alirken de tanimlanabilir.
    /// </remarks>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Deney adi.</param>
    /// <param name="policy">Yeni kural; kaldirmak icin <see langword="null"/>.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Guncellenmis deney.</returns>
    ValueTask<Experiment> SetCanaryPolicyAsync(
        string tenantId,
        string name,
        CanaryPolicy? policy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kanarya degerlendiricisinin kademeli artirma kararini uygular: deney
    /// <see cref="ExperimentStatus.Running"/>'de kalir, yalnizca kol agirliklari
    /// degisir. Yalnizca kanarya degerlendirme servisi tarafindan cagrilir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Deney adi.</param>
    /// <param name="variants">Yeni kol agirliklari. Toplam 100 olmalidir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Guncellenmis deney.</returns>
    ValueTask<Experiment> AdvanceCanaryRampAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kanarya degerlendiricisinin otomatik geri alma kararini uygular: deneyi
    /// <see cref="ExperimentStatus.Stopped"/>'a gecirir, agirliklari kontrol koluna
    /// dondurur ve <see cref="Experiment.RollbackReason"/> yazar.
    /// </summary>
    /// <remarks>
    /// 🚨 K-089 emsali: caginan taraf (kanarya degerlendirme servisi) bu metodu
    /// cagirmadan ONCE denetim izine yazmis olmalidir; yazma basarisiz olursa bu
    /// metot hic cagrilmamalidir — "denetim izine yazilamayan bir geri alma
    /// uygulanmaz".
    /// </remarks>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Deney adi.</param>
    /// <param name="variants">Kontrol koluna dondurulmus agirliklar. Toplam 100 olmalidir.</param>
    /// <param name="reason">Geri alma nedeni.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Guncellenmis deney.</returns>
    ValueTask<Experiment> RollbackCanaryAsync(
        string tenantId,
        string name,
        IReadOnlyList<ExperimentVariant> variants,
        string reason,
        CancellationToken cancellationToken = default);
}
