namespace AgentPrism;

/// <summary>
/// Veritabaninda saklanan agent tanimlarinin deposu. Surumleme ve geri alma
/// destegi zorunludur: her kayit yeni bir surum uretir, eski surumler silinmez.
/// </summary>
public interface IAgentDefinitionStore
{
    /// <summary>Adi verilen tanimin guncel surumunu getirir.</summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tanim; yoksa <see langword="null"/>.</returns>
    ValueTask<AgentDefinition?> GetAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Adi verilen tanimin belirtilen surumunu getirir.</summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="version">Istenen surum numarasi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tanim; o surum yoksa <see langword="null"/>.</returns>
    ValueTask<AgentDefinition?> GetVersionAsync(string name, int version, CancellationToken cancellationToken = default);

    /// <summary>Tum tanimlarin guncel surumlerini listeler.</summary>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tanimlar.</returns>
    ValueTask<IReadOnlyList<AgentDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tanimi kaydeder ve yeni bir surum uretir. Gelen tanimdaki
    /// <see cref="AgentDefinition.Version"/> degeri yok sayilir; surum numarasini
    /// depo belirler.
    /// </summary>
    /// <param name="definition">Kaydedilecek tanim.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yeni surum numarasi atanmis tanim.</returns>
    ValueTask<AgentDefinition> SaveAsync(AgentDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Tanimi ve tum surumlerini siler.</summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Bir tanimin tum surumlerini, yeniden eskiye dogru listeler.</summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Surum gecmisi.</returns>
    ValueTask<IReadOnlyList<AgentDefinition>> ListVersionsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Belirtilen surumu guncel hale getirir. Geri alma islemi eski surumu
    /// silmez; icerigini yeni bir surum olarak kaydeder.
    /// </summary>
    /// <param name="name">Agent adi.</param>
    /// <param name="version">Geri donulecek surum numarasi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yeni surum olarak kaydedilmis tanim.</returns>
    ValueTask<AgentDefinition> RollbackAsync(string name, int version, CancellationToken cancellationToken = default);
}
