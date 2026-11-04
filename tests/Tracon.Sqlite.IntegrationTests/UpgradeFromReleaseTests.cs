using Tracon.Sqlite.IntegrationTests.Infrastructure;
using Tracon.UpgradeGate;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The upgrade gate on SQLite: the database of the last release is moved to the migrations of the tree and every store
/// reads the rows it holds.
/// </summary>
/// <remarks>
/// A database is a file, so the boundary half runs at every migration by default; set <c>TRACON_TEST_UPGRADE_SWEEP</c> to
/// <c>release</c> to narrow it to the release and what came after.
/// </remarks>
public sealed class UpgradeFromReleaseTests(SqliteFixture fixture) : UpgradeGateTests
{
    /// <summary>The last migration of the database to upgrade.</summary>
    public static TheoryData<int> Boundaries { get; } = [.. SelectBoundaries(
        MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations."),
        "sqlite",
        sweepsEveryBoundaryByDefault: true)];

    /// <summary>A database that stopped at one migration is upgraded and read.</summary>
    /// <param name="lastApplied">The sequence number of the last migration the database holds.</param>
    /// <returns>The completion task.</returns>
    [Theory]
    [MemberData(nameof(Boundaries))]
    public Task A_database_that_stopped_at_any_migration_is_upgraded_and_read(int lastApplied)
        => UpgradeFromBoundaryAsync(lastApplied);

    /// <inheritdoc />
    internal override async ValueTask<IUpgradeDatabase> CreateDatabaseAsync()
    {
        var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false);

        return new SqliteUpgradeDatabase(context);
    }

    /// <inheritdoc />
    internal override async ValueTask<IUpgradeDatabase> CreateDatabaseWithOptionalSetsAsync()
    {
        var context = await SqliteTestContext.CreateAsync(fixture, applyMigrations: false, enableReadViews: true);

        return new SqliteUpgradeDatabase(context);
    }
}

/// <summary>A SQLite file the gate builds, fills and upgrades.</summary>
/// <param name="context">The test context that owns the file.</param>
internal sealed class SqliteUpgradeDatabase(SqliteTestContext context) : IUpgradeDatabase
{
    /// <inheritdoc />
    public SeedProvider Provider => SeedProvider.Sqlite;

    /// <inheritdoc />
    public string ProviderDirectory => "sqlite";

    /// <inheritdoc />
    public SqlStoreContext StoreContext => context.StoreContext;

    /// <inheritdoc />
    public ITenantContext TenantContext => context.TenantContext;

    /// <inheritdoc />
    public MigrationRunner Migrations => context.Migrations;

    /// <inheritdoc />
    public string Schema => context.TablePrefix;

    /// <inheritdoc />
    public string Qualify(string table) => context.TablePrefix + table;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => context.DisposeAsync();
}
