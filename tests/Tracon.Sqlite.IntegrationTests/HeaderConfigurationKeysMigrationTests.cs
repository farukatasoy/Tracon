using System.Globalization;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Rows written before phase 190's <c>header_configuration_keys</c> column
/// read back with an empty map, keep their plain headers, and take a map on
/// the next save.
/// </summary>
/// <remarks>
/// Both readers address the row by ordinal and the column is appended last;
/// a migration that left existing rows NULL, or a default the reader cannot
/// parse, would break every read of the table.
/// 🚨 SQLite has no <c>ADD COLUMN IF NOT EXISTS</c>, and a <c>NOT NULL</c>
/// column with a <c>CHECK</c> is added to a table that already has rows: this
/// measures that SQLite accepts both and fills the default.
/// </remarks>
public sealed class HeaderConfigurationKeysMigrationTests(SqliteFixture fixture)
{
    private const int MigrationId = 41;

    private const string Tenant = "default";

    /// <summary>The text shape <c>SqliteDialect.AddTimestamp</c> writes.</summary>
    private const string Seeded = "2026-01-01T00:00:00.0000000Z";

    [Fact]
    public async Task Existing_rows_read_an_empty_map_and_take_one_on_the_next_save()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        await context.ExecuteAsync(
            $"INSERT INTO {prefix}mcp_servers (id, tenant_id, name, endpoint, headers, created_at, updated_at) " +
            $"VALUES ('{NewId()}', '{Tenant}', 'm1', 'https://mcp.example.com/mcp', '{{\"X-Team\":\"t1\"}}', '{Seeded}', '{Seeded}');");
        await context.ExecuteAsync(
            $"INSERT INTO {prefix}webhook_subscriptions (id, tenant_id, name, url, events, headers, created_at, updated_at) " +
            $"VALUES ('{NewId()}', '{Tenant}', 'w1', 'https://example.com/hook', '[\"run.completed\"]', '{{\"X-Team\":\"t1\"}}', '{Seeded}', '{Seeded}');");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var table in new[] { "mcp_servers", "webhook_subscriptions" })
        {
            (await context.ScalarAsync<long>(
                $"SELECT \"notnull\" FROM pragma_table_info('{prefix}{table}') WHERE name = 'header_configuration_keys';"))
                .ShouldBe(1);
        }

        var server = await context.McpServers.GetAsync(Tenant, "m1");
        server.ShouldNotBeNull().HeaderConfigurationKeys.ShouldBeEmpty();
        server.Headers["X-Team"].ShouldBe("t1");

        var subscription = await context.Webhooks.GetSubscriptionAsync(Tenant, "w1");
        subscription.ShouldNotBeNull().HeaderConfigurationKeys.ShouldBeEmpty();
        subscription.Headers["X-Team"].ShouldBe("t1");

        var saved = await context.McpServers.SaveAsync(server with
        {
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Api-Key"] = "Tracon:McpSecrets:SearchKey",
            },
        });
        saved.HeaderConfigurationKeys["X-Api-Key"].ShouldBe("Tracon:McpSecrets:SearchKey");

        var savedSubscription = await context.Webhooks.SaveSubscriptionAsync(subscription with
        {
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Api-Key"] = "Tracon:WebhookSecrets:OrdersKey",
            },
        });
        savedSubscription.HeaderConfigurationKeys["X-Api-Key"].ShouldBe("Tracon:WebhookSecrets:OrdersKey");
        savedSubscription.Headers["X-Team"].ShouldBe("t1");
    }

    /// <summary>An identifier in the shape a non-nullable Guid parameter takes.</summary>
    private static string NewId() => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    private static async Task ApplyThroughAsync(SqliteTestContext context, string prefix, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty();

        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '{Seeded}');");
        }
    }
}
