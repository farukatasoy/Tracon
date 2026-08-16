using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Knowledge;

public sealed class KnowledgeIngestionServiceTests
{
    [Fact]
    public void Not_supported_when_the_store_or_the_generator_is_missing()
    {
        CreateService(withStore: false, withEmbeddings: true).IsSupported.ShouldBeFalse();
        CreateService(withStore: true, withEmbeddings: false).IsSupported.ShouldBeFalse();
        CreateService(withStore: false, withEmbeddings: false).IsSupported.ShouldBeFalse();
    }

    [Fact]
    public void Supported_when_both_are_registered()
    {
        CreateService(withStore: true, withEmbeddings: true).IsSupported.ShouldBeTrue();
    }

    [Fact]
    public async Task Every_method_throws_AgentPrismException_when_not_supported()
    {
        var service = CreateService(withStore: false, withEmbeddings: false);

        await Should.ThrowAsync<AgentPrismException>(
            () => service.IngestAsync("kb", "source", "text", null).AsTask());
        await Should.ThrowAsync<AgentPrismException>(
            () => service.SearchAsync("kb", "query").AsTask());
        await Should.ThrowAsync<AgentPrismException>(
            () => service.DeleteSourceAsync("kb", "source").AsTask());
        await Should.ThrowAsync<AgentPrismException>(
            () => service.ListSourcesAsync("kb").AsTask());
    }

    [Fact]
    public async Task Text_is_chunked_and_embedded_when_given()
    {
        var store = new FakeVectorSearchStore();
        var embeddings = new FakeEmbeddingGenerator();
        var service = CreateService(store, embeddings, chunkSize: 5, chunkOverlap: 1);

        var count = await service.IngestAsync("kb", "source", "0123456789", chunks: null);

        count.ShouldBeGreaterThan(1);
        embeddings.Requested.Count.ShouldBe(count);

        var sources = await service.ListSourcesAsync("kb");
        sources.ShouldBe(["source"]);
    }

    [Fact]
    public async Task Prebuilt_chunks_get_an_embedding_only_when_empty_a_filled_one_stays_as_is()
    {
        var store = new FakeVectorSearchStore();
        var embeddings = new FakeEmbeddingGenerator();
        var service = CreateService(store, embeddings);

        var precomputed = new float[] { 1, 2, 3 };

        var chunks = new[]
        {
            new VectorChunk { Index = 0, Content = "unembedded", Embedding = ReadOnlyMemory<float>.Empty },
            new VectorChunk { Index = 1, Content = "embedded", Embedding = precomputed },
        };

        await service.IngestAsync("kb", "source", text: null, chunks);

        // Only the unembedded chunk went to the generator.
        embeddings.Requested.ShouldBe(["unembedded"]);
    }

    [Fact]
    public async Task Giving_both_text_and_chunks_fails()
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        await Should.ThrowAsync<ArgumentException>(() => service
            .IngestAsync("kb", "source", "text", [new VectorChunk { Index = 0, Content = "x", Embedding = new float[] { 1, 2, 3 } }])
            .AsTask());
    }

    [Fact]
    public async Task Giving_neither_text_nor_chunks_fails()
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        await Should.ThrowAsync<ArgumentException>(() => service.IngestAsync("kb", "source", null, null).AsTask());
    }

    [Fact]
    public async Task Wrong_sized_prebuilt_embedding_fails()
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        var chunks = new[] { new VectorChunk { Index = 0, Content = "x", Embedding = new float[] { 1, 2 } } };

        await Should.ThrowAsync<ArgumentException>(() => service.IngestAsync("kb", "source", null, chunks).AsTask());
    }

    [Theory]
    [InlineData("invalid collection")]
    [InlineData("invalid/collection")]
    [InlineData("")]
    public async Task Invalid_collection_name_is_rejected(string collection)
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        await Should.ThrowAsync<ArgumentException>(() => service.SearchAsync(collection, "query").AsTask());
    }

    [Fact]
    public async Task Search_passes_the_tenant_and_the_collection_through_correctly()
    {
        var store = new FakeVectorSearchStore();
        var embeddings = new FakeEmbeddingGenerator();
        var service = CreateService(store, embeddings, tenantId: "acme", maxResults: 7);

        await service.SearchAsync("kb", "query");

        var request = store.Searches.ShouldHaveSingleItem();
        request.TenantId.ShouldBe("acme");
        request.Collection.ShouldBe("kb");
        request.Top.ShouldBe(7);
    }

    private static KnowledgeIngestionService CreateService(bool withStore, bool withEmbeddings)
        => new(
            new FixedTenantContext("default"),
            Options.Create(new AgentPrismKnowledgeOptions { Dimensions = 3 }),
            withStore ? new FakeVectorSearchStore() : null,
            withEmbeddings ? new FakeEmbeddingGenerator() : null);

    private static KnowledgeIngestionService CreateService(
        FakeVectorSearchStore store,
        FakeEmbeddingGenerator embeddings,
        string tenantId = "default",
        int chunkSize = 1000,
        int chunkOverlap = 100,
        int maxResults = 5)
        => new(
            new FixedTenantContext(tenantId),
            Options.Create(new AgentPrismKnowledgeOptions
            {
                Dimensions = 3,
                ChunkSize = chunkSize,
                ChunkOverlap = chunkOverlap,
                MaxResults = maxResults,
            }),
            store,
            embeddings);
}
