using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The migration that folds a tenant's provider names to lower case does so with the ASCII rules, whatever collation the
/// database has.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written under the schema before the migration and read back after it. Under a Turkish collation
/// (<c>TRACON_TEST_SQLSERVER_COLLATION=Turkish_CI_AS</c> with <c>TRACON_TEST_SQLSERVER_LCID=1055</c>) <c>LOWER(N'OpenAI')</c>
/// is <c>openaı</c>, which no agent definition names and no egress policy allows. Under any other collation this test
/// passes before and after the fix, which is why the migration text is guarded separately by
/// <c>MigrationCaseFoldingTests</c>.
/// </para>
/// <para>
/// The primary key of the table follows the collation of the database, so two spellings of one provider cannot both be
/// written here (they can on PostgreSQL); the duplicate path of the migration is covered on that side.
/// </para>
/// </remarks>
public sealed class ProviderNameCaseMigrationTests(SqlServerFixture fixture)
{
    private const int MigrationId = 25;

    [Fact]
    public async Task Mixed_case_provider_names_are_folded_to_ascii_lower_case()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        var prefix = $"{schemaName}.";

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        await InsertAsync(context, prefix, "default", "OpenAI", "key-openai");
        await InsertAsync(context, prefix, "default", "AZUREOPENAI", "key-azure");
        await InsertAsync(context, prefix, "default", "anthropic", "key-anthropic");
        await InsertAsync(context, prefix, "acme", "OpenAI", "key-acme");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, prefix, "tenant_id = N'default'")).ShouldBe(3);
        (await CountAsync(context, prefix, "tenant_id = N'acme'")).ShouldBe(1);

        // Compared by their characters (BIN2), so a collation that folds case or letters in its own way cannot hide a wrong name.
        foreach (var (tenant, name, key) in new[]
        {
            ("default", "openai", "key-openai"),
            ("default", "azureopenai", "key-azure"),
            ("default", "anthropic", "key-anthropic"),
            ("acme", "openai", "key-acme"),
        })
        {
            (await CountAsync(context, prefix, $"tenant_id = N'{tenant}' AND provider_name COLLATE Latin1_General_BIN2 = N'{name}' AND api_key_configuration_name = N'{key}'"))
                .ShouldBe(1, $"{tenant}/{name} should hold {key}");
        }

        (await CountAsync(context, prefix, "provider_name COLLATE Latin1_General_BIN2 LIKE N'%' + NCHAR(305) + N'%'"))
            .ShouldBe(0, "no name may contain the dotless i a Turkish LOWER() writes");
    }

    private static async Task<int> CountAsync(SqlServerTestContext context, string prefix, string predicate)
        => await context.ScalarAsync<int>($"SELECT COUNT(*) FROM {prefix}tenant_provider_bindings WHERE {predicate};");

    private static async Task InsertAsync(SqlServerTestContext context, string prefix, string tenant, string provider, string key)
        => await context.ExecuteRetryingDeadlockAsync(
            $"INSERT INTO {prefix}tenant_provider_bindings (tenant_id, provider_name, api_key_configuration_name, updated_at) " +
            $"VALUES (N'{tenant}', N'{provider}', N'{key}', SYSDATETIMEOFFSET());");

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
            await context.ExecuteRetryingDeadlockAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteRetryingDeadlockAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', SYSDATETIMEOFFSET());");
        }
    }
}
