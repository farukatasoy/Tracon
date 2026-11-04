using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.UpgradeGate;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// The upgrade gate on SQL Server: the database of the last release is moved to the migrations of the tree and every
/// store reads the rows it holds.
/// </summary>
/// <remarks>
/// Each boundary builds the whole schema again on the server, so by default the boundary half runs from the last release
/// on; set <c>TRACON_TEST_UPGRADE_SWEEP</c> to <c>all</c> to stop at every migration.
/// </remarks>
public sealed class UpgradeFromReleaseTests(SqlServerFixture fixture) : UpgradeGateTests
{
    /// <summary>The last migration of the database to upgrade.</summary>
    public static TheoryData<int> Boundaries { get; } = [.. SelectBoundaries(
        MigrationDescriptor.Discover(typeof(MigrationRunner).Assembly, "Tracon.SqlServer.Migrations."),
        "sqlserver",
        sweepsEveryBoundaryByDefault: false)];

    /// <summary>A database that stopped at one migration is upgraded and read.</summary>
    /// <param name="lastApplied">The sequence number of the last migration the database holds.</param>
    /// <returns>The completion task.</returns>
    [Theory]
    [MemberData(nameof(Boundaries))]
    public Task A_database_that_stopped_at_a_migration_is_upgraded_and_read(int lastApplied)
        => UpgradeFromBoundaryAsync(lastApplied);

    /// <summary>
    /// The database of the last release is upgraded in a database whose collation is <c>Turkish_CI_AS</c>, with the optional
    /// sets on.
    /// </summary>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// Under a Turkish collation the capital I and the small i are two letters, and a statement that spells an object or a
    /// value in the other case, or folds case with <c>LOWER</c>, means something else. The matrix cell that runs the whole
    /// suite on a Turkish server is a local run; this test makes the upgrade under that collation part of the default
    /// run, in a database of its own on the same server.
    /// </remarks>
    [Fact]
    public async Task The_database_of_the_last_release_is_upgraded_in_a_Turkish_collation_database()
    {
        var name = "tr_" + Guid.NewGuid().ToString("N")[..12];

        await using (var admin = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync(TestContext.Current.CancellationToken);

            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{name}] COLLATE Turkish_CI_AS;";
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var connectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fixture.ConnectionString) { InitialCatalog = name, Pooling = false }.ConnectionString;

        try
        {
            await using (var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);

                // Prove the database is the Turkish one before the gate says anything about it: in this collation the capital I
                // equals the dotless small i and is not the dotted one.
                await using var probe = connection.CreateCommand();
                probe.CommandText = "SELECT CASE WHEN N'I' = N'\u0131' AND N'I' <> N'i' THEN 1 ELSE 0 END;";
                (await probe.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe(1, "the database is not under a Turkish collation");
            }

            await using var database = new SqlServerUpgradeDatabase(SqlServerTestContext.Create(connectionString, SqlServerTestContext.NewSchemaName(), enableReadViews: true));

            await UpgradeTheReleaseAsync(database, optionalSets: true);
        }
        finally
        {
            Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();

            await using var admin = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString);
            await admin.OpenAsync(CancellationToken.None);

            await using var drop = admin.CreateCommand();
            drop.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <inheritdoc />
    internal override ValueTask<IUpgradeDatabase> CreateDatabaseAsync()
        => ValueTask.FromResult<IUpgradeDatabase>(
            new SqlServerUpgradeDatabase(SqlServerTestContext.Create(fixture, SqlServerTestContext.NewSchemaName())));

    /// <inheritdoc />
    internal override ValueTask<IUpgradeDatabase> CreateDatabaseWithOptionalSetsAsync()
        => ValueTask.FromResult<IUpgradeDatabase>(
            new SqlServerUpgradeDatabase(SqlServerTestContext.Create(fixture, SqlServerTestContext.NewSchemaName(), enableReadViews: true)));
}

/// <summary>A SQL Server schema the gate builds, fills and upgrades.</summary>
/// <param name="context">The test context that owns the schema.</param>
internal sealed class SqlServerUpgradeDatabase(SqlServerTestContext context) : IUpgradeDatabase
{
    /// <inheritdoc />
    public SeedProvider Provider => SeedProvider.SqlServer;

    /// <inheritdoc />
    public string ProviderDirectory => "sqlserver";

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
