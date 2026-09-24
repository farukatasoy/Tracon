using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

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
public sealed class HeaderConfigurationKeysMigrationTests(PostgresFixture fixture)
{
    private const int MigrationId = 54;

    private const string Tenant = "default";

    [Fact]
    public async Task Existing_rows_read_an_empty_map_and_take_one_on_the_next_save()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);

        await context.ExecuteAsync(
            $"INSERT INTO {schemaName}.mcp_servers (id, tenant_id, name, endpoint, headers, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{Tenant}', 'm1', 'https://mcp.example.com/mcp', '{{\"X-Team\":\"t1\"}}', now(), now());");
        await context.ExecuteAsync(
            $"INSERT INTO {schemaName}.webhook_subscriptions (id, tenant_id, name, url, events, headers, created_at, updated_at) " +
            $"VALUES (gen_random_uuid(), '{Tenant}', 'w1', 'https://example.com/hook', ARRAY['run.completed'], '{{\"X-Team\":\"t1\"}}', now(), now());");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var table in new[] { "mcp_servers", "webhook_subscriptions" })
        {
            (await context.ScalarAsync<string>(
                "SELECT is_nullable FROM information_schema.columns " +
                $"WHERE table_schema = '{schemaName}' AND table_name = '{table}' AND column_name = 'header_configuration_keys';"))
                .ShouldBe("NO");
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

    private static async Task ApplyThroughAsync(PostgresTestContext context, string schemaName, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync($"CREATE SCHEMA {schemaName};");
        await context.ExecuteAsync($"""
            CREATE TABLE {schemaName}.__migrations (
                id         integer     NOT NULL,
                set_name   text        NOT NULL,
                name       text        NOT NULL,
                checksum   text        NOT NULL,
                applied_at timestamptz NOT NULL,
                PRIMARY KEY (set_name, id)
            );
            """);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync($"""
                INSERT INTO {schemaName}.__migrations (id, set_name, name, checksum, applied_at)
                VALUES ({migration.Id}, 'core', '{migration.Name}', '{migration.Checksum}', now());
                """);
        }
    }
}
