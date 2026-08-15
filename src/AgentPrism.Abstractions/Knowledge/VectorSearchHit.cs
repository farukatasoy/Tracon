namespace AgentPrism;

/// <summary>A single search result.</summary>
public sealed record VectorSearchHit
{
    /// <summary>The identifier of the source the chunk belongs to.</summary>
    public required string SourceId { get; init; }

    /// <summary>The sequence number within the source.</summary>
    public required int ChunkIndex { get; init; }

    /// <summary>The chunk's text.</summary>
    public required string Content { get; init; }

    /// <summary>The cosine distance. A smaller value means closer.</summary>
    public required double Distance { get; init; }

    /// <summary>The optional metadata given at write time.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
