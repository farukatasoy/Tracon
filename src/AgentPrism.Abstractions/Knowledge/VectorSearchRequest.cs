namespace AgentPrism;

/// <summary>A semantic search request.</summary>
public sealed record VectorSearchRequest
{
    /// <summary>The tenant identifier.</summary>
    public required string TenantId { get; init; }

    /// <summary>The collection name.</summary>
    public required string Collection { get; init; }

    /// <summary>The embedding of the query text.</summary>
    public required ReadOnlyMemory<float> QueryEmbedding { get; init; }

    /// <summary>The number of results to return.</summary>
    public int Top { get; init; } = 5;

    /// <summary>
    /// The largest cosine distance accepted (0 = identical, 2 = opposite).
    /// No distance filter is applied when <see langword="null"/>.
    /// </summary>
    public double? MaxDistance { get; init; }
}
