using Tracon.PostgreSql.IntegrationTests.Infrastructure;
using Tracon.UpgradeGate;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// The upgrade gate on PostgreSQL: the database of the last release is moved to the migrations of the tree and every
/// store reads the rows it holds.
/// </summary>
/// <remarks>
/// Each boundary builds the whole schema again on the server, so by default the boundary half runs from the last release
/// on; set <c>TRACON_TEST_UPGRADE_SWEEP</c> to <c>all</c> to stop at every migration.
/// </remarks>
public sealed class UpgradeFromReleaseTests(PostgresFixture fixture) : UpgradeGateTests
{
    /// <summary>The last migration of the database to upgrade.</summary>
    public static TheoryData<int> Boundaries { get; } = [.. SelectBoundaries(
        MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations."),
        "postgresql",
        sweepsEveryBoundaryByDefault: false)];

    /// <summary>A database that stopped at one migration is upgraded and read.</summary>
    /// <param name="lastApplied">The sequence number of the last migration the database holds.</param>
    /// <returns>The completion task.</returns>
    [Theory]
    [MemberData(nameof(Boundaries))]
    public Task A_database_that_stopped_at_a_migration_is_upgraded_and_read(int lastApplied)
        => UpgradeFromBoundaryAsync(lastApplied);

    /// <inheritdoc />
    internal override ValueTask<IUpgradeDatabase> CreateDatabaseAsync()
        => ValueTask.FromResult<IUpgradeDatabase>(
            new PostgresUpgradeDatabase(PostgresTestContext.Create(fixture, PostgresTestContext.NewSchemaName(), enableKnowledge: false)));
}

/// <summary>A PostgreSQL schema the gate builds, fills and upgrades.</summary>
/// <param name="context">The test context that owns the schema.</param>
internal sealed class PostgresUpgradeDatabase(PostgresTestContext context) : IUpgradeDatabase
{
    /// <inheritdoc />
    public SeedProvider Provider => SeedProvider.PostgreSql;

    /// <inheritdoc />
    public string ProviderDirectory => "postgresql";

    /// <inheritdoc />
    public SqlStoreContext StoreContext => context.StoreContext;

    /// <inheritdoc />
    public ITenantContext TenantContext => context.TenantContext;

    /// <inheritdoc />
    public MigrationRunner Migrations => context.Migrations;

    /// <inheritdoc />
    public string Schema => context.SchemaName;

    /// <inheritdoc />
    public string Qualify(string table) => context.SchemaName + "." + table;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => context.DisposeAsync();
}
