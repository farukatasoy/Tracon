using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The migration runner refuses a SQL Server database that holds a
/// non-canonical <c>tenant_id</c>.
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
public sealed class TenantIdCaseGuardTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    [Fact]
    public async Task A_non_canonical_row_stops_the_runner_and_names_the_table()
    {
        var context = await schema.ResetAsync(tenantId: "default");

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('Acme', '["openai"]', SYSDATETIMEOFFSET());
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
        failure.Message.ShouldContain("canonical");
        failure.Message.ShouldNotContain("audit trail", Case.Insensitive);

        // The guard reports; it does not repair.
        var stillThere = await context.ScalarAsync<int>($"""
            SELECT COUNT(*) FROM {context.SchemaName}.tenant_egress_policies
            WHERE tenant_id COLLATE Latin1_General_BIN2 = 'Acme' COLLATE Latin1_General_BIN2;
            """);

        stillThere.ShouldBe(1);
    }

    [Fact]
    public async Task An_audit_entry_outside_the_allowed_set_is_not_offered_a_rename()
    {
        // The tenant is part of each entry's hash: a renamed entry fails the chain verification, so the message
        // must not send the operator down that road without saying so.
        const string tenant = "caf\u00E9";

        var context = await schema.ResetAsync(tenantId: "default");

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.audit_log (id, tenant_id, actor, action, entity, created_at)
            VALUES (NEWID(), N'{tenant}', N'admin', N'agent.create', N'agent:demo', SYSDATETIMEOFFSET());
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
        // 🚨 SQL Server joins a composed accented letter and its decomposed spelling, and a full-width letter and its
        // plain one, into one tenant under its default collation. The predicate has to read the code points.
        var context = await schema.ResetAsync(tenantId: "default");

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES (N'{tenant}', '["openai"]', SYSDATETIMEOFFSET());
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
        failure.Message.ShouldContain("canonical");
    }

    [Fact]
    public async Task A_full_width_asterisk_is_not_the_default_retention_policy()
    {
        // 🚨 Under SQL Server's default collation U+FF0A equals "*" (measured), so a plain comparison with "*" would
        // take this row for the default policy and wave it through.
        const string tenant = "\uFF0A";

        var context = await schema.ResetAsync(tenantId: "default");

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.retention_policies (id, tenant_id, target, created_at, updated_at)
            VALUES (NEWID(), N'{tenant}', N'run_events', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("retention_policies");
    }

    [Fact]
    public async Task The_default_retention_policy_row_passes()
    {
        // "*" in retention_policies is the default policy of every tenant, not a tenant.
        var context = await schema.ResetAsync(tenantId: "default");

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.retention_policies (id, tenant_id, target, created_at, updated_at)
            VALUES (NEWID(), N'*', N'run_events', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        await Should.NotThrowAsync(async () => await context.Migrations.ApplyAsync());
    }

    [Fact]
    public async Task The_wildcard_in_any_other_table_stops_the_runner()
    {
        var context = await schema.ResetAsync(tenantId: "default");

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES (N'*', '["openai"]', SYSDATETIMEOFFSET());
            """);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("tenant_egress_policies");
    }

    [Fact]
    public async Task The_predicate_flags_exactly_the_characters_outside_the_alphabet()
    {
        // 🚨 Measured over every UTF-16 code unit but NUL, one per row. A bracket class with the hyphen written last
        // read ".-]" as a range from a dot to a closing bracket: capital letters and ":" passed as canonical and "-"
        // was flagged. No hand-picked sample showed it, because the samples that failed were the ones nobody wrote.
        var context = await schema.ResetAsync(tenantId: "default");

        var allowed = string.Join(", ", Alphabet().Select(static character => $"N'{character}'"));
        var predicate = context.StoreContext.Dialect.NonCanonicalTenantIdPredicate;

        var wrong = await context.ScalarAsync<string>($"""
            WITH n AS (SELECT TOP (65535) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS c FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            SELECT STRING_AGG(CAST(c AS nvarchar(max)), N',') FROM (SELECT c, NCHAR(c) AS tenant_id FROM n) AS probe
            WHERE (({predicate}) AND tenant_id COLLATE Latin1_General_BIN2 IN ({allowed}))
               OR (NOT ({predicate}) AND tenant_id COLLATE Latin1_General_BIN2 NOT IN ({allowed}));
            """);

        wrong.ShouldBeNull("code units the predicate got wrong (flagged although allowed, or accepted although not)");
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
        var context = await schema.ResetAsync(tenantId: "default");

        await context.ExecuteAsync($"""
            INSERT INTO {context.SchemaName}.tenant_egress_policies (tenant_id, allowed_providers, updated_at)
            VALUES ('acme', '["openai"]', SYSDATETIMEOFFSET());
            """);

        await Should.NotThrowAsync(async () => await context.Migrations.ApplyAsync());
    }
}
