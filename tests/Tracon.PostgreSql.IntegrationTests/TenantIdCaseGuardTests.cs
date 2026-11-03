using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The migration runner refuses a PostgreSQL database that holds a
/// non-canonical <c>tenant_id</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>This is the case that holds
/// <c>PostgresDialect.TenantIdTableCatalogSql</c>.</strong> Each engine names
/// its catalog differently, and PostgreSQL's query carries a filter the other
/// two do not need: <c>t.table_type = 'BASE TABLE'</c>. A catalog query that
/// returns nothing makes the guard report a clean database over any number of
/// offending rows — the failure is silent, which is the same shape as the
/// SQL Server predicate trap next door.
/// </para>
/// <para>
/// The SQLite cases cannot stand in for this one: they exercise a different
/// catalog (<c>pragma_table_info</c>) through a different override. Only a
/// real PostgreSQL database turns a broken <c>information_schema</c> query
/// red.
/// </para>
/// </remarks>
public sealed class TenantIdCaseGuardTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_non_canonical_row_stops_the_runner_and_names_the_table()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('Acme', ARRAY['openai'], now());
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
        failure.Message.ShouldContain("canonical");
        failure.Message.ShouldNotContain("audit trail", Case.Insensitive);

        // The guard reports; it does not repair. On a case-sensitive engine
        // `Acme` and `acme` may be two real tenants, and merging two tenants
        // cannot be undone.
        var stillThere = await context.ScalarAsync<long>($"""
            SELECT count(*) FROM {context.SchemaName}.tenant_egress_policies
            WHERE tenant_id = 'Acme';
            """);

        stillThere.ShouldBe(1);
    }

    [Fact]
    public async Task An_audit_entry_outside_the_allowed_set_is_not_offered_a_rename()
    {
        // The tenant is part of each entry's hash: a renamed entry fails the chain verification, so the message
        // must not send the operator down that road without saying so.
        const string tenant = "caf\u00E9";

        await using var context = await PostgresTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.audit_log (id, tenant_id, actor, action, entity, created_at)
            VALUES (gen_random_uuid(), '{tenant}', 'admin', 'agent.create', 'agent:demo', now());
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
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('{tenant}', ARRAY['openai'], now());
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
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.retention_policies (id, tenant_id, target, created_at, updated_at)
            VALUES ('11111111-1111-1111-1111-111111111111', '*', 'run_events', now(), now());
            """);

        await Should.NotThrowAsync(async () => await context.Migrations.ApplyAsync());
    }

    [Fact]
    public async Task The_wildcard_in_any_other_table_stops_the_runner()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('*', ARRAY['openai'], now());
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
    }

    [Fact]
    public async Task The_predicate_flags_exactly_the_characters_outside_the_alphabet()
    {
        // Measured over every code point of the basic plane but NUL and the surrogates (PostgreSQL's chr() refuses
        // them), one per row, so that a bracket class that reads differently than it looks cannot hide.
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        var allowed = string.Join(", ", Alphabet().Select(static character => $"'{character}'"));
        var predicate = context.StoreContext.Dialect.NonCanonicalTenantIdPredicate;

        var wrong = await context.ScalarAsync<string>($"""
            SELECT string_agg(c::text, ',') FROM (
                SELECT g AS c, chr(g) AS tenant_id FROM generate_series(1, 65535) AS g WHERE g NOT BETWEEN 55296 AND 57343) AS probe
            WHERE (({predicate}) AND tenant_id COLLATE "C" IN ({allowed}))
               OR (NOT ({predicate}) AND tenant_id COLLATE "C" NOT IN ({allowed}));
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
    public async Task A_canonical_row_passes()
    {
        // The other half. Without it a guard that always threw would satisfy
        // the case above, and a guard that cried wolf on every start would be
        // switched off within a week.
        await using var context = await PostgresTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('acme', ARRAY['openai'], now());
            """);

        await Should.NotThrowAsync(async () => await context.Migrations.ApplyAsync());
    }
}
