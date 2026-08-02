using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// Workflow kontrol noktalarinin deposu.
/// </summary>
/// <remarks>
/// <para>
/// Sozlesme bilerek Microsoft Agent Framework tipi <strong>tasimaz</strong>:
/// durum bir <see cref="JsonElement"/> olarak ifade edilir. Boylece
/// <c>AgentPrism.PostgreSql</c> kontrol noktalarini saklarken workflow
/// motorunun 130 tipli API yuzeyine baglanmaz. MAF'in
/// <c>ICheckpointStore&lt;JsonElement&gt;</c> arayuzune uyarlama
/// <c>AgentPrism.Workflows</c> icinde yapilir.
/// </para>
/// <para>
/// 🚨 <strong>Her okuma kiraci filtresiyle yapilir.</strong> Bir kontrol noktasi
/// tum yurutme durumunu tasir; baska bir kiracinin noktasi <em>bulunamadi</em>
/// doner - "yetkisiz" bile denmez, varligi sizdirilmaz.
/// </para>
/// </remarks>
public interface IWorkflowCheckpointStore
{
    /// <summary>Yeni bir kontrol noktasi yazar.</summary>
    /// <param name="record">Yazilacak kayit.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Yazilan kayit.</returns>
    ValueTask<WorkflowCheckpointRecord> CreateAsync(
        WorkflowCheckpointRecord record,
        CancellationToken cancellationToken = default);

    /// <summary>Tek bir kontrol noktasinin durumunu okur.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="sessionId">Yurutme oturumu kimligi.</param>
    /// <param name="checkpointId">Kontrol noktasi kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Opak durum; kayit yoksa veya baska kiraciya aitse <see langword="null"/>.</returns>
    ValueTask<JsonElement?> ReadAsync(
        string tenantId,
        string sessionId,
        string checkpointId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bir oturumun kontrol noktalarini eskiden yeniye listeler. Durum yuku
    /// <strong>okunmaz</strong>; yalnizca ustveri doner.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="sessionId">Yurutme oturumu kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tek bir calistirmanin urettigi kontrol noktalarini eskiden yeniye listeler.
    /// Durum yuku <strong>okunmaz</strong>.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kayitlar.</returns>
    /// <remarks>
    /// Oturum bazli listeden ayridir: bir oturum birden cok calistirma tasiyabilir
    /// (sürdürme her seferinde yeni bir calistirma acar) ve arayuz "bu calistirma
    /// nereden devam ettirilebilir" sorusunu sorar.
    /// </remarks>
    ValueTask<IReadOnlyList<WorkflowCheckpointRecord>> ListByRunAsync(
        string tenantId,
        Guid runId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir oturumun tum kontrol noktalarini siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="sessionId">Yurutme oturumu kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silinen kayit sayisi.</returns>
    ValueTask<int> DeleteAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default);
}
