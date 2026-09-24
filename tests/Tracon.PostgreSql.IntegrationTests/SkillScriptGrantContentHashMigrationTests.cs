using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The content hash column on script grants, applied over grants that already exist.
/// </summary>
/// <remarks>
/// 🚨 A grant written before the column existed must come out of the upgrade intact
/// and with NO hash. A missing hash refuses every stored script, which is the
/// intent: a backfill with today's hash would approve content that may have changed
/// since the grant was given. No compiler and no SQL text snapshot sees migration
/// SQL, so the upgrade runs against rows written under the old schema.
/// </remarks>
public sealed class SkillScriptGrantContentHashMigrationTests(PostgresFixture fixture)
{
    /// <summary>The migration under test. Everything before it defines the pre-upgrade schema.</summary>
    private const int ContentHashMigrationId = 53;

    private const string Tenant = "default";

    [Fact]
    public async Task Existing_grants_survive_with_no_content_hash_and_take_one_when_granted_again()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        await ApplyThroughAsync(context, schemaName, ContentHashMigrationId - 1);

        // Rows written under the OLD shape, one of each kind a database can hold.
        await SeedGrantAsync(context, schemaName, "wide-skill", scriptName: null, revoked: false);
        await SeedGrantAsync(context, schemaName, "narrow-skill", scriptName: "hello", revoked: false);
        await SeedGrantAsync(context, schemaName, "revoked-skill", scriptName: null, revoked: true);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, schemaName, "TRUE")).ShouldBe(3);
        (await CountAsync(context, schemaName, "content_hash IS NOT NULL")).ShouldBe(0, "the migration must not backfill");
        (await context.ScalarAsync<string>(
            "SELECT is_nullable FROM information_schema.columns " +
            $"WHERE table_schema = '{schemaName}' AND table_name = 'skill_script_grants' AND column_name = 'content_hash';"))
            .ShouldBe("YES");

        var store = context.SkillScriptGrants;
        var grants = await store.ListAsync(Tenant);

        grants.Count.ShouldBe(3);
        grants.Where(static grant => grant.ContentHash is not null).ShouldBeEmpty();
        grants.Where(static grant => !string.Equals(grant.GrantedBy, "seed", StringComparison.Ordinal)).ShouldBeEmpty();
        grants.Single(static grant => string.Equals(grant.SkillName, "narrow-skill", StringComparison.Ordinal)).ScriptName.ShouldBe("hello");
        grants.Single(static grant => string.Equals(grant.SkillName, "revoked-skill", StringComparison.Ordinal)).RevokedAt.ShouldNotBeNull();

        // The runner still finds the migrated grant, and finds no pin on it.
        var found = await store.FindActiveAsync(Tenant, "wide-skill", "any-script", DateTimeOffset.UtcNow);
        found.ShouldNotBeNull();
        found.ContentHash.ShouldBeNull();

        // Granting again over the migrated row writes the pin through the update
        // branch of the upsert, and no second row appears.
        var pin = new string('A', 64);
        (await store.GrantAsync(new SkillScriptGrant { TenantId = Tenant, SkillName = "wide-skill", ContentHash = pin, GrantedBy = "admin" }))
            .ContentHash.ShouldBe(pin);
        (await store.FindActiveAsync(Tenant, "wide-skill", "any-script", DateTimeOffset.UtcNow))!.ContentHash.ShouldBe(pin);
        (await CountAsync(context, schemaName, "TRUE")).ShouldBe(3);
    }

    /// <summary>Runs every migration up to <paramref name="lastId"/> and records it as applied.</summary>
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

    private static ValueTask<int> SeedGrantAsync(
        PostgresTestContext context,
        string schemaName,
        string skillName,
        string? scriptName,
        bool revoked)
        => context.ExecuteAsync(
            $"INSERT INTO {schemaName}.skill_script_grants " +
            "(id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at) " +
            $"VALUES (gen_random_uuid(), '{Tenant}', '{skillName}', " +
            (scriptName is null ? "NULL" : $"'{scriptName}'") +
            ", 'seed', now(), NULL, " + (revoked ? "now()" : "NULL") + ");");

    private static async Task<long> CountAsync(PostgresTestContext context, string schemaName, string predicate)
        => await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {schemaName}.skill_script_grants WHERE {predicate};");
}
