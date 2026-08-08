using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <see cref="PgVectorSearchStore"/>'un sozlesmesi: siralama, mesafe suzgeci,
/// yeniden yazma, silme, listeleme, boyut uyusmazligi ve kiraci yalitimi
/// (Faz 51, Is B).
/// </summary>
public sealed class PgVectorSearchStoreTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext? _context;

    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Dimensions_ayardan_gelir()
    {
        _context!.Vectors.Dimensions.ShouldBe(PostgresTestContext.DefaultVectorDimensions);
    }

    [Fact]
    public async Task Arama_mesafeye_gore_artan_sirali_doner()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "yakin", Chunks(("icerik-yakin", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "uzak", Chunks(("icerik-uzak", [0f, 1f, 0f])));

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
            Top = 5,
        });

        results.Count.ShouldBe(2);
        results[0].SourceId.ShouldBe("yakin");
        results[1].SourceId.ShouldBe("uzak");
        results[0].Distance.ShouldBeLessThan(results[1].Distance);
    }

    [Fact]
    public async Task MaxDistance_uzak_sonuclari_eler()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "yakin", Chunks(("a", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "uzak", Chunks(("b", [0f, 1f, 0f])));

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
            Top = 5,
            MaxDistance = 0.01,
        });

        results.ShouldHaveSingleItem().SourceId.ShouldBe("yakin");
    }

    [Fact]
    public async Task Ayni_kaynak_yeniden_yazilinca_eski_parcalar_silinir()
    {
        await _context!.Vectors.UpsertAsync(
            "default", "kb", "belge",
            Chunks(("parca-0", [1f, 0f, 0f]), ("parca-1", [0f, 1f, 0f])));

        await _context.Vectors.UpsertAsync("default", "kb", "belge", Chunks(("yeni-parca", [0f, 0f, 1f])));

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 0f, 0f, 1f },
            Top = 10,
        });

        var hit = results.ShouldHaveSingleItem();
        hit.SourceId.ShouldBe("belge");
        hit.Content.ShouldBe("yeni-parca");
    }

    [Fact]
    public async Task DeleteSourceAsync_yalniz_o_kaynagi_siler()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "silinecek", Chunks(("a", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "kalacak", Chunks(("b", [0f, 1f, 0f])));

        var deleted = await _context.Vectors.DeleteSourceAsync("default", "kb", "silinecek");
        deleted.ShouldBe(1);

        var sources = await _context.Vectors.ListSourcesAsync("default", "kb");
        sources.ShouldBe(["kalacak"]);
    }

    [Fact]
    public async Task ListSourcesAsync_koleksiyondaki_kaynaklari_alfabetik_dondurur()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "zeta", Chunks(("a", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "alfa", Chunks(("b", [0f, 1f, 0f])));

        var sources = await _context.Vectors.ListSourcesAsync("default", "kb");
        sources.ShouldBe(["alfa", "zeta"]);
    }

    [Fact]
    public async Task Yanlis_boyutlu_gomu_UpsertAsync_hata_verir()
    {
        var chunks = new[]
        {
            new VectorChunk { Index = 0, Content = "a", Embedding = new float[] { 0.1f, 0.2f } },
        };

        await Should.ThrowAsync<ArgumentException>(
            () => _context!.Vectors.UpsertAsync("default", "kb", "x", chunks).AsTask());
    }

    [Fact]
    public async Task Metadata_gidip_gelir()
    {
        var chunks = new[]
        {
            new VectorChunk
            {
                Index = 0,
                Content = "icerik",
                Embedding = new float[] { 1f, 0f, 0f },
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["kaynak-turu"] = "pdf" },
            },
        };

        await _context!.Vectors.UpsertAsync("default", "kb", "belge", chunks);

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
        });

        results.ShouldHaveSingleItem().Metadata.ShouldNotBeNull()["kaynak-turu"].ShouldBe("pdf");
    }

    [Fact]
    public async Task Kiraci_birbirinin_koleksiyonunu_gormez()
    {
        await _context!.Vectors.UpsertAsync("kiraci-a", "kb", "gizli", Chunks(("A kiracisinin verisi", [1f, 0f, 0f])));

        var asTenantB = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "kiraci-b",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
        });

        asTenantB.ShouldBeEmpty();

        var asTenantA = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "kiraci-a",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
        });

        asTenantA.ShouldHaveSingleItem();

        var sourcesForB = await _context.Vectors.ListSourcesAsync("kiraci-b", "kb");
        sourcesForB.ShouldBeEmpty();

        (await _context.Vectors.DeleteSourceAsync("kiraci-b", "kb", "gizli")).ShouldBe(0);
        (await _context.Vectors.ListSourcesAsync("kiraci-a", "kb")).ShouldHaveSingleItem();
    }

    private static VectorChunk[] Chunks(params (string Content, float[] Embedding)[] items)
    {
        var result = new VectorChunk[items.Length];

        for (var i = 0; i < items.Length; i++)
        {
            result[i] = new VectorChunk { Index = i, Content = items[i].Content, Embedding = items[i].Embedding };
        }

        return result;
    }
}
