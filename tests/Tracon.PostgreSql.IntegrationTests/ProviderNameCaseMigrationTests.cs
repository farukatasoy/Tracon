using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The migration that folds a tenant's provider names to lower case does so with the ASCII rules, whatever locale the
/// database has.
/// </summary>
/// <remarks>
/// <para>
/// Rows are written under the schema before the migration and read back after it. On a database whose locale is Turkish
/// (<c>TRACON_TEST_POSTGRES_ICU_LOCALE=tr-TR</c>) <c>lower('OpenAI')</c> ends in a dotless i (U+0131) instead of the i of <c>openai</c>, a name no agent definition
/// names and no egress policy allows; the binding of the tenant then goes unused without an error. Under any other
/// locale this test passes before and after the fix, which is why the migration text is guarded separately by
/// <c>MigrationCaseFoldingTests</c>.
/// </para>
/// </remarks>
public sealed class ProviderNameCaseMigrationTests(PostgresFixture fixture)
{
    private const int MigrationId = 38;

    [Fact]
    public async Task Mixed_case_provider_names_are_folded_to_ascii_lower_case_and_the_newer_duplicate_stays()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

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
            ("default", "openai", "key-openai"),
            ("default", "anthropic", "key-new"),
            ("default", "azure", "key-azure"),
            ("acme", "openai", "key-acme"),
        })
        {
            (await CountAsync(context, schemaName, $"tenant_id = '{tenant}' AND provider_name COLLATE \"C\" = '{name}' AND api_key_configuration_name = '{key}'"))
                .ShouldBe(1, $"{tenant}/{name} should hold {key}");
        }

        (await CountAsync(context, schemaName, "provider_name COLLATE \"C\" LIKE '%' || chr(305) || '%'"))
            .ShouldBe(0, "no name may contain the dotless i a Turkish lower() writes");
    }

    /// <summary>
    /// Two spellings of one provider written at the same instant: the survivor is the one whose name sorts last by its
    /// characters, whatever the sort order of the database.
    /// </summary>
    /// <remarks>
    /// The migration keeps the row with the later <c>updated_at</c>, and for equal timestamps the one whose name is larger.
    /// Compared with the database's own rules, "larger" is a locale's opinion: <c>openai</c> sorts before <c>OpenAI</c> in
    /// en_US and after it in "C", so the same pair lost a different row on a database set up differently, and the binding
    /// of the tenant held another credential. By the characters, <c>openai</c> (0x6F) is larger than <c>OpenAI</c> (0x4F)
    /// everywhere. The image's default locale is not "C", so this fails there when the comparison is left to the database.
    /// </remarks>
    [Fact]
    public async Task Two_spellings_written_at_the_same_instant_leave_the_row_that_sorts_last_by_its_characters()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);

        await InsertAsync(context, schemaName, "default", "OpenAI", "key-upper", "2026-01-01");
        await InsertAsync(context, schemaName, "default", "openai", "key-lower", "2026-01-01");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, schemaName, "tenant_id = 'default'")).ShouldBe(1);
        (await CountAsync(context, schemaName, "provider_name COLLATE \"C\" = 'openai' AND api_key_configuration_name = 'key-lower'"))
            .ShouldBe(1, "'openai' is larger than 'OpenAI' by its characters, so its row is the one that stays");
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
