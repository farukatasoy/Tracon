using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Knowledge;

public sealed class KnowledgeIngestionServiceTests
{
    [Fact]
    public void Store_veya_uretici_eksikse_desteklenmiyor()
    {
        CreateService(withStore: false, withEmbeddings: true).IsSupported.ShouldBeFalse();
        CreateService(withStore: true, withEmbeddings: false).IsSupported.ShouldBeFalse();
        CreateService(withStore: false, withEmbeddings: false).IsSupported.ShouldBeFalse();
    }

    [Fact]
    public void Ikisi_de_kayitliyken_destekleniyor()
    {
        CreateService(withStore: true, withEmbeddings: true).IsSupported.ShouldBeTrue();
    }

    [Fact]
    public async Task Desteklenmiyorken_her_metot_AgentPrismException_firlatir()
    {
        var service = CreateService(withStore: false, withEmbeddings: false);

        await Should.ThrowAsync<AgentPrismException>(
            () => service.IngestAsync("kb", "kaynak", "metin", null).AsTask());
        await Should.ThrowAsync<AgentPrismException>(
            () => service.SearchAsync("kb", "sorgu").AsTask());
        await Should.ThrowAsync<AgentPrismException>(
            () => service.DeleteSourceAsync("kb", "kaynak").AsTask());
        await Should.ThrowAsync<AgentPrismException>(
            () => service.ListSourcesAsync("kb").AsTask());
    }

    [Fact]
    public async Task Metin_verilirse_parcalanir_ve_gomulur()
    {
        var store = new FakeVectorSearchStore();
        var embeddings = new FakeEmbeddingGenerator();
        var service = CreateService(store, embeddings, chunkSize: 5, chunkOverlap: 1);

        var count = await service.IngestAsync("kb", "kaynak", "0123456789", chunks: null);

        count.ShouldBeGreaterThan(1);
        embeddings.Requested.Count.ShouldBe(count);

        var sources = await service.ListSourcesAsync("kb");
        sources.ShouldBe(["kaynak"]);
    }

    [Fact]
    public async Task Hazir_parcalarda_bos_gomu_uretilir_dolu_olan_oldugu_gibi_kalir()
    {
        var store = new FakeVectorSearchStore();
        var embeddings = new FakeEmbeddingGenerator();
        var service = CreateService(store, embeddings);

        var onceden = new float[] { 1, 2, 3 };

        var chunks = new[]
        {
            new VectorChunk { Index = 0, Content = "gomusuz", Embedding = ReadOnlyMemory<float>.Empty },
            new VectorChunk { Index = 1, Content = "gomulu", Embedding = onceden },
        };

        await service.IngestAsync("kb", "kaynak", text: null, chunks);

        // Yalniz gomusuz parca ureticiye gitti.
        embeddings.Requested.ShouldBe(["gomusuz"]);
    }

    [Fact]
    public async Task Hem_metin_hem_parca_verilirse_hata_verir()
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        await Should.ThrowAsync<ArgumentException>(() => service
            .IngestAsync("kb", "kaynak", "metin", [new VectorChunk { Index = 0, Content = "x", Embedding = new float[] { 1, 2, 3 } }])
            .AsTask());
    }

    [Fact]
    public async Task Ne_metin_ne_parca_verilirse_hata_verir()
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        await Should.ThrowAsync<ArgumentException>(() => service.IngestAsync("kb", "kaynak", null, null).AsTask());
    }

    [Fact]
    public async Task Yanlis_boyutlu_hazir_gomu_hata_verir()
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        var chunks = new[] { new VectorChunk { Index = 0, Content = "x", Embedding = new float[] { 1, 2 } } };

        await Should.ThrowAsync<ArgumentException>(() => service.IngestAsync("kb", "kaynak", null, chunks).AsTask());
    }

    [Theory]
    [InlineData("gecersiz koleksiyon")]
    [InlineData("gecersiz/koleksiyon")]
    [InlineData("")]
    public async Task Gecersiz_koleksiyon_adi_reddedilir(string collection)
    {
        var service = CreateService(withStore: true, withEmbeddings: true);

        await Should.ThrowAsync<ArgumentException>(() => service.SearchAsync(collection, "sorgu").AsTask());
    }

    [Fact]
    public async Task Arama_kiraciyi_ve_koleksiyonu_dogru_gecirir()
    {
        var store = new FakeVectorSearchStore();
        var embeddings = new FakeEmbeddingGenerator();
        var service = CreateService(store, embeddings, tenantId: "acme", maxResults: 7);

        await service.SearchAsync("kb", "sorgu");

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
