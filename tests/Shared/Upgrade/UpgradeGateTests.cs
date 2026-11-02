using System.Text;

namespace Tracon.UpgradeGate;

/// <summary>
/// The upgrade gate: the database of the last release, with a row in every table, is moved to the migrations of the
/// tree, and every store reads what the release wrote.
/// </summary>
/// <remarks>
/// <para>
/// Each provider's test project derives one class from this one. The gate has two halves. The release half starts at
/// the schema the last release shipped (the migrations in the tree up to the last file the release held, proved to be
/// the release's files by <c>UpgradeBaselinePinTests</c>) and applies every migration since. The boundary half starts at
/// the schema after any one migration, so a migration still being written meets rows too, not only the ones the
/// release sealed.
/// </para>
/// <para>
/// The rows are the same in both: <c>UpgradeSeed.json</c>, written with the columns each table has had. Nothing the
/// current code writes is used to fill the old database, so a store that stops reading an old shape cannot also stop
/// writing it.
/// </para>
/// </remarks>
public abstract class UpgradeGateTests
{
    private const string SweepVariable = "TRACON_TEST_UPGRADE_SWEEP";

    /// <summary>Gets whether the sweep covers every migration by default, which only a provider whose database is a file can afford.</summary>
    protected abstract bool SweepsEveryBoundaryByDefault { get; }

    /// <summary>Opens a database with nothing in it.</summary>
    /// <returns>The database.</returns>
    internal abstract ValueTask<IUpgradeDatabase> CreateDatabaseAsync();

    /// <summary>The last migration a database that stopped there holds; the gate upgrades each such database.</summary>
    /// <param name="head">The migrations of the tree.</param>
    /// <param name="providerDirectory">The provider directory name in the manifest.</param>
    /// <param name="sweepsEveryBoundaryByDefault">Whether every migration is a boundary unless the environment narrows it.</param>
    /// <returns>The sequence numbers.</returns>
    internal static IEnumerable<int> SelectBoundaries(
        IReadOnlyList<MigrationDescriptor> head,
        string providerDirectory,
        bool sweepsEveryBoundaryByDefault)
    {
        var all = string.Equals(Environment.GetEnvironmentVariable(SweepVariable), "all", StringComparison.OrdinalIgnoreCase)
            || (sweepsEveryBoundaryByDefault && !string.Equals(Environment.GetEnvironmentVariable(SweepVariable), "release", StringComparison.OrdinalIgnoreCase));

        var releaseLast = UpgradeBaseline.Load().Providers[providerDirectory].Max(static migration => migration.Id);

        return head.Take(head.Count - 1)
            .Select(static migration => migration.Id)
            .Where(id => all || id >= releaseLast);
    }

    /// <summary>Every table the seed fills is read back by some probe, so a table no store reads cannot pass the gate unnoticed.</summary>
    [Fact]
    public void Every_table_of_the_seed_is_read_by_a_probe()
    {
        var unread = UpgradeSeed.Load().Tables.Keys.Where(table => !UpgradeProbes.ProbedTables.Contains(table)).Order(StringComparer.Ordinal).ToList();

        unread.ShouldBeEmpty("a table nothing reads after the upgrade proves nothing about the upgrade of it; add a probe that reads it through its store");
    }

    /// <summary>The database of the release is upgraded and every store reads its rows.</summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task The_database_of_the_last_release_is_upgraded_and_every_store_reads_its_rows()
    {
        var baseline = UpgradeBaseline.Load();
        var seed = UpgradeSeed.Load();

        await using var database = await CreateDatabaseAsync();

        var head = UpgradeHarness.HeadMigrations(database);
        var released = baseline.Providers[database.ProviderDirectory];
        var prefix = head.Where(migration => released.Any(file => file.Id == migration.Id)).ToList();

        prefix.Select(static migration => migration.Name).ShouldBe(
            released.Select(static file => file.Name),
            "the migrations of the tree start with every file the release held, in the same order");

        // A database of a preview carries checksums the files have outgrown (comments moved); the gate writes today's, as an
        // operator who followed the release notes would. From the first release candidate on the checksums stay the release's.
        await UpgradeHarness.ApplyAsync(
            database,
            prefix,
            migration => baseline.ChecksumsMayDrift ? migration.Checksum : released.Single(file => file.Id == migration.Id).Checksum);

        var seeded = await UpgradeHarness.SeedAsync(database, seed);
        var before = await UpgradeHarness.CountsAsync(database);

        foreach (var (table, count) in seeded.Rows)
        {
            before[table].ShouldBe(count, $"{table} holds exactly the rows of the seed before the upgrade");
        }

        (await database.Migrations.ApplyAsync()).ShouldBe(head.Count - prefix.Count, "every migration since the release is applied");

        await AssertUpgradedAsync(database, seed, seeded, before, strict: true);
    }

    /// <summary>The ledger of a release database is compared with the files of the tree, and a drift is refused or accepted as the line allows.</summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task The_ledger_of_the_last_release_is_judged_by_the_rule_of_its_line()
    {
        var baseline = UpgradeBaseline.Load();

        await using var database = await CreateDatabaseAsync();

        var head = UpgradeHarness.HeadMigrations(database);
        var released = baseline.Providers[database.ProviderDirectory];
        var prefix = head.Where(migration => released.Any(file => file.Id == migration.Id)).ToList();

        await UpgradeHarness.ApplyAsync(database, prefix, migration => released.Single(file => file.Id == migration.Id).Checksum);

        if (baseline.ChecksumsMayDrift)
        {
            // The preview line has no upgrade path for stored data between previews: the start is refused and says why.
            var refusal = await Should.ThrowAsync<TraconException>(async () => await database.Migrations.ApplyAsync());

            refusal.Message.ShouldContain("has been applied to the database but the file's content has changed");
        }
        else
        {
            // From the first release candidate on, the database of a release starts under the next one untouched.
            (await database.Migrations.ApplyAsync()).ShouldBe(head.Count - prefix.Count);
        }
    }

    /// <summary>A database that holds no row, and a database whose rows the current schema was created under, read the seed the same way.</summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task A_new_database_reads_the_rows_of_the_seed_the_same_way_as_an_upgraded_one()
    {
        var seed = UpgradeSeed.Load();

        await using var database = await CreateDatabaseAsync();

        var head = UpgradeHarness.HeadMigrations(database);

        await UpgradeHarness.ApplyAsync(database, head, static migration => migration.Checksum);

        var seeded = await UpgradeHarness.SeedAsync(database, seed);

        var failures = await UpgradeProbes.RunAsync(database, seed, seeded, strict: true);

        failures.ShouldBeEmpty("a probe that fails on a new database is wrong about the seed, not about the upgrade");
    }

    /// <summary>The upgrade of a database that stopped at one migration.</summary>
    /// <param name="lastApplied">The sequence number of the last migration the database holds.</param>
    /// <returns>The completion task.</returns>
    protected async Task UpgradeFromBoundaryAsync(int lastApplied)
    {
        var seed = UpgradeSeed.Load();

        await using var database = await CreateDatabaseAsync();

        var head = UpgradeHarness.HeadMigrations(database);
        var prefix = head.Where(migration => migration.Id <= lastApplied).ToList();

        prefix.ShouldNotBeEmpty();
        prefix.Count.ShouldBeLessThan(head.Count, "a boundary leaves at least one migration to apply");

        await UpgradeHarness.ApplyAsync(database, prefix, static migration => migration.Checksum);

        var seeded = await UpgradeHarness.SeedAsync(database, seed);
        var before = await UpgradeHarness.CountsAsync(database);

        (await database.Migrations.ApplyAsync()).ShouldBe(head.Count - prefix.Count);

        var releaseLast = UpgradeBaseline.Load().Providers[database.ProviderDirectory].Max(static migration => migration.Id);

        await AssertUpgradedAsync(database, seed, seeded, before, strict: lastApplied >= releaseLast);
    }

    private static async Task AssertUpgradedAsync(
        IUpgradeDatabase database,
        UpgradeSeed seed,
        SeedOutcome seeded,
        IReadOnlyDictionary<string, long> before,
        bool strict)
    {
        var after = await UpgradeHarness.CountsAsync(database);

        UpgradeHarness.DescribeCountChanges(before, after).ShouldBeEmpty("an upgrade neither drops, adds nor merges a row");

        var failures = await UpgradeProbes.RunAsync(database, seed, seeded, strict);

        if (failures.Count > 0)
        {
            var report = new StringBuilder();

            foreach (var failure in failures)
            {
                report.AppendLine(failure);
            }

            Assert.Fail($"{failures.Count} store(s) cannot read what the older schema holds:{Environment.NewLine}{report}");
        }
    }
}
