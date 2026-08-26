using AgentPrism.SqlServer.IntegrationTests.Infrastructure;

namespace AgentPrism.SqlServer.IntegrationTests;

/// <summary>
/// The "views" optional migration set (Phase 111): opt-in via
/// <c>EnableReadViews</c>, off by default.
/// </summary>
public sealed class ReadViewOptionalSetTests(SqlServerFixture fixture)
{
    [Fact]
    public async Task Read_views_disabled_by_default_creates_no_view()
    {
        await using var context = await SqlServerTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableReadViews: false);

        await context.Migrations.ApplyAsync();

        var hasRunsV1 = await context.ScalarAsync<bool>(
            $"SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.views AS v " +
            $"JOIN sys.schemas AS s ON v.schema_id = s.schema_id " +
            $"WHERE s.name = '{context.SchemaName}' AND v.name = 'runs_v1') THEN 1 ELSE 0 END AS bit);");

        hasRunsV1.ShouldBeFalse();

        var ledgerHasViewsRows = await context.ScalarAsync<int>(
            $"SELECT count(*) FROM {context.SchemaName}.__migrations WHERE set_name = 'views';");

        ledgerHasViewsRows.ShouldBe(0);
    }

    [Fact]
    public async Task Read_views_enabled_creates_runs_v1()
    {
        await using var context = await SqlServerTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableReadViews: true);

        await context.Migrations.ApplyAsync();

        var hasRunsV1 = await context.ScalarAsync<bool>(
            $"SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.views AS v " +
            $"JOIN sys.schemas AS s ON v.schema_id = s.schema_id " +
            $"WHERE s.name = '{context.SchemaName}' AND v.name = 'runs_v1') THEN 1 ELSE 0 END AS bit);");

        hasRunsV1.ShouldBeTrue();

        var ledgerRow = await context.ScalarAsync<string>(
            $"SELECT name FROM {context.SchemaName}.__migrations WHERE set_name = 'views' AND id = 1;");

        ledgerRow.ShouldBe("0001_read_views");
    }
}
