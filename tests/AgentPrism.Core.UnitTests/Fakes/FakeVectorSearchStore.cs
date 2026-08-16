namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// A fake <see cref="IVectorSearchStore"/>. Uses no real database; it holds
/// data in memory. Used only by the compiler (<see cref="AgentDefinitionCompiler"/>)
/// and <see cref="KnowledgeIngestionService"/> tests.
/// </summary>
internal sealed class FakeVectorSearchStore : IVectorSearchStore
{
    private readonly Dictionary<(string Tenant, string Collection, string Source), List<VectorChunk>> _data = new();

    public int Dimensions { get; init; } = 3;

    public List<VectorSearchRequest> Searches { get; } = [];

    public ValueTask UpsertAsync(
        string tenantId,
        string collection,
        string sourceId,
        IReadOnlyList<VectorChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Embedding.Length != Dimensions)
            {
                throw new ArgumentException(
                    $"Chunk {chunk.Index} embedding length ({chunk.Embedding.Length}) does not match " +
                    $"the store dimension ({Dimensions}).",
                    nameof(chunks));
            }
        }

        _data[(tenantId, collection, sourceId)] = [.. chunks];
        return default;
    }

    public ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        Searches.Add(request);

        IReadOnlyList<VectorSearchHit> hits = _data
            .Where(entry =>
                string.Equals(entry.Key.Tenant, request.TenantId, StringComparison.Ordinal) &&
                string.Equals(entry.Key.Collection, request.Collection, StringComparison.Ordinal))
            .SelectMany(entry => entry.Value.Select(chunk => new VectorSearchHit
            {
                SourceId = entry.Key.Source,
                ChunkIndex = chunk.Index,
                Content = chunk.Content,
                Metadata = chunk.Metadata,
                Distance = 0,
            }))
            .Take(request.Top)
            .ToArray();

        return new ValueTask<IReadOnlyList<VectorSearchHit>>(hits);
    }

    public ValueTask<int> DeleteSourceAsync(
        string tenantId,
        string collection,
        string sourceId,
        CancellationToken cancellationToken = default)
    {
        var key = (tenantId, collection, sourceId);
        var removed = _data.Remove(key, out var chunks);
        return new ValueTask<int>(removed ? chunks!.Count : 0);
    }

    public ValueTask<IReadOnlyList<string>> ListSourcesAsync(
        string tenantId,
        string collection,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> sources = _data.Keys
            .Where(key =>
                string.Equals(key.Tenant, tenantId, StringComparison.Ordinal) &&
                string.Equals(key.Collection, collection, StringComparison.Ordinal))
            .Select(static key => key.Source)
            .OrderBy(static source => source, StringComparer.Ordinal)
            .ToArray();

        return new ValueTask<IReadOnlyList<string>>(sources);
    }
}
