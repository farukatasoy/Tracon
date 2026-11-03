using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The migration runner refuses a database that holds a non-canonical
/// <c>tenant_id</c>.
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
        failure.Message.ShouldNotContain("audit trail", Case.Insensitive);

        // The guard reports; it does not repair.
        var stillThere = await context.ScalarAsync<long>($"""
            SELECT COUNT(*) FROM {context.TablePrefix}tenant_egress_policies WHERE tenant_id = 'Acme';
            """);

        stillThere.ShouldBe(1);
    }

    [Fact]
    public async Task An_audit_entry_outside_the_allowed_set_is_not_offered_a_rename()
    {
        // The tenant is part of each entry's hash: a renamed entry fails the chain verification, so the message
        // must not send the operator down that road without saying so.
        const string tenant = "caf\u00E9";

        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}audit_log (id, tenant_id, actor, action, entity, created_at)
            VALUES ('a1', '{tenant}', 'admin', 'agent.create', 'agent:demo', '2026-09-19T00:00:00Z');
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("audit_log");
        failure.Message.ShouldContain("do not rename those rows");
        failure.Message.ShouldContain("fails its verification");
    }

    [Theory]
    [InlineData("caf\u00E9")]
    [InlineData("cafe\u0301")]
    [InlineData("\uFF41cme")]
    [InlineData("ac me")]
    [InlineData("acme:ops")]
    public async Task A_tenant_with_a_character_outside_the_allowed_set_stops_the_runner(string tenant)
    {
        // A store refuses such a tenant on every read, so the row is unreachable, and SQL Server joins an accented
        // letter written two ways into one tenant: the guard names the table before anything else runs.
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('{tenant}', '["openai"]', '2026-09-19T00:00:00Z');
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
        failure.Message.ShouldContain("canonical");
    }

    [Fact]
    public async Task The_default_retention_policy_row_passes()
    {
        // "*" in retention_policies is the default policy of every tenant, not a tenant.
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}retention_policies (id, tenant_id, target, created_at, updated_at)
            VALUES ('11111111-1111-1111-1111-111111111111', '*', 'run_events', '2026-09-19T00:00:00Z', '2026-09-19T00:00:00Z');
            """);

        await Should.NotThrowAsync(async () => await context.Migrations.ApplyAsync());
    }

    [Fact]
    public async Task The_wildcard_in_any_other_table_stops_the_runner()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        await context.Migrations.ApplyAsync();

        await context.ExecuteAsync($"""
            INSERT INTO {context.TablePrefix}tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('*', '["openai"]', '2026-09-19T00:00:00Z');
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
    }

    [Fact]
    public async Task The_predicate_flags_exactly_the_characters_outside_the_alphabet()
    {
        // Measured over every code point of the basic plane but NUL and the surrogates, one per row, so that a
        // bracket class that reads differently than it looks cannot hide.
        await using var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        var allowed = string.Join(", ", Alphabet().Select(static character => $"'{character}'"));
        var predicate = context.StoreContext.Dialect.NonCanonicalTenantIdPredicate;

        var wrong = await context.ScalarAsync<string>($"""
            WITH RECURSIVE c(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM c WHERE i < 65535),
            probe AS (SELECT i, char(i) AS tenant_id FROM c WHERE i NOT BETWEEN 55296 AND 57343)
            SELECT group_concat(i) FROM probe
            WHERE (({predicate}) AND tenant_id IN ({allowed}))
               OR (NOT ({predicate}) AND tenant_id NOT IN ({allowed}));
            """);

        wrong.ShouldBeNull("code points the predicate got wrong (flagged although allowed, or accepted although not)");
    }

    /// <summary>The characters the tenant rule accepts, each in its canonical form, read from the rule itself.</summary>
    private static List<char> Alphabet()
        => Enumerable.Range(0, 0x10000)
            .Select(static code => (char)code)
            .Select(static character => AmbientTenantScope.TryNormalize(character.ToString(), out var canonical) ? canonical : null)
            .Where(static canonical => canonical is not null)
            .Select(static canonical => canonical![0])
            .Distinct()
            .Order()
            .ToList();

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
