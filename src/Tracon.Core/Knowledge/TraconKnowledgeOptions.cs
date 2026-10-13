namespace Tracon;

/// <summary>Options for the knowledge base (semantic search).</summary>
/// <remarks>
/// Reads values from the <c>Tracon:Knowledge</c> configuration section. It does
/// not require a separate <c>Use...()</c> call, for the same reason as scheduling
/// and quotas. It becomes functional only when <see cref="IVectorSearchStore"/> and
/// <c>IEmbeddingGenerator</c> are registered.
/// </remarks>
public sealed class TraconKnowledgeOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Tracon:Knowledge";

    /// <summary>
    /// The embedding dimensions. They are fixed with the schema. Changing them
    /// invalidates all embeddings.
    /// </summary>
    public int Dimensions { get; set; } = 1536;

    /// <summary>The chunk length in characters.</summary>
    public int ChunkSize { get; set; } = 1000;

    /// <summary>The overlap length between consecutive chunks, in characters.</summary>
    public int ChunkOverlap { get; set; } = 100;

    /// <summary>The maximum number of results returned by the <c>search_knowledge</c> tool.</summary>
    public int MaxResults { get; set; } = 5;

    /// <summary>
    /// The name of the provider the registered embedding generator calls, as a
    /// tenant egress policy names it (for example <c>azure-openai</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every embedding call — a <c>search_knowledge</c> query, a knowledge search and
    /// a document upload — checks the tenant's egress policy against this name
    /// before the text leaves the process, and refuses a tenant that has its own
    /// credential for this provider: the generator was built with the setup key and
    /// cannot use the tenant's.
    /// </para>
    /// <para>
    /// When it is not set, the provider name the generator reports is used. That
    /// name does not always match a policy name: the OpenAI and the Azure OpenAI
    /// generators both report <c>openai</c>. When neither name is known, a tenant
    /// with an egress policy is refused, and a tenant without one is not.
    /// </para>
    /// </remarks>
    public string? EmbeddingProvider { get; set; }
}
