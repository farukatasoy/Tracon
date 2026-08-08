namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>
/// <see cref="IVectorSearchStore"/> sahtesi. Gercek bir veritabani kullanmaz;
/// bellekte tutar. Yalniz derleyici (<see cref="AgentDefinitionCompiler"/>) ve
/// <see cref="KnowledgeIngestionService"/> testleri icindir.
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
                    $"Parca {chunk.Index} gomu uzunlugu ({chunk.Embedding.Length}) depo boyutuyla " +
                    $"({Dimensions}) eslesmiyor.",
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
