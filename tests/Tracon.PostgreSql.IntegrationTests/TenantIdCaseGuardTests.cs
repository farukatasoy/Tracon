using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The migration runner refuses a PostgreSQL database that holds a
/// non-canonical <c>tenant_id</c> (phase 179).
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
