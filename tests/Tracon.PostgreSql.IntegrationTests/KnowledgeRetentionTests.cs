using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// <c>document_embeddings</c> is recognized as a retention target
/// (<see cref="RetentionTargets.DocumentEmbeddings"/>) (Phase 51, DoD).
/// </summary>
public sealed class KnowledgeRetentionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Old_chunks_are_deleted_new_ones_remain()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await SeedChunkAsync("old", cutoff.AddDays(-5));
        await SeedChunkAsync("new", cutoff.AddDays(5));

        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.DocumentEmbeddings, tenantId: null, cutoff))
            .ShouldBe(1);

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.DocumentEmbeddings, tenantId: null, cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        var sources = await _context.Vectors.ListSourcesAsync("default", "kb");
        sources.ShouldBe(["new"]);
    }

    [Fact]
    public async Task Tenant_filter_deletes_only_that_tenants_rows()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await SeedChunkAsync("tenant-a-source", cutoff.AddDays(-5), tenantId: "tenant-a");
        await SeedChunkAsync("tenant-b-source", cutoff.AddDays(-5), tenantId: "tenant-b");

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.DocumentEmbeddings, tenantId: "tenant-a", cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        (await _context.Vectors.ListSourcesAsync("tenant-a", "kb")).ShouldBeEmpty();
        (await _context.Vectors.ListSourcesAsync("tenant-b", "kb")).ShouldHaveSingleItem();
    }

    private async Task SeedChunkAsync(string sourceId, DateTimeOffset createdAt, string tenantId = "default")
    {
        var schema = _context.SchemaName;

        // The metadata column is not specified; 0001_vector's DEFAULT '{}'::jsonb kicks in.
        await _context.ExecuteAsync($"""
            INSERT INTO {schema}.document_embeddings
                (id, tenant_id, collection, source_id, chunk_index, content, embedding, created_at)
            VALUES
                (gen_random_uuid(), '{tenantId}', 'kb', '{sourceId}', 0, 'content',
                 '[1,0,0]'::vector, '{createdAt:O}');
            """);
    }
}
