namespace AgentPrism;

/// <summary>
/// Gomu (embedding) tabanli anlamsal arama deposu.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>Varsayilan uygulama YOKTUR</strong> (K4). Tek somut uygulama
/// <c>AgentPrism.PostgreSql</c> icindedir ve <c>pgvector</c> uzantisini ister.
/// SQL Server ve SQLite tuketicisi kendi uygulamasini kaydedebilir; hicbiri
/// kayitli degilken <c>EnableVectorSearch = true</c> acilista acik bir hata
/// verir, sessizce bos sonuc donmez.
/// </para>
/// <para>
/// Bu arayuz <c>Microsoft.Extensions.VectorData.VectorStore</c>'u SARMALAMAZ.
/// Olculdu (10.8.0): o tip <c>Expression&lt;Func&lt;TRecord,bool&gt;&gt;</c>
/// suzgeci ister; ifade agaci cevirmek AOT duruşunu bozar ve
/// <c>AgentPrism.PostgreSql</c> AOT uyumludur. Gerekce: <c>docs/KARARLAR.md</c>.
/// </para>
/// </remarks>
public interface IVectorSearchStore
{
    /// <summary>Deponun beklediği gomu boyutu.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Parcalari gomuleriyle birlikte yazar. Ayni kaynak (<paramref name="sourceId"/>)
    /// yeniden yazilirsa eski parcalari silinir.
    /// </summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="collection">Koleksiyon adi.</param>
    /// <param name="sourceId">Belgeyi ayirt eden kaynak kimligi.</param>
    /// <param name="chunks">Yazilacak parcalar.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <exception cref="ArgumentException">
    /// Bir parcanin gomu uzunlugu <see cref="Dimensions"/> ile eslesmiyorsa.
    /// </exception>
    ValueTask UpsertAsync(
        string tenantId,
        string collection,
        string sourceId,
        IReadOnlyList<VectorChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>Gomuye en yakin parcalari dondurur.</summary>
    /// <param name="request">Arama istegi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Mesafeye gore artan sirali sonuclar.</returns>
    ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Bir kaynagin tum parcalarini siler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="collection">Koleksiyon adi.</param>
    /// <param name="sourceId">Silinecek kaynak kimligi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Silinen parca sayisi.</returns>
    ValueTask<int> DeleteSourceAsync(
        string tenantId,
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Bir koleksiyondaki tum kaynak kimliklerini listeler.</summary>
    /// <param name="tenantId">Kiraci kimligi.</param>
    /// <param name="collection">Koleksiyon adi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kaynak kimlikleri, alfabetik sirali.</returns>
    ValueTask<IReadOnlyList<string>> ListSourcesAsync(
        string tenantId,
        string collection,
        CancellationToken cancellationToken = default);
}
