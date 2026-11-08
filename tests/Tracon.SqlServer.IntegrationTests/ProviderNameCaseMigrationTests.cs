using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The migration that folds a tenant's provider names to lower case is a released file and folds with the rules of the
/// collation of the database it runs in.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written under the schema before the migration and read back after it. Under a Turkish collation
/// (<c>TRACON_TEST_SQLSERVER_COLLATION=Turkish_CI_AS</c> with <c>TRACON_TEST_SQLSERVER_LCID=1055</c>) <c>LOWER(N'OpenAI')</c>
/// ends in a dotless i (U+0131) instead of the i of <c>openai</c>. The file is published and its bytes never change, so the
/// test states what each kind of database does instead of asking for one answer: ASCII folding where the collation is not
/// Turkish, the dotless i where it is. On a supported install the file runs on a table that holds no row, so the difference
/// reaches only a Turkish database that applied the file with a checksum patched by hand; the compatibility page names that
/// limit.
/// </para>
/// <para>
/// Which of the two the database does is read from the database (<c>LOWER(N'I')</c>), not from the environment variable, so
/// the test cannot disagree with the server it runs against. The primary key of the table follows the collation of the
/// database, so two spellings of one provider cannot both be written here; a case-sensitive database that can hold both is
/// not tested, because which of the two rows stays is decided by its sort order and no one answer holds in every collation.
/// </para>
/// </remarks>
public sealed class ProviderNameCaseMigrationTests(SqlServerFixture fixture)
{
    private const int MigrationId = 25;

    [Fact]
    public async Task Mixed_case_provider_names_are_folded_by_the_rules_of_the_collation()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        var prefix = $"{schemaName}.";

        // The capital I is the only letter in these names whose lower case depends on the collation.
        var turkish = await context.ScalarAsync<int>("SELECT CASE WHEN LOWER(N'I') COLLATE Latin1_General_BIN2 = NCHAR(305) THEN 1 ELSE 0 END;") == 1;
        var openai = turkish ? "opena\u0131" : "openai";
        var azure = turkish ? "azureopena\u0131" : "azureopenai";

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
            ("default", openai, "key-openai"),
            ("default", azure, "key-azure"),
            ("default", "anthropic", "key-anthropic"),
            ("acme", openai, "key-acme"),
        })
        {
            (await CountAsync(context, prefix, $"tenant_id = N'{tenant}' AND provider_name COLLATE Latin1_General_BIN2 = N'{name}' AND api_key_configuration_name = N'{key}'"))
                .ShouldBe(1, $"{tenant}/{name} should hold {key}");
        }

        (await CountAsync(context, prefix, "provider_name COLLATE Latin1_General_BIN2 LIKE N'%' + NCHAR(305) + N'%'"))
            .ShouldBe(turkish ? 3 : 0, "a Turkish LOWER() writes the dotless i into the three names that held a capital I, and no other collation does");
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
