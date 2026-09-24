using System.Globalization;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The SQLite twin of the PostgreSQL content hash upgrade test.
/// </summary>
/// <remarks>
/// 🚨 SQLite has no <c>ADD COLUMN IF NOT EXISTS</c>; the migration relies on the
/// ledger running it once. Existing grants keep a NULL hash on purpose; see the
/// PostgreSQL test for why.
/// </remarks>
public sealed class SkillScriptGrantContentHashMigrationTests(SqliteFixture fixture)
{
    private const int ContentHashMigrationId = 40;

    private const string Tenant = "default";

    /// <summary>The text shape <c>SqliteDialect.AddTimestamp</c> writes.</summary>
    private const string Seeded = "2026-01-01T00:00:00.0000000Z";

    [Fact]
    public async Task Existing_grants_survive_with_no_content_hash_and_take_one_when_granted_again()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, ContentHashMigrationId - 1);

        await SeedGrantAsync(context, prefix, "wide-skill", scriptName: null, revoked: false);
        await SeedGrantAsync(context, prefix, "narrow-skill", scriptName: "hello", revoked: false);
        await SeedGrantAsync(context, prefix, "revoked-skill", scriptName: null, revoked: true);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await CountAsync(context, prefix, "1 = 1")).ShouldBe(3);
        (await CountAsync(context, prefix, "content_hash IS NOT NULL")).ShouldBe(0, "the migration must not backfill");
        (await context.ScalarAsync<long>(
            $"SELECT \"notnull\" FROM pragma_table_info('{prefix}skill_script_grants') WHERE name = 'content_hash';"))
            .ShouldBe(0);

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

    /// <summary>
    /// Writes a grant the way the store wrote it before the upgrade: an upper-case
    /// identifier (the shape a non-nullable Guid parameter takes) and the dialect's
    /// timestamp text.
    /// </summary>
    private static ValueTask<int> SeedGrantAsync(
        SqliteTestContext context,
        string prefix,
        string skillName,
        string? scriptName,
        bool revoked)
        => context.ExecuteAsync(
            $"INSERT INTO {prefix}skill_script_grants " +
            "(id, tenant_id, skill_name, script_name, granted_by, granted_at, expires_at, revoked_at) " +
            $"VALUES ('{Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant()}', '{Tenant}', '{skillName}', " +
            (scriptName is null ? "NULL" : $"'{scriptName}'") +
            $", 'seed', '{Seeded}', NULL, " + (revoked ? $"'{Seeded}'" : "NULL") + ");");

    private static async Task<long> CountAsync(SqliteTestContext context, string prefix, string predicate)
        => await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}skill_script_grants WHERE {predicate};");
}
