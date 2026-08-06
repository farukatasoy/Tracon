namespace AgentPrism;

/// <summary>
/// Calistirma ve mesaj puanlarinin (<see cref="RunScore"/>) deposu.
/// </summary>
/// <remarks>
/// <see cref="IRunStore"/>'a metot olarak eklenmez: bir puan farkli bir yasam
/// dongusu izler (seyrek yazilir, <see cref="IRunStore"/> her calistirmada
/// yazilan sicak yoldadir) ve ayri bir arayuz her genisleme noktasinin
/// degistirilebilir olmasi kuraliyla (K4) daha iyi ortusur.
/// </remarks>
public interface IRunScoreStore
{
    /// <summary>Bir puani ekler veya gunceller.</summary>
    /// <param name="score">Puan. <see cref="RunScore.Id"/> bos ise (<see cref="Guid.Empty"/>) yeni bir kimlik uretilir.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yazilan puan.</returns>
    /// <remarks>
    /// Ayni yazar (<see cref="RunScore.Author"/>) ayni hedefi (calistirma veya
    /// mesaj) ikinci kez puanladiginda satir <strong>guncellenir</strong>, yeni
    /// satir acilmaz. <see cref="RunScore.Author"/> bos ise bu kural
    /// uygulanmaz; her cagri yeni bir satir yazar.
    /// </remarks>
    ValueTask<RunScore> UpsertAsync(RunScore score, CancellationToken cancellationToken = default);

    /// <summary>Bir calistirmanin tum puanlarini listeler.</summary>
    /// <param name="tenantId">Kiraci.</param>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Puanlar. Sira garantisi yoktur.</returns>
    ValueTask<IReadOnlyList<RunScore>> ListAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir puani siler.</summary>
    /// <param name="tenantId">Kiraci.</param>
    /// <param name="scoreId">Puan kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silindiyse <see langword="true"/>; boyle bir puan yoksa <see langword="false"/>.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        Guid scoreId,
        CancellationToken cancellationToken = default);
}
