using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The "views" optional migration set (Phase 111): opt-in via
/// <c>EnableReadViews</c>, off by default.
/// </summary>
public sealed class ReadViewOptionalSetTests(SqliteFixture fixture)
{
    [Fact]
    public async Task Read_views_disabled_by_default_creates_no_view()
    {
        await using var context = await SqliteTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableReadViews: false);

        await context.Migrations.ApplyAsync();

        var hasRunsV1 = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM sqlite_master WHERE type = 'view' AND name = '{context.TablePrefix}runs_v1';");

        hasRunsV1.ShouldBe(0);

        var ledgerHasViewsRows = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.TablePrefix}__migrations WHERE set_name = 'views';");

        ledgerHasViewsRows.ShouldBe(0);
    }

    [Fact]
    public async Task Read_views_enabled_creates_runs_v1()
    {
        await using var context = await SqliteTestContext.CreateAsync(
            fixture,
            applyMigrations: false,
            enableReadViews: true);

        await context.Migrations.ApplyAsync();

        var hasRunsV1 = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM sqlite_master WHERE type = 'view' AND name = '{context.TablePrefix}runs_v1';");

        hasRunsV1.ShouldBe(1);

        var ledgerRow = await context.ScalarAsync<string>(
            $"SELECT name FROM {context.TablePrefix}__migrations WHERE set_name = 'views' AND id = 1;");

        ledgerRow.ShouldBe("0001_read_views");
    }
}
