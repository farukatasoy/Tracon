using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Produces the <c>search_knowledge</c> tool (Phase 51).
/// </summary>
/// <remarks>
/// K2 is preserved: the tool is defined here, in code; <see cref="AgentDefinitionCompiler"/>
/// binds this tool only while <see cref="MemorySettings.EnableVectorSearch"/> is
/// on, and it cannot be written from the interface.
/// </remarks>
internal static class VectorSearchToolFactory
{
    /// <summary>Builds a <c>search_knowledge</c> tool that looks at a specific collection.</summary>
    /// <param name="store">The vector store.</param>
    /// <param name="embeddings">The embedding generator.</param>
    /// <param name="tenantId">
    /// The tenant the query runs for. Resolved at compile time (see
    /// <see cref="AgentDefinitionCompiler"/>); it does NOT RE-READ an ambient
    /// context at tool-call time — a compiled agent is bound to a single definition.
    /// </param>
    /// <param name="collection">The collection name to search.</param>
    /// <param name="maxResults">The maximum number of results to return.</param>
    /// <returns>The tool the model can call.</returns>
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
            description: "Performs a semantic search over the knowledge base. Returns " +
                         "semantically close chunks even without a keyword match.");
    }

    private sealed class VectorSearchToolBody(
        IVectorSearchStore store,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        string tenantId,
        string collection,
        int maxResults)
    {
        [Description("Performs a semantic search over the knowledge base.")]
        public async Task<IReadOnlyList<VectorSearchToolResult>> SearchKnowledgeAsync(
            [Description("The natural-language query to search for.")] string query,
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

/// <summary>A single result of a <c>search_knowledge</c> tool call.</summary>
/// <param name="SourceId">The identity of the source the chunk belongs to.</param>
/// <param name="ChunkIndex">The sequence number within the source.</param>
/// <param name="Content">The chunk's text.</param>
/// <param name="Distance">The cosine distance. A smaller value means closer.</param>
internal sealed record VectorSearchToolResult(string SourceId, int ChunkIndex, string Content, double Distance);
