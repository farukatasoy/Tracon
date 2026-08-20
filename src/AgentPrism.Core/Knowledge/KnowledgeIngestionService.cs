using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Management (operator) surface for document ingestion, semantic search, and source management.
/// </summary>
/// <remarks>
/// <para>
/// Not the agent's own job —
/// The agent side is the <c>search_knowledge</c> tool produced by
/// <see cref="VectorSearchToolFactory"/>.
/// </para>
/// <para>
/// While <see cref="IsSupported"/> is <see langword="false"/>, every method
/// throws <see cref="AgentPrismException"/>; it does not silently return an
/// empty result.
/// </para>
/// </remarks>
public sealed partial class KnowledgeIngestionService
{
    private readonly ITenantContext _tenantContext;
    private readonly AgentPrismKnowledgeOptions _options;
    private readonly IVectorSearchStore? _store;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddings;

    /// <summary>Creates a new knowledge base service.</summary>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="options">Knowledge base settings.</param>
    /// <param name="store">
    /// The vector store. If <see langword="null"/>, <see cref="IsSupported"/> is
    /// <see langword="false"/> (the replaceable-extension rule: no default implementation).
    /// </param>
    /// <param name="embeddings">
    /// The embedding generator. If <see langword="null"/>, <see cref="IsSupported"/>
    /// is <see langword="false"/>; the consumer must register its own provider.
    /// </param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public KnowledgeIngestionService(
        ITenantContext tenantContext,
        IOptions<AgentPrismKnowledgeOptions> options,
        IVectorSearchStore? store = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddings = null)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);

        _tenantContext = tenantContext;
        _options = options.Value;
        _store = store;
        _embeddings = embeddings;
    }

    /// <summary>
    /// Whether the knowledge base is supported in this setup.
    /// </summary>
    /// <remarks>
    /// <see langword="true"/> only while both an <see cref="IVectorSearchStore"/>
    /// (today: PostgreSQL only) AND an
    /// <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c> are registered.
    /// </remarks>
    public bool IsSupported => _store is not null && _embeddings is not null;

    /// <summary>
    /// Ingests a document: chunks it (if not given), embeds it (if not given), and writes it.
    /// </summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="sourceId">The source identity. Re-ingesting with the same identity replaces the old one.</param>
    /// <param name="text">
    /// Raw text. If given, it is chunked with
    /// <see cref="AgentPrismKnowledgeOptions.ChunkSize"/> and
    /// <see cref="AgentPrismKnowledgeOptions.ChunkOverlap"/>, and each chunk is
    /// embedded. Cannot be given together with <paramref name="chunks"/>.
    /// </param>
    /// <param name="chunks">
    /// Ready-made chunks. Chunks with an empty <see cref="VectorChunk.Embedding"/>
    /// are embedded here; chunks that already have one are written AS IS (the
    /// consumer may bring its own embedding). Cannot be given together with
    /// <paramref name="text"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of chunks written.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> is <see langword="false"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Neither <paramref name="text"/> nor <paramref name="chunks"/> is given,
    /// both are given, or a chunk's embedding length does not match the store's dimension.
    /// </exception>
    public async ValueTask<int> IngestAsync(
        string collection,
        string sourceId,
        string? text,
        IReadOnlyList<VectorChunk>? chunks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        RequireSupported();

        if (string.IsNullOrEmpty(text) == (chunks is null or []))
        {
            throw new ArgumentException(
                $"Either {nameof(text)} or {nameof(chunks)} must be given; not both, and not neither.",
                nameof(text));
        }

        var prepared = text is not null
            ? await EmbedTextAsync(text, cancellationToken).ConfigureAwait(false)
            : await EmbedMissingAsync(chunks!, cancellationToken).ConfigureAwait(false);

        foreach (var chunk in prepared)
        {
            if (chunk.Embedding.Length != _store!.Dimensions)
            {
                throw new ArgumentException(
                    $"Chunk {chunk.Index} embedding length ({chunk.Embedding.Length}) does not match " +
                    $"the store dimension ({_store.Dimensions}).",
                    nameof(chunks));
            }
        }

        await _store!.UpsertAsync(_tenantContext.TenantId, collection, sourceId, prepared, cancellationToken)
            .ConfigureAwait(false);

        return prepared.Count;
    }

    /// <summary>Embeds a query text and returns the nearest chunks.</summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="query">The query text.</param>
    /// <param name="top">How many results to return. If not given, <see cref="AgentPrismKnowledgeOptions.MaxResults"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Results sorted by ascending distance.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> is <see langword="false"/>.</exception>
    public async ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        string collection,
        string query,
        int? top = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        RequireSupported();

        var queryEmbedding = await GenerateAsync(query, cancellationToken).ConfigureAwait(false);

        return await _store!.SearchAsync(
            new VectorSearchRequest
            {
                TenantId = _tenantContext.TenantId,
                Collection = collection,
                QueryEmbedding = queryEmbedding,
                Top = top ?? _options.MaxResults,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes all chunks of a source.</summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="sourceId">The source identity to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of chunks deleted.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> is <see langword="false"/>.</exception>
    public ValueTask<int> DeleteSourceAsync(string collection, string sourceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        RequireSupported();

        return _store!.DeleteSourceAsync(_tenantContext.TenantId, collection, sourceId, cancellationToken);
    }

    /// <summary>Lists all sources in a collection.</summary>
    /// <param name="collection">The collection name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source identities.</returns>
    /// <exception cref="AgentPrismException"><see cref="IsSupported"/> is <see langword="false"/>.</exception>
    public ValueTask<IReadOnlyList<string>> ListSourcesAsync(string collection, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        RequireValidCollectionName(collection);
        RequireSupported();

        return _store!.ListSourcesAsync(_tenantContext.TenantId, collection, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<VectorChunk>> EmbedTextAsync(string text, CancellationToken cancellationToken)
    {
        var pieces = TextChunker.Split(text, _options.ChunkSize, _options.ChunkOverlap);

        if (pieces.Count == 0)
        {
            return [];
        }

        var generated = await _embeddings!.GenerateAsync(pieces, options: null, cancellationToken).ConfigureAwait(false);
        var result = new List<VectorChunk>(pieces.Count);

        for (var index = 0; index < pieces.Count; index++)
        {
            result.Add(new VectorChunk
            {
                Index = index,
                Content = pieces[index],
                Embedding = generated[index].Vector,
            });
        }

        return result;
    }

    private async ValueTask<IReadOnlyList<VectorChunk>> EmbedMissingAsync(
        IReadOnlyList<VectorChunk> chunks,
        CancellationToken cancellationToken)
    {
        var toEmbed = new List<int>();

        for (var i = 0; i < chunks.Count; i++)
        {
            if (chunks[i].Embedding.IsEmpty)
            {
                toEmbed.Add(i);
            }
        }

        if (toEmbed.Count == 0)
        {
            return chunks;
        }

        var generated = await _embeddings!
            .GenerateAsync(toEmbed.Select(i => chunks[i].Content), options: null, cancellationToken)
            .ConfigureAwait(false);

        var result = chunks.ToArray();

        for (var i = 0; i < toEmbed.Count; i++)
        {
            var chunk = result[toEmbed[i]];
            result[toEmbed[i]] = chunk with { Embedding = generated[i].Vector };
        }

        return result;
    }

    private async ValueTask<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken cancellationToken)
    {
        var generated = await _embeddings!.GenerateAsync([text], options: null, cancellationToken).ConfigureAwait(false);
        return generated[0].Vector;
    }

    private void RequireSupported()
    {
        if (!IsSupported)
        {
            throw new AgentPrismException(
                "Knowledge base not supported: an IVectorSearchStore (today only PostgreSQL, " +
                "UsePostgreSql()) AND an IEmbeddingGenerator<string, Embedding<float>> must both " +
                "be registered.");
        }
    }

    /// <summary>
    /// Confirms that the collection name is a safe identifier. The name is
    /// passed to the query as a PARAMETER (not a SQL injection path), but since
    /// it may come from the interface it is NOT ACCEPTED as free-form text.
    /// </summary>
    /// <exception cref="ArgumentException">The name does not match the pattern.</exception>
    private static void RequireValidCollectionName(string collection)
    {
        if (!ValidCollectionName().IsMatch(collection))
        {
            throw new ArgumentException(
                $"'{collection}' is not a valid collection name. It may only contain letters, digits, " +
                "underscores, and hyphens.",
                nameof(collection));
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ValidCollectionName();
}
