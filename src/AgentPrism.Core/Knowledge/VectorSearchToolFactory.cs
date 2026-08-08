using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// <c>search_knowledge</c> tool'unu uretir (Faz 51).
/// </summary>
/// <remarks>
/// K2 korunur: tool burada, kodda tanimlidir; <see cref="AgentDefinitionCompiler"/>
/// yalnizca <see cref="MemorySettings.EnableVectorSearch"/> acikken bu tool'u
/// baglar, arayuzden yazilamaz.
/// </remarks>
internal static class VectorSearchToolFactory
{
    /// <summary>Belirli bir koleksiyona bakan bir <c>search_knowledge</c> tool'u kurar.</summary>
    /// <param name="store">Vektor deposu.</param>
    /// <param name="embeddings">Gomu ureticisi.</param>
    /// <param name="tenantId">
    /// Sorgunun calisacagi kiraci. Derleme aninda cozulur (bkz.
    /// <see cref="AgentDefinitionCompiler"/>); tool cagri aninda ambient bir
    /// baglami YENIDEN OKUMAZ — derlenen agent tek bir tanima baglidir.
    /// </param>
    /// <param name="collection">Aranacak koleksiyon adi.</param>
    /// <param name="maxResults">Dondurulecek en fazla sonuc sayisi.</param>
    /// <returns>Modelin cagirabilecegi tool.</returns>
    public static AIFunction Create(
        IVectorSearchStore store,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        string tenantId,
        string collection,
        int maxResults)
    {
        var body = new VectorSearchToolBody(store, embeddings, tenantId, collection, maxResults);

        return AIFunctionFactory.Create(
            body.SearchKnowledgeAsync,
            name: "search_knowledge",
            description: "Bilgi tabaninda anlamsal arama yapar. Kelime eslesmesi olmasa bile " +
                         "anlamca yakin parcalari dondurur.");
    }

    private sealed class VectorSearchToolBody(
        IVectorSearchStore store,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        string tenantId,
        string collection,
        int maxResults)
    {
        [Description("Bilgi tabaninda anlamsal arama yapar.")]
        public async Task<IReadOnlyList<VectorSearchToolResult>> SearchKnowledgeAsync(
            [Description("Aranacak dogal dil sorgusu.")] string query,
            CancellationToken cancellationToken)
        {
            var generated = await embeddings.GenerateAsync([query], options: null, cancellationToken)
                .ConfigureAwait(false);

            var hits = await store.SearchAsync(
                new VectorSearchRequest
                {
                    TenantId = tenantId,
                    Collection = collection,
                    QueryEmbedding = generated[0].Vector,
                    Top = maxResults,
                },
                cancellationToken).ConfigureAwait(false);

            return hits
                .Select(static hit => new VectorSearchToolResult(hit.SourceId, hit.ChunkIndex, hit.Content, hit.Distance))
                .ToArray();
        }
    }
}

/// <summary>Bir <c>search_knowledge</c> tool cagrisinin tek bir sonucu.</summary>
/// <param name="SourceId">Parcanin ait oldugu kaynak kimligi.</param>
/// <param name="ChunkIndex">Kaynak icindeki sira numarasi.</param>
/// <param name="Content">Parcanin metni.</param>
/// <param name="Distance">Kosinus mesafesi. Kucuk deger daha yakin demektir.</param>
internal sealed record VectorSearchToolResult(string SourceId, int ChunkIndex, string Content, double Distance);
