using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The migration that folds a tenant's provider names to lower case is a released file and folds with the rules of the
/// database it runs in.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written under the schema before the migration and read back after it. On a database whose locale is Turkish
/// (<c>TRACON_TEST_POSTGRES_ICU_LOCALE=tr-TR</c>) <c>lower('OpenAI')</c> ends in a dotless i (U+0131) instead of the i of
/// <c>openai</c>. The file is published and its bytes never change, so the test states what each kind of database does
/// instead of asking for one answer: ASCII folding where the locale is not Turkish, the dotless i where it is. On a supported
/// install the file runs on a table that holds no row, so the difference reaches only a Turkish database that applied the file
/// with a checksum patched by hand; the compatibility page names that limit.
/// </para>
/// <para>
/// Which of the two the database does is read from the database (<c>lower('I')</c>), not from the environment variable, so
/// the test cannot disagree with the server it runs against. Two spellings of one provider written at one instant are not
/// tested: which of them stays is decided by the sort order of the database, and no one answer holds in every locale.
/// </para>
/// </remarks>
public sealed class ProviderNameCaseMigrationTests(PostgresFixture fixture)
{
    private const int MigrationId = 38;

    [Fact]
    public async Task Mixed_case_provider_names_are_folded_by_the_rules_of_the_database_and_the_newer_duplicate_stays()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        // The capital I is the only letter in these names whose lower case depends on the locale.
        var turkish = await context.ScalarAsync<bool>("SELECT lower('I') = chr(305);");
        var openai = turkish ? "opena\u0131" : "openai";

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);

        await InsertAsync(context, schemaName, "default", "OpenAI", "key-openai", "2026-01-01");
        await InsertAsync(context, schemaName, "default", "Anthropic", "key-old", "2026-01-01");
        await InsertAsync(context, schemaName, "default", "anthropic", "key-new", "2026-02-01");
        await InsertAsync(context, schemaName, "default", "azure", "key-azure", "2026-01-01");
        await InsertAsync(context, schemaName, "acme", "OpenAI", "key-acme", "2026-01-01");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, schemaName, "tenant_id = 'default'")).ShouldBe(3, "the older of the two spellings of one provider goes");
        (await CountAsync(context, schemaName, "tenant_id = 'acme'")).ShouldBe(1);

        // Compared by their characters ("C"), so a database that folds case or letters in its own way cannot hide a wrong name.
        foreach (var (tenant, name, key) in new[]
        {
            ("default", openai, "key-openai"),
            ("default", "anthropic", "key-new"),
            ("default", "azure", "key-azure"),
            ("acme", openai, "key-acme"),
        })
        {
            (await CountAsync(context, schemaName, $"tenant_id = '{tenant}' AND provider_name COLLATE \"C\" = '{name}' AND api_key_configuration_name = '{key}'"))
                .ShouldBe(1, $"{tenant}/{name} should hold {key}");
        }

        (await CountAsync(context, schemaName, "provider_name COLLATE \"C\" LIKE '%' || chr(305) || '%'"))
            .ShouldBe(turkish ? 2 : 0, "a Turkish lower() writes the dotless i into the two names that held a capital I, and no other database does");
    }

    private static async Task<long> CountAsync(PostgresTestContext context, string schemaName, string predicate)
        => await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.tenant_provider_bindings WHERE {predicate};");

    private static async Task InsertAsync(PostgresTestContext context, string schemaName, string tenant, string provider, string key, string updatedAt)
        => await context.ExecuteAsync(
            $"INSERT INTO {schemaName}.tenant_provider_bindings (tenant_id, provider_name, api_key_configuration_name, updated_at) " +
            $"VALUES ('{tenant}', '{provider}', '{key}', '{updatedAt}T00:00:00Z');");

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
