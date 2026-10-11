using Microsoft.Data.Sqlite;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The harness gives every table prefix its own SQLite writer lock (F-307).
/// </summary>
/// <remarks>
/// <para>
/// Every class used to share ONE database file. SQLite has one writer lock per
/// file, and its busy handler polls instead of queueing, so under a saturated
/// machine a statement in a random test starved past the 30-second command
/// timeout and failed with <c>database is locked</c>. That failure needs load
/// to appear; the property behind it does not. These tests check the property
/// directly, so putting two prefixes - two test classes - back into one file
/// turns the first test red on any machine.
/// </para>
/// </remarks>
public sealed class TestDatabaseIsolationTests(SqliteFixture fixture)
{
    [Fact]
    public async Task A_write_lock_held_under_one_prefix_does_not_block_a_write_under_another()
    {
        await using var holder = await SqliteTestContext.CreateAsync(fixture);
        await using var other = await SqliteTestContext.CreateAsync(fixture);

        await using var holding = new SqliteConnection(holder.Options.ConnectionString);
        await holding.OpenAsync(TestContext.Current.CancellationToken);
        await ExecuteAsync(holding, "BEGIN IMMEDIATE;");

        try
        {
            await using var probe = new SqliteConnection(other.Options.ConnectionString);
            await probe.OpenAsync(TestContext.Current.CancellationToken);
            await ExecuteAsync(probe, "PRAGMA busy_timeout = 0;");

            // A shared file refuses this with SQLITE_BUSY: the holder owns its
            // only writer lock. The one-second timeout bounds that red case.
            await ExecuteAsync(probe, "BEGIN IMMEDIATE;");
            await ExecuteAsync(probe, "ROLLBACK;");
        }
        finally
        {
            await ExecuteAsync(holding, "ROLLBACK;");
        }
    }

    /// <summary>
    /// The counterpart: the deliberate concurrency tests open several contexts
    /// on ONE prefix to make them contend. If isolation went one step too far,
    /// those tests would pass without any contention at all.
    /// </summary>
    [Fact]
    public async Task Contexts_that_name_the_same_prefix_share_one_database()
    {
        await using var owner = await SqliteTestContext.CreateAsync(fixture);
        await using var second = SqliteTestContext.Create(fixture, owner.TablePrefix);

        var applied = await second.ScalarAsync<long>(
            $"SELECT COUNT(*) FROM {owner.TablePrefix}__migrations;");

        applied.ShouldBeGreaterThan(0, "the second context must see the migrations the owner applied");
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 1;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
