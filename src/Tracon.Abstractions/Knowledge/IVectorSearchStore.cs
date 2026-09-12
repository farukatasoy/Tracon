namespace Tracon;

/// <summary>
/// The embedding-based semantic search store.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is NO default implementation</strong>. The only
/// concrete implementation is in <c>Tracon.PostgreSql</c> and requires the
/// <c>pgvector</c> extension. A SQL Server or SQLite consumer may register
/// their own implementation; when none is registered, <c>EnableVectorSearch = true</c>
/// throws an explicit error at startup instead of silently returning an empty result.
/// </para>
/// <para>
/// This interface does NOT wrap <c>Microsoft.Extensions.VectorData.VectorStore</c>.
/// Measured (10.8.0): that type requires an
/// <c>Expression&lt;Func&lt;TRecord,bool&gt;&gt;</c> filter; translating an
/// expression tree breaks the AOT stance, and <c>Tracon.PostgreSql</c> is
/// AOT-compatible.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton, optional.</strong> No default
/// implementation is registered; <c>Tracon.PostgreSql</c> registers one
/// with <c>TryAdd</c> when <c>EnableVectorSearch = true</c>. A consumer's
/// own registration must be a singleton — a scoped registration would be a
/// captive dependency.
/// </para>
/// </remarks>
public interface IVectorSearchStore
{
    /// <summary>The embedding dimensionality the store expects.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Writes chunks together with their embeddings. If the same source
    /// (<paramref name="sourceId"/>) is written again, the old chunks are deleted.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="collection">The collection name.</param>
    /// <param name="sourceId">The source identifier distinguishing the document.</param>
    /// <param name="chunks">The chunks to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <exception cref="ArgumentException">
    /// A chunk's embedding length does not match <see cref="Dimensions"/>.
    /// </exception>
    ValueTask UpsertAsync(
        string tenantId,
        string collection,
        string sourceId,
        IReadOnlyList<VectorChunk> chunks,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the chunks nearest to the embedding.</summary>
    /// <param name="request">The search request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Results ordered by ascending distance.</returns>
    ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all chunks of a source.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="collection">The collection name.</param>
    /// <param name="sourceId">The source identifier to delete.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted chunks.</returns>
    ValueTask<int> DeleteSourceAsync(
        string tenantId,
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists all source identifiers in a collection.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="collection">The collection name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source identifiers, in alphabetical order.</returns>
    ValueTask<IReadOnlyList<string>> ListSourcesAsync(
        string tenantId,
        string collection,
        CancellationToken cancellationToken = default);
}
