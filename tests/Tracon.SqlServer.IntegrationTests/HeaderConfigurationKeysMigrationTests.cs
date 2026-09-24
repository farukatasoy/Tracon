using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Rows written before phase 190's <c>header_configuration_keys</c> column
/// read back with an empty map, keep their plain headers, and take a map on
/// the next save.
/// </summary>
/// <remarks>
/// Both readers address the row by ordinal and the column is appended last;
/// a migration that left existing rows NULL, or a default the reader cannot
/// parse, would break every read of the table.
/// </remarks>
public sealed class HeaderConfigurationKeysMigrationTests(SqlServerFixture fixture)
{
    private const int MigrationId = 42;

    private const string Tenant = "default";

    [Fact]
    public async Task Existing_rows_read_an_empty_map_and_take_one_on_the_next_save()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        var prefix = $"{schemaName}.";

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        await context.ExecuteAsync(
            $"INSERT INTO {prefix}mcp_servers (id, tenant_id, name, endpoint, headers, created_at, updated_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'm1', N'https://mcp.example.com/mcp', N'{{\"X-Team\":\"t1\"}}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());");
        await context.ExecuteAsync(
            $"INSERT INTO {prefix}webhook_subscriptions (id, tenant_id, name, url, events, headers, created_at, updated_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'w1', N'https://example.com/hook', N'[\"run.completed\"]', N'{{\"X-Team\":\"t1\"}}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var table in new[] { "mcp_servers", "webhook_subscriptions" })
        {
            (await context.ScalarAsync<bool>(
                $"SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'{prefix}{table}') AND name = N'header_configuration_keys';"))
                .ShouldBeFalse();
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

    private static async Task ApplyThroughAsync(SqlServerTestContext context, string prefix, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations.")
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
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', SYSDATETIMEOFFSET());");
        }
    }
}
