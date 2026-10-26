using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// An embedding generator that counts its calls and reports a token count when it has one.
/// </summary>
/// <remarks>
/// A test that must prove a query was NOT embedded reads <see cref="Calls"/>; a test of the usage line sets
/// <see cref="InputTokens"/>.
/// </remarks>
internal sealed class CountingEmbeddings : IEmbeddingGenerator<string, Embedding<float>>
{
    private int _calls;

    /// <summary>Gets how many times the generator was called.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Gets the input token count each call reports, or <see langword="null"/> for a provider that reports none.</summary>
    public long? InputTokens { get; init; } = 3;

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _calls);

        var generated = new GeneratedEmbeddings<Embedding<float>>(
            values.Select(static _ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f })))
        {
            Usage = InputTokens is { } tokens ? new UsageDetails { InputTokenCount = tokens, TotalTokenCount = tokens } : null,
        };

        return Task.FromResult(generated);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>A knowledge vector store that holds nothing, counts its searches and returns the hits it was given.</summary>
internal class CountingKnowledgeStore : IVectorSearchStore
{
    private int _searches;

    /// <summary>Gets how many searches the store received.</summary>
    public int Searches => Volatile.Read(ref _searches);

    /// <summary>Gets the hits every search returns.</summary>
    public IReadOnlyList<VectorSearchHit> Hits { get; init; } = [];

    /// <inheritdoc />
    public int Dimensions => 3;

    /// <inheritdoc />
    public ValueTask UpsertAsync(
        string tenantId, string collection, string sourceId, IReadOnlyList<VectorChunk> chunks, CancellationToken cancellationToken = default)
        => default;

    /// <inheritdoc />
    public virtual ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _searches);

        return new(Hits);
    }

    /// <inheritdoc />
    public ValueTask<int> DeleteSourceAsync(string tenantId, string collection, string sourceId, CancellationToken cancellationToken = default)
        => new(0);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<string>> ListSourcesAsync(string tenantId, string collection, CancellationToken cancellationToken = default)
        => new([]);
}

/// <summary>A knowledge vector store whose search waits for ever, until the caller gives up.</summary>
internal sealed class HangingKnowledgeStore : CountingKnowledgeStore
{
    /// <inheritdoc />
    public override async ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated

        return [];
    }
}
