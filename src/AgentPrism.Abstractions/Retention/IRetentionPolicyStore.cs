namespace AgentPrism;

/// <summary>Saklama politikalarinin ve kosu gecmisinin kontrol duzlemi deposu.</summary>
/// <remarks>
/// Veri duzlemi (fiili silme/sayma/arsiv okuma) <see cref="IRetentionStore"/>
/// uzerindendir. Bu ayrim, is kuyrugundaki <c>IJobStore</c>/<c>IJobScheduleStore</c>
/// ayrimiyla aynidir: kontrol duzlemi her zaman kayitlidir (bellek ici veya SQL),
/// veri duzlemi ise yalniz bir SQL saglayicisi acikken anlamlidir.
/// </remarks>
public interface IRetentionPolicyStore
{
    /// <summary>Bir kiracinin (ve <c>"*"</c> genel) tum politikalarini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Politikalar.</returns>
    ValueTask<IReadOnlyList<RetentionPolicy>> ListPoliciesAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir hedefin politikasini kiraci icinde arar, yoksa <c>"*"</c> genelinde arar.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="target">Hedef adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Politika; ne kiracide ne genelde varsa <see langword="null"/>.</returns>
    ValueTask<RetentionPolicy?> GetPolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default);

    /// <summary>Bir politikayi olusturur veya gunceller.</summary>
    /// <param name="policy">Politika.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaydedilen politika.</returns>
    ValueTask<RetentionPolicy> SavePolicyAsync(
        RetentionPolicy policy,
        CancellationToken cancellationToken = default);

    /// <summary>Bir politikayi siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="target">Hedef adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeletePolicyAsync(
        string tenantId,
        string target,
        CancellationToken cancellationToken = default);

    /// <summary>Yeni bir temizleme kosusu acar.</summary>
    /// <param name="run">Baslangic kaydi. <see cref="RetentionRun.CompletedAt"/> yok sayilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Olusturulan kayit.</returns>
    ValueTask<RetentionRun> CreateRunAsync(RetentionRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devam eden bir kosunun sayaclarini artirir. Her parti sonrasi cagrilir.
    /// </summary>
    /// <param name="runId">Kosu kimligi.</param>
    /// <param name="deletedDelta">Bu partide silinen satir sayisi.</param>
    /// <param name="archivedDelta">Bu partide arsivlenen satir sayisi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask AppendRunProgressAsync(
        Guid runId,
        long deletedDelta,
        long archivedDelta,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kosuyu sonlandirir.</summary>
    /// <param name="runId">Kosu kimligi.</param>
    /// <param name="completedAt">Bitis zamani (UTC).</param>
    /// <param name="errorMessage">Hata mesaji; basariliysa <see langword="null"/>.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    ValueTask CompleteRunAsync(
        Guid runId,
        DateTimeOffset completedAt,
        string? errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kiracinin kosu gecmisini listeler. En yeni kayit basta doner.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="target">Yalniz bu hedefin kosularini getirir; <see langword="null"/> ise tumu.</param>
    /// <param name="skip">Atlanacak kayit sayisi.</param>
    /// <param name="take">Getirilecek ust kayit sayisi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    ValueTask<IReadOnlyList<RetentionRun>> ListRunsAsync(
        string tenantId,
        string? target,
        int skip,
        int take,
        CancellationToken cancellationToken = default);
}
