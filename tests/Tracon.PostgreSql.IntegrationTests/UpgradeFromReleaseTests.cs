using Npgsql;
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

    /// <summary>
    /// The database of the last release is upgraded in a database whose default collation is Turkish (ICU <c>tr-TR</c>),
    /// with the optional sets on.
    /// </summary>
    /// <returns>The completion task.</returns>
    /// <remarks>
    /// A migration that folds letter case with <c>lower()</c> or compares text by the database collation behaves the same on
    /// an English database and differently on a Turkish one (the dotless i). The matrix cell that runs the whole suite under
    /// <c>tr-TR</c> is a local run; this test makes the upgrade of that locale part of the default run, in a database of
    /// its own on the same server.
    /// </remarks>
    [Fact]
    public async Task The_database_of_the_last_release_is_upgraded_in_a_Turkish_locale_database()
    {
        var name = "tr_" + Guid.NewGuid().ToString("N")[..12];

        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync(TestContext.Current.CancellationToken);

            // ICU locales are a per-database setting from PostgreSQL 15 on; a cell older than that has nothing to test here.
            Assert.SkipUnless(admin.PostgreSqlVersion.Major >= 15, "A per-database ICU locale needs PostgreSQL 15 or later.");

            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE {name} TEMPLATE template0 ENCODING 'UTF8' LOCALE_PROVIDER icu ICU_LOCALE 'tr-TR';";
            await create.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Database = name, Pooling = false }.ConnectionString;

        try
        {
            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);

                await using var extension = connection.CreateCommand();
                extension.CommandText = "CREATE EXTENSION IF NOT EXISTS vector;";
                await extension.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

                // Prove the database is the Turkish one before the gate says anything about it: the dotted capital of the
                // Turkish alphabet lower-cases to the dotted small i, and the English one does not.
                await using var probe = connection.CreateCommand();
                probe.CommandText = "SELECT lower('I' COLLATE \"default\") = '\u0131';";
                (await probe.ExecuteScalarAsync(TestContext.Current.CancellationToken)).ShouldBe(true, "the database does not fold I to the dotless i: it is not a Turkish locale database");
            }

            await using var database = new PostgresUpgradeDatabase(
                PostgresTestContext.Create(connectionString, PostgresTestContext.NewSchemaName(), enableKnowledge: true, enableReadViews: true));

            await UpgradeTheReleaseAsync(database, optionalSets: true);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.ConnectionString);
            await admin.OpenAsync(TestContext.Current.CancellationToken);

            await using var drop = admin.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {name} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    /// <inheritdoc />
    internal override ValueTask<IUpgradeDatabase> CreateDatabaseAsync()
        => ValueTask.FromResult<IUpgradeDatabase>(
            new PostgresUpgradeDatabase(PostgresTestContext.Create(fixture, PostgresTestContext.NewSchemaName(), enableKnowledge: false)));

    /// <inheritdoc />
    internal override ValueTask<IUpgradeDatabase> CreateDatabaseWithOptionalSetsAsync()
        => ValueTask.FromResult<IUpgradeDatabase>(
            new PostgresUpgradeDatabase(PostgresTestContext.Create(fixture, PostgresTestContext.NewSchemaName(), enableKnowledge: true, enableReadViews: true)));
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
