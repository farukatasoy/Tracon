using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The <c>authorization_configuration_key</c> column moves into the
/// <c>header_configuration_keys</c> map and is dropped, with the rule the
/// connection code applied before: OAuth rows and rows whose map already names
/// <c>Authorization</c> keep their map, an empty key moves nothing.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written under the PRE-upgrade schema (every migration before this
/// one) and read back through the real store after the upgrade, so the test also
/// proves the reader still finds every column once an earlier one is gone.
/// </para>
/// <para>
/// 🚨 T-SQL compiles a batch up front and OPENJSON's <c>key</c> column has a
/// binary collation: the compile order, the case-insensitive match and the
/// string escaping of <c>JSON_MODIFY</c> are measured here, not assumed.
/// </para>
/// </remarks>
public sealed class RemoveAuthorizationConfigurationKeyMigrationTests(SqlServerFixture fixture)
{
    private const int MigrationId = 50;

    private const string Tenant = "default";

    private const string Bearer = "Tracon:McpSecrets:Bearer";

    [Fact]
    public async Task Stored_keys_move_into_the_map_by_the_rule_the_connection_applied_and_the_column_is_dropped()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        var prefix = $"{schemaName}.";

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        await InsertAsync(context, prefix, Tenant, "moves", key: Bearer, map: "{}");
        await InsertAsync(context, prefix, Tenant, "keeps-the-other-keys", key: Bearer, map: """{"X-Api-Key":"Tracon:McpSecrets:Search","Cookie":"Tracon:McpSecrets:Session"}""");
        await InsertAsync(context, prefix, Tenant, "map-wins-lower", key: "Tracon:McpSecrets:Legacy", map: """{"authorization":"Tracon:McpSecrets:Map"}""");
        await InsertAsync(context, prefix, Tenant, "map-wins-upper", key: "Tracon:McpSecrets:Legacy", map: """{"AUTHORIZATION":"Tracon:McpSecrets:Map"}""");
        await InsertAsync(context, prefix, Tenant, "oauth", key: Bearer, map: "{}", oauth: true);
        await InsertAsync(context, prefix, Tenant, "empty", key: string.Empty, map: "{}");
        await InsertAsync(context, prefix, Tenant, "null", key: null, map: "{}");
        await InsertAsync(context, prefix, Tenant, "escaped", key: "Tracon:McpSecrets:quo\"te\\path", map: "{}");
        await InsertAsync(context, prefix, "acme", "moves", key: "Tracon:McpSecrets:acme:Bearer", map: "{}");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'{prefix}mcp_servers') AND name = N'authorization_configuration_key';"))
            .ShouldBe(0);

        (await Map(context, Tenant, "moves")).ShouldBe(Expect(("Authorization", Bearer)));
        (await Map(context, Tenant, "keeps-the-other-keys")).ShouldBe(Expect(
            ("X-Api-Key", "Tracon:McpSecrets:Search"),
            ("Cookie", "Tracon:McpSecrets:Session"),
            ("Authorization", Bearer)));
        (await Map(context, Tenant, "map-wins-lower")).ShouldBe(Expect(("authorization", "Tracon:McpSecrets:Map")));
        (await Map(context, Tenant, "map-wins-upper")).ShouldBe(Expect(("AUTHORIZATION", "Tracon:McpSecrets:Map")));
        (await Map(context, Tenant, "oauth")).ShouldBeEmpty();
        (await Map(context, Tenant, "empty")).ShouldBeEmpty();
        (await Map(context, Tenant, "null")).ShouldBeEmpty();
        (await Map(context, Tenant, "escaped")).ShouldBe(Expect(("Authorization", "Tracon:McpSecrets:quo\"te\\path")));

        // A row is moved inside its own tenant; the same name elsewhere is a different record.
        (await Map(context, "acme", "moves")).ShouldBe(Expect(("Authorization", "Tracon:McpSecrets:acme:Bearer")));

        // The reader still finds every other column once an earlier one is gone.
        var oauth = await context.McpServers.GetAsync(Tenant, "oauth");

        oauth.ShouldNotBeNull().OAuthEnabled.ShouldBeTrue();
        oauth.Endpoint.ToString().ShouldBe("https://mcp.example.com/mcp");
    }

    // Sorted: a provider is free to store a JSON object's members in any order
    // (jsonb sorts them by length), and the order is not part of the contract.
    private static SortedDictionary<string, string> Expect(params (string Name, string Key)[] entries)
        => new(entries.ToDictionary(static entry => entry.Name, static entry => entry.Key, StringComparer.Ordinal), StringComparer.Ordinal);

    private static async Task<SortedDictionary<string, string>> Map(SqlServerTestContext context, string tenant, string name)
    {
        var server = await context.McpServers.GetAsync(tenant, name);

        return new SortedDictionary<string, string>(
            server.ShouldNotBeNull().HeaderConfigurationKeys.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static async Task InsertAsync(
        SqlServerTestContext context,
        string prefix,
        string tenant,
        string name,
        string? key,
        string map,
        bool oauth = false)
    {
        var keyLiteral = key is null ? "NULL" : $"N'{key.Replace("'", "''", StringComparison.Ordinal)}'";
        var oauthLiteral = oauth ? 1 : 0;

        await context.ExecuteAsync(
            $"INSERT INTO {prefix}mcp_servers (id, tenant_id, name, endpoint, authorization_configuration_key, header_configuration_keys, oauth_enabled, created_at, updated_at) " +
            $"VALUES (NEWID(), N'{tenant}', N'{name}', N'https://mcp.example.com/mcp', {keyLiteral}, N'{map}', {oauthLiteral}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());");
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
