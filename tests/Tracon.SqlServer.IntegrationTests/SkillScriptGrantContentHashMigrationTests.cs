using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The SQL Server twin of the PostgreSQL content hash upgrade test.
/// </summary>
/// <remarks>
/// 🚨 The column is <c>nvarchar(64)</c>: a hash that did not fit would fail the
/// re-grant at run time only, against a real server. Existing grants keep a NULL
/// hash on purpose; see the PostgreSQL test for why.
/// </remarks>
public sealed class SkillScriptGrantContentHashMigrationTests(SqlServerFixture fixture)
{
    private const int ContentHashMigrationId = 41;

    private const string Tenant = "default";

    [Fact]
    public async Task Existing_grants_survive_with_no_content_hash_and_take_one_when_granted_again()
    {
        var schemaName = SqlServerTestContext.NewSchemaName();
        await using var context = SqlServerTestContext.Create(fixture, schemaName);
        var prefix = $"{schemaName}.";

        await context.ExecuteAsync($"IF SCHEMA_ID(N'{schemaName}') IS NULL EXEC(N'CREATE SCHEMA {schemaName}');");

        await ApplyThroughAsync(context, prefix, ContentHashMigrationId - 1);

        await SeedGrantAsync(context, prefix, "wide-skill", scriptName: null, revoked: false);
        await SeedGrantAsync(context, prefix, "narrow-skill", scriptName: "hello", revoked: false);
        await SeedGrantAsync(context, prefix, "revoked-skill", scriptName: null, revoked: true);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, prefix, "1 = 1")).ShouldBe(3);
        (await CountAsync(context, prefix, "content_hash IS NOT NULL")).ShouldBe(0, "the migration must not backfill");
        (await context.ScalarAsync<bool>(
            $"SELECT is_nullable FROM sys.columns WHERE object_id = OBJECT_ID(N'{prefix}skill_script_grants') AND name = N'content_hash';"))
            .ShouldBeTrue();

        var store = context.SkillScriptGrants;
        var grants = await store.ListAsync(Tenant);

        grants.Count.ShouldBe(3);
        grants.Where(static grant => grant.ContentHash is not null).ShouldBeEmpty();
        grants.Where(static grant => !string.Equals(grant.GrantedBy, "seed", StringComparison.Ordinal)).ShouldBeEmpty();
        grants.Single(static grant => string.Equals(grant.SkillName, "narrow-skill", StringComparison.Ordinal)).ScriptName.ShouldBe("hello");
        grants.Single(static grant => string.Equals(grant.SkillName, "revoked-skill", StringComparison.Ordinal)).RevokedAt.ShouldNotBeNull();

        var found = await store.FindActiveAsync(Tenant, "wide-skill", "any-script", DateTimeOffset.UtcNow);
        found.ShouldNotBeNull();
        found.ContentHash.ShouldBeNull();

        var pin = new string('A', 64);
        (await store.GrantAsync(new SkillScriptGrant { TenantId = Tenant, SkillName = "wide-skill", ContentHash = pin, GrantedBy = "admin" }))
            .ContentHash.ShouldBe(pin);
        (await store.FindActiveAsync(Tenant, "wide-skill", "any-script", DateTimeOffset.UtcNow))!.ContentHash.ShouldBe(pin);
        (await CountAsync(context, prefix, "1 = 1")).ShouldBe(3);
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

    private static ValueTask<int> SeedGrantAsync(
        SqlServerTestContext context,
        string prefix,
        string skillName,
        string? scriptName,
        bool revoked)
        => context.ExecuteAsync(
            $"INSERT INTO {prefix}skill_script_grants " +
            "(id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at) " +
            $"VALUES (NEWID(), N'{Tenant}', N'{skillName}', " +
            (scriptName is null ? "NULL" : $"N'{scriptName}'") +
            ", N'seed', SYSDATETIMEOFFSET(), NULL, " + (revoked ? "SYSDATETIMEOFFSET()" : "NULL") + ");");

    private static async Task<long> CountAsync(SqlServerTestContext context, string prefix, string predicate)
        => await context.ScalarAsync<long>($"SELECT CAST(COUNT(*) AS bigint) FROM {prefix}skill_script_grants WHERE {predicate};");
}
