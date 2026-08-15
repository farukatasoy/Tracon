namespace AgentPrism;

/// <summary>Options for the knowledge base (semantic search) — Phase 51.</summary>
/// <remarks>
/// Reads values from the <c>AgentPrism:Knowledge</c> configuration section. It does
/// not require a separate <c>Use...()</c> call, for the same reason as scheduling
/// and quotas. It becomes functional only when <see cref="IVectorSearchStore"/> and
/// <c>IEmbeddingGenerator</c> are registered.
/// </remarks>
public sealed class AgentPrismKnowledgeOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "AgentPrism:Knowledge";

    /// <summary>
    /// The embedding dimensions. They are fixed with the schema. Changing them
    /// invalidates all embeddings. See <c>docs/51-VEKTOR-BELLEK-VE-RAG.md</c>.
    /// </summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>The chunk length in characters.</summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>The overlap length between consecutive chunks, in characters.</summary>
    public int ChunkOverlap { get; set; } = 100;

    /// <summary>The maximum number of results returned by the <c>search_knowledge</c> tool.</summary>
    public int MaxResults { get; set; } = 5;
}
