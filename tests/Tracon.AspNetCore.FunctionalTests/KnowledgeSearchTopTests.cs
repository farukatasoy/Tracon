using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The <c>top</c> of a knowledge search is a positive number or it is refused with <c>400</c>.
/// </summary>
/// <remarks>
/// A zero or negative value went to the vector store as the <c>LIMIT</c> of a query, where
/// PostgreSQL refuses a negative one and the host answered <c>500</c>.
/// </remarks>
public sealed class KnowledgeSearchTopTests
{
    private static readonly Uri SearchUri = new("/tracon/api/knowledge/kb/search", UriKind.Relative);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task A_top_below_one_is_refused(int top)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy", top });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task A_positive_top_is_searched(int top)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy", top });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_search_without_a_top_uses_the_configured_number()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(SearchUri, new { query = "refund policy" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(
            configureServices: static services =>
            {
                services.Configure<TraconKnowledgeOptions>(static options => options.Dimensions = 3);
                services.AddSingleton<IVectorSearchStore>(new EmptyVectorSearchStore());
                services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(new ConstantEmbeddingGenerator());
            });

    private sealed class ConstantEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values,
            EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(static _ => new Embedding<float>(new float[] { 0.1f, 0.2f, 0.3f }))));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class EmptyVectorSearchStore : IVectorSearchStore
    {
        public int Dimensions => 3;

        public ValueTask UpsertAsync(
            string tenantId, string collection, string sourceId, IReadOnlyList<VectorChunk> chunks, CancellationToken cancellationToken = default)
            => default;

        public ValueTask<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken = default)
            => new([]);

        public ValueTask<int> DeleteSourceAsync(string tenantId, string collection, string sourceId, CancellationToken cancellationToken = default)
            => new(0);

        public ValueTask<IReadOnlyList<string>> ListSourcesAsync(string tenantId, string collection, CancellationToken cancellationToken = default)
            => new([]);
    }
}
