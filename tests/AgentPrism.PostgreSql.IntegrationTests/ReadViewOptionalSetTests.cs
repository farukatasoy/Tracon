using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// The "views" optional migration set (Phase 111): opt-in via
/// <c>EnableReadViews</c>, off by default. Mirrors
/// <see cref="OptionalMigrationSetTests.Knowledge_disabled_by_default_creates_no_vector_objects"/>
/// for the "knowledge" set.
/// </summary>
public sealed class ReadViewOptionalSetTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Read_views_disabled_by_default_creates_no_view()
    {
        await using var context = await PostgresTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableReadViews: false);

        await context.Migrations.ApplyAsync();

        var hasRunsV1 = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.views " +
            $"WHERE table_schema = '{context.SchemaName}' AND table_name = 'runs_v1');");

        hasRunsV1.ShouldBeFalse();

        var ledgerHasViewsRows = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.SchemaName}.__migrations WHERE set_name = 'views';");

        ledgerHasViewsRows.ShouldBe(0);
    }

    [Fact]
    public async Task Read_views_enabled_creates_runs_v1()
    {
        await using var context = await PostgresTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableReadViews: true);

        await context.Migrations.ApplyAsync();

        var hasRunsV1 = await context.ScalarAsync<bool>(
            $"SELECT EXISTS (SELECT 1 FROM information_schema.views " +
            $"WHERE table_schema = '{context.SchemaName}' AND table_name = 'runs_v1');");

        hasRunsV1.ShouldBeTrue();

        var ledgerRow = await context.ScalarAsync<string>(
            $"SELECT name FROM {context.SchemaName}.__migrations WHERE set_name = 'views' AND id = 1;");

        ledgerRow.ShouldBe("0001_read_views");
    }
}
