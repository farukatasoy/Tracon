namespace AgentPrism;

/// <summary>
/// Veritabaninda saklanan workflow tanimlarinin deposu.
/// </summary>
/// <remarks>
/// Agent tanimlarindan farkli olarak surum <strong>gecmisi</strong> tutulmaz.
/// Gerekce: bir workflow tanimi yalnizca ad listesi ve bir desen tasir; geri
/// almak icin gereken bilgi denetim izinde (<see cref="IAuditLog"/>) zaten
/// bulunur. Agent tanimi ise talimat metni tasir ve o metnin eski hali baska
/// hicbir yerde yeniden kurulamaz.
/// </remarks>
public interface IWorkflowDefinitionStore
{
    /// <summary>Adi verilen tanimi getirir.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Workflow adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tanim; yoksa <see langword="null"/>.</returns>
    ValueTask<WorkflowDefinition?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kiracinin tum tanimlarini ada gore listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tanimlar.</returns>
    ValueTask<IReadOnlyList<WorkflowDefinition>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tanimi kaydeder. Gelen <see cref="WorkflowDefinition.Version"/> degeri yok
    /// sayilir; surum numarasini depo belirler.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="definition">Kaydedilecek tanim.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Surum numarasi atanmis tanim.</returns>
    ValueTask<WorkflowDefinition> SaveAsync(
        string tenantId,
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>Tanimi siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="name">Workflow adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silme gerceklestiyse <see langword="true"/>.</returns>
    ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default);
}
