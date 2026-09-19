using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The migration runner refuses a database that holds a non-canonical
/// <c>tenant_id</c> (phase 179).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The guard <strong>never folds the rows itself</strong>. On a
/// case-sensitive engine <c>Acme</c> and <c>acme</c> may be two real tenants,
/// and merging two tenants cannot be undone. Naming the tables and stopping
/// leaves the decision with the only party that knows the answer.
/// </para>
/// <para>
/// SQLite carries these cases because it needs no container. The same guard
/// runs on all three engines through <c>SqlDialect.TenantIdTableCatalogSql</c>
/// and <c>NonCanonicalTenantIdPredicate</c>; the SQL Server override of the
/// predicate is the one that must not be lost — see the 🚨 there.
/// </para>
/// </remarks>
public sealed class TenantIdCaseGuardTests(SqliteFixture fixture)
{
    [Fact]
    public async Task A_clean_database_passes()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('acme', '["openai"]', '2026-09-19T00:00:00Z');
            """);

        // The guard runs on every ApplyAsync, so a second pass over a clean
        // database must stay silent. A guard that cried wolf here would be
        // switched off within a week.
        await Should.NotThrowAsync(async () => await context.Migrations.ApplyAsync());
    }

    [Fact]
    public async Task A_non_canonical_row_stops_the_runner_and_names_the_table()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('Acme', '["openai"]', '2026-09-19T00:00:00Z');
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
        failure.Message.ShouldContain("canonical");

        // The guard reports; it does not repair.
        var stillThere = await context.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM {context.TablePrefix}tenant_egress_policies WHERE tenant_id = 'Acme';
            """);

        stillThere.ShouldBe(1);
    }

    [Fact]
    public async Task Every_table_carrying_the_column_is_inspected()
    {
        // 🚨 The table list comes from the catalog, never from a list in the
        // source. A hand-written list goes stale the first time a table is
        // added, and the guard then passes over the very table that broke.
        // This case proves the discovery by planting the row in a table the
        // guard was never told about by name.
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}api_keys
                (id, tenant_id, name, key_hash, key_prefix, scopes, created_at)
            VALUES ('k1', 'Acme', 'n', x'00', 'ap_test', '[]', '2026-09-19T00:00:00Z');
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("api_keys");
    }
}
