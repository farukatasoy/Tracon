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
}
