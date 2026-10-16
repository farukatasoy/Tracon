using System.ComponentModel;

namespace Tracon;

/// <summary>Request to upload a document.</summary>
/// <remarks>
/// Either <c>Text</c> or <see cref="Chunks"/> is given; both or neither
/// cannot be given (see <see cref="KnowledgeIngestionService.IngestAsync"/>).
/// </remarks>
public sealed record UploadDocumentRequest
{
    /// <summary>Source identifier. Uploading again with the same identifier replaces the old one.</summary>
    public required string SourceId { get; init; }

    /// <summary>
    /// Raw text. If given, the server chunks and embeds it.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Ready-made chunks. If <c>UploadDocumentChunk.Embedding</c> is
    /// left empty, the server embeds it; if it is filled in, it is written AS IS.
    /// </summary>
    public IReadOnlyList<UploadDocumentChunk>? Chunks { get; init; }
}

/// <summary>A single ready-made chunk in an upload request.</summary>
public sealed record UploadDocumentChunk
{
    /// <summary>Sequence number within the source.</summary>
    public required int Index { get; init; }

    /// <summary>The chunk's text.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// The chunk's embedding. If empty, the server embeds it; if filled in, it
    /// is written as is and returns 400 if it does not match the store's dimension.
    /// </summary>
    public float[]? Embedding { get; init; }

    /// <summary>Optional metadata.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

[Description("Result of a document upload request.")]
internal sealed record UploadDocumentResponse(
    [property: Description("Identifier of the uploaded source.")] string SourceId,
    [property: Description("Number of chunks written.")] int ChunkCount);

/// <summary>A semantic search request.</summary>
public sealed record SearchKnowledgeRequest
{
    /// <summary>Natural language query to search.</summary>
    public required string Query { get; init; }

    /// <summary>Number of results to return. If not given, the default in configuration is used.</summary>
    public int? Top { get; init; }
}

[Description("A semantic search hit.")]
internal sealed record SearchKnowledgeHit
{
    [Description("Identifier of the source the chunk belongs to.")]
    public required string SourceId { get; init; }

    [Description("Sequence number within the source.")]
    public required int ChunkIndex { get; init; }

    [Description("The chunk's text.")]
    public required string Content { get; init; }

    [Description("Cosine distance. A smaller value means closer.")]
    public required double Distance { get; init; }

    [Description("Optional metadata given at write time.")]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
