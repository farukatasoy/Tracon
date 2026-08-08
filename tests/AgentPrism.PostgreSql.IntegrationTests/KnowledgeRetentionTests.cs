using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// <c>document_embeddings</c>'in bir saklama hedefi (<see cref="RetentionTargets.DocumentEmbeddings"/>)
/// olarak taninmasi (Faz 51, DoD).
/// </summary>
public sealed class KnowledgeRetentionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private PostgresTestContext _context = null!;

    public async ValueTask InitializeAsync() => _context = await PostgresTestContext.CreateAsync(fixture);

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    [Fact]
    public async Task Eski_parcalar_silinir_yeniler_kalir()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await SeedChunkAsync("eski", cutoff.AddDays(-5));
        await SeedChunkAsync("yeni", cutoff.AddDays(5));

        (await _context.RetentionData.CountOlderThanAsync(RetentionTargets.DocumentEmbeddings, tenantId: null, cutoff))
            .ShouldBe(1);

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.DocumentEmbeddings, tenantId: null, cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        var sources = await _context.Vectors.ListSourcesAsync("default", "kb");
        sources.ShouldBe(["yeni"]);
    }

    [Fact]
    public async Task Kiraci_suzgeciyle_yalniz_o_kiracinin_satirlari_silinir()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);

        await SeedChunkAsync("a-kiracisi", cutoff.AddDays(-5), tenantId: "kiraci-a");
        await SeedChunkAsync("b-kiracisi", cutoff.AddDays(-5), tenantId: "kiraci-b");

        var deleted = await _context.RetentionData.DeleteBatchAsync(
            RetentionTargets.DocumentEmbeddings, tenantId: "kiraci-a", cutoff, batchSize: 100);

        deleted.ShouldBe(1);

        (await _context.Vectors.ListSourcesAsync("kiraci-a", "kb")).ShouldBeEmpty();
        (await _context.Vectors.ListSourcesAsync("kiraci-b", "kb")).ShouldHaveSingleItem();
    }

    private async Task SeedChunkAsync(string sourceId, DateTimeOffset createdAt, string tenantId = "default")
    {
        var schema = _context.SchemaName;

        // metadata sutunu belirtilmez; migration 0024'teki DEFAULT '{}'::jsonb devreye girer.
        await _context.ExecuteAsync($"""
            INSERT INTO {schema}.document_embeddings
                (id, tenant_id, collection, source_id, chunk_index, content, embedding, created_at)
            VALUES
                (gen_random_uuid(), '{tenantId}', 'kb', '{sourceId}', 0, 'icerik',
                 '[1,0,0]'::vector, '{createdAt:O}');
            """);
    }
}
