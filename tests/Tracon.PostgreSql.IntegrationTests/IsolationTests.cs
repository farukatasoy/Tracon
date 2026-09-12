using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// Verifies that the consumer's <c>public</c> schema is left untouched.
/// </summary>
/// <remarks>This is a security and trust boundary; decision K-013.</remarks>
public sealed class SchemaIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Public_schema_is_unchanged()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture, applyMigrations: false);

        // Simulates the consumer's existing table.
        await context.ExecuteAsync("CREATE TABLE IF NOT EXISTS public.customer_orders (id integer PRIMARY KEY);");

        var before = await ReadPublicTablesAsync(context);

        await context.Migrations.ApplyAsync();

        var after = await ReadPublicTablesAsync(context);

        after.ShouldBe(before);
        after.ShouldContain(static table => string.Equals(table, "customer_orders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Tables_are_created_only_in_the_tracon_schema()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var traconTables = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM information_schema.tables WHERE table_schema = '{context.SchemaName}';");

        traconTables.ShouldBeGreaterThan(0);

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
