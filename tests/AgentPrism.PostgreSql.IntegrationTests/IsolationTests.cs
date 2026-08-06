using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;
using AgentPrism.StoreContracts;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Tuketicinin <c>public</c> semasina dokunulmadigini dogrular.
/// </summary>
/// <remarks>Bu bir guvenlik ve guven sinirdir; karar K-013.</remarks>
public sealed class SchemaIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Public_semasi_degismez()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        // Tuketicinin var olan tablosunu taklit ediyoruz.
        await context.ExecuteAsync("CREATE TABLE IF NOT EXISTS public.musteri_siparisleri (id integer PRIMARY KEY);");

        var before = await ReadPublicTablesAsync(context);

        await context.Migrations.ApplyAsync();

        var after = await ReadPublicTablesAsync(context);

        after.ShouldBe(before);
        after.ShouldContain(static table => string.Equals(table, "musteri_siparisleri", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tablolar_yalnizca_agentprism_semasinda_olusur()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var agentPrismTables = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{context.SchemaName}';");

        agentPrismTables.ShouldBeGreaterThan(0);

        var leaked = await context.ScalarAsync<long>(
            """
            SELECT count(*) FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name IN
                ('runs', 'run_events', 'sessions', 'agent_definitions', 'conversations', '__migrations');
            """);

        leaked.ShouldBe(0);
    }

    private static async ValueTask<List<string>> ReadPublicTablesAsync(PostgresTestContext context)
    {
        var tables = new List<string>();

        await using var command = context.DataSource.CreateCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY table_name;");

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
