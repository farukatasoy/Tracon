using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The contract of <see cref="PgVectorSearchStore"/>: ordering, distance
/// filtering, rewriting, deletion, listing, dimension mismatch, and tenant
/// isolation (Phase 51, Job B).
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
    public async Task Dimensions_comes_from_settings()
    {
        _context!.Vectors.Dimensions.ShouldBe(PostgresTestContext.DefaultVectorDimensions);
    }

    [Fact]
    public async Task Search_returns_results_sorted_by_ascending_distance()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "near", Chunks(("content-near", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "far", Chunks(("content-far", [0f, 1f, 0f])));

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
            Top = 5,
        });

        results.Count.ShouldBe(2);
        results[0].SourceId.ShouldBe("near");
        results[1].SourceId.ShouldBe("far");
        results[0].Distance.ShouldBeLessThan(results[1].Distance);
    }

    [Fact]
    public async Task MaxDistance_filters_out_far_results()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "near", Chunks(("a", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "far", Chunks(("b", [0f, 1f, 0f])));

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
            Top = 5,
            MaxDistance = 0.01,
        });

        results.ShouldHaveSingleItem().SourceId.ShouldBe("near");
    }

    [Fact]
    public async Task Same_source_rewritten_deletes_old_chunks()
    {
        await _context!.Vectors.UpsertAsync(
            "default", "kb", "document",
            Chunks(("chunk-0", [1f, 0f, 0f]), ("chunk-1", [0f, 1f, 0f])));

        await _context.Vectors.UpsertAsync("default", "kb", "document", Chunks(("new-chunk", [0f, 0f, 1f])));

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 0f, 0f, 1f },
            Top = 10,
        });

        var hit = results.ShouldHaveSingleItem();
        hit.SourceId.ShouldBe("document");
        hit.Content.ShouldBe("new-chunk");
    }

    [Fact]
    public async Task DeleteSourceAsync_deletes_only_that_source()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "to-delete", Chunks(("a", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "to-keep", Chunks(("b", [0f, 1f, 0f])));

        var deleted = await _context.Vectors.DeleteSourceAsync("default", "kb", "to-delete");
        deleted.ShouldBe(1);

        var sources = await _context.Vectors.ListSourcesAsync("default", "kb");
        sources.ShouldBe(["to-keep"]);
    }

    [Fact]
    public async Task ListSourcesAsync_returns_sources_in_the_collection_alphabetically()
    {
        await _context!.Vectors.UpsertAsync("default", "kb", "zeta", Chunks(("a", [1f, 0f, 0f])));
        await _context.Vectors.UpsertAsync("default", "kb", "alpha", Chunks(("b", [0f, 1f, 0f])));

        var sources = await _context.Vectors.ListSourcesAsync("default", "kb");
        sources.ShouldBe(["alpha", "zeta"]);
    }

    [Fact]
    public async Task Wrong_dimension_embedding_UpsertAsync_throws()
    {
        var chunks = new[]
        {
            new VectorChunk { Index = 0, Content = "a", Embedding = new float[] { 0.1f, 0.2f } },
        };

        await Should.ThrowAsync<ArgumentException>(
            () => _context!.Vectors.UpsertAsync("default", "kb", "x", chunks).AsTask());
    }

    [Fact]
    public async Task Metadata_round_trips()
    {
        var chunks = new[]
        {
            new VectorChunk
            {
                Index = 0,
                Content = "content",
                Embedding = new float[] { 1f, 0f, 0f },
                Metadata = new Dictionary<string, string>(StringComparer.Ordinal) { ["source-type"] = "pdf" },
            },
        };

        await _context!.Vectors.UpsertAsync("default", "kb", "document", chunks);

        var results = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "default",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
        });

        results.ShouldHaveSingleItem().Metadata.ShouldNotBeNull()["source-type"].ShouldBe("pdf");
    }

    [Fact]
    public async Task Tenant_does_not_see_the_others_collection()
    {
        await _context!.Vectors.UpsertAsync("tenant-a", "kb", "secret", Chunks(("tenant A's data", [1f, 0f, 0f])));

        var asTenantB = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "tenant-b",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
        });

        asTenantB.ShouldBeEmpty();

        var asTenantA = await _context.Vectors.SearchAsync(new VectorSearchRequest
        {
            TenantId = "tenant-a",
            Collection = "kb",
            QueryEmbedding = new float[] { 1f, 0f, 0f },
        });

        asTenantA.ShouldHaveSingleItem();

        var sourcesForB = await _context.Vectors.ListSourcesAsync("tenant-b", "kb");
        sourcesForB.ShouldBeEmpty();

        (await _context.Vectors.DeleteSourceAsync("tenant-b", "kb", "secret")).ShouldBe(0);
        (await _context.Vectors.ListSourcesAsync("tenant-a", "kb")).ShouldHaveSingleItem();
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
