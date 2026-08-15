namespace AgentPrism;

/// <summary>A single chunk to write.</summary>
public sealed record VectorChunk
{
    /// <summary>The sequence number within the source (0-based).</summary>
    public required int Index { get; init; }

    /// <summary>The chunk's text.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The chunk's embedding. 🚨 Its length MUST match
    /// <see cref="IVectorSearchStore.Dimensions"/>; otherwise the write fails.
    /// </summary>
    public required ReadOnlyMemory<float> Embedding { get; init; }

    /// <summary>Optional metadata. Can be used for filtering.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
