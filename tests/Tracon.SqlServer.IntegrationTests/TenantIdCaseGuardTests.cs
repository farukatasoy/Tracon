using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The migration runner refuses a SQL Server database that holds a
/// non-canonical <c>tenant_id</c> (phase 179).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>This is the case that holds
/// <c>SqlServerDialect.NonCanonicalTenantIdPredicate</c>.</strong> SQL Server's
/// default collation is case-INsensitive, so
/// <c>tenant_id &lt;&gt; LOWER(tenant_id)</c> — the base dialect's predicate —
/// is ALWAYS false here: the comparison ignores the very difference it is
/// meant to find, the guard reports a clean database, and the offending rows
/// migrate on untouched.
/// </para>
/// <para>
/// The SQLite cases cannot stand in for this one. There the base predicate
/// already works, so deleting the SQL Server override leaves every one of them
/// green. Only a real SQL Server database, on its own default collation, turns
/// that deletion red.
/// </para>
/// </remarks>
public sealed class TenantIdCaseGuardTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task A_non_canonical_row_stops_the_runner_and_names_the_table()
    {
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('Acme', '["openai"]', SYSDATETIMEOFFSET());
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
        failure.Message.ShouldContain("canonical");

        // The guard reports; it does not repair.
        var stillThere = await context.ScalarAsync<int>($"""
            SELECT COUNT(*) FROM {context.SchemaName}.tenant_egress_policies
            WHERE tenant_id COLLATE Latin1_General_BIN2 = 'Acme' COLLATE Latin1_General_BIN2;
            """);

        stillThere.ShouldBe(1);
    }

    [Fact]
    public async Task A_canonical_row_passes()
    {
        // The other half. Without it a guard that always threw would satisfy
        // the case above, and a guard that cried wolf on every start would be
        // switched off within a week.
        await using var context = await SqlServerTestContext.CreateAsync(fixture);

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('acme', '["openai"]', SYSDATETIMEOFFSET());
            """);

        await Should.NotThrowAsync(async () => await context.Migrations.ApplyAsync());
    }
}
