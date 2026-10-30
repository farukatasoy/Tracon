using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A write SQLite refuses because another writer holds the database waits for
/// the lock up to the command timeout, and then fails.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Two layers absorb a held write lock, and neither is Tracon code.
/// <c>PRAGMA busy_timeout</c> (5 s, <see cref="SqliteDataSource"/>) makes one
/// attempt wait. Microsoft.Data.Sqlite then sends the statement again on
/// <c>SQLITE_BUSY</c>, <c>SQLITE_LOCKED</c> and <c>262</c>, 150 ms apart,
/// until <c>CommandTimeout</c> has passed (measured by decompiling
/// <c>SqliteDataReader.NextResult</c>, 2026-10-11).
/// </para>
/// <para>
/// Tracon used to wrap every data source command in a retry decorator of its
/// own. It never retried: its filter read <c>DbCommand.Transaction</c>,
/// the data source's command wrapper throws <c>NotSupportedException</c> from
/// that getter, and an exception thrown inside a <c>catch … when</c> filter
/// makes the filter false. The tests that proved it measured the
/// mechanism against a fake inner command and the wiring by type, never the
/// two together. These tests measure the behavior a store actually gets,
/// against the real data source and a real lock.
/// </para>
/// </remarks>
public sealed class SqliteWriteContentionTests : IDisposable
{
    /// <summary>Longer than <c>busy_timeout</c>, shorter than the default <c>CommandTimeout</c>.</summary>
    private static readonly TimeSpan LockHeldFor = TimeSpan.FromSeconds(7);

    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"tracon-contention-{Guid.NewGuid():N}.db");

    /// <summary>
    /// A lock held longer than <c>busy_timeout</c> does not fail the write:
    /// the statement is sent again until the lock is released.
    /// </summary>
    /// <remarks>
    /// A lock held for less than <c>busy_timeout</c> would prove nothing - one
    /// attempt absorbs it. Holding it past 5 s is what makes the resend loop the
    /// only thing between the store and <c>database is locked</c>.
    /// </remarks>
    [Fact]
    public async Task A_write_outlasts_a_lock_held_longer_than_the_busy_timeout()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dataSource = await CreateDataSourceWithTableAsync(cancellationToken);

        await using var holder = await HoldWriteLockAsync(cancellationToken);
        var release = ReleaseAfterAsync(holder, LockHeldFor, cancellationToken);

        var stopwatch = Stopwatch.StartNew();

        await using (var insert = dataSource.CreateCommand("INSERT INTO items (value) VALUES ('written');"))
        {
            (await insert.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
        }

        stopwatch.Stop();
        await release;

        // The write did wait past one busy_timeout attempt, so the resend loop
        // is what carried it - not a lock released early.
        stopwatch.Elapsed.ShouldBeGreaterThan(TimeSpan.FromSeconds(5));

        await using var count = dataSource.CreateCommand("SELECT COUNT(*) FROM items;");
        Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), provider: null).ShouldBe(1);
    }

    /// <summary>
    /// The wait is bounded: a lock still held when the command timeout has
    /// passed fails the write with <c>database is locked</c>.
    /// </summary>
    /// <remarks>
    /// An unbounded wait turns a held database into a hang. The bound is the
    /// command's own <c>CommandTimeout</c>; one attempt still waits a full
    /// <c>busy_timeout</c> first, so the failure arrives after about 5 s here.
    /// </remarks>
    [Fact]
    public async Task A_lock_still_held_after_the_command_timeout_fails_the_write()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var dataSource = await CreateDataSourceWithTableAsync(cancellationToken);

        await using var holder = await HoldWriteLockAsync(cancellationToken);

        await using var insert = dataSource.CreateCommand("INSERT INTO items (value) VALUES ('refused');");
        insert.CommandTimeout = 1;

        var stopwatch = Stopwatch.StartNew();

        var exception = await Should.ThrowAsync<SqliteException>(
            async () => await insert.ExecuteNonQueryAsync(cancellationToken));

        stopwatch.Stop();

        exception.SqliteErrorCode.ShouldBe(5);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(20));

        await holder.DisposeAsync();
    }

    private async Task<SqliteDataSource> CreateDataSourceWithTableAsync(CancellationToken cancellationToken)
    {
        var dataSource = new SqliteDataSource($"Data Source={_path}");

        await using var create = dataSource.CreateCommand(
            "CREATE TABLE items (id INTEGER PRIMARY KEY, value TEXT NOT NULL);");
        await create.ExecuteNonQueryAsync(cancellationToken);

        return dataSource;
    }

    /// <summary>Opens a second connection and takes the database's write lock.</summary>
    private async Task<SqliteConnection> HoldWriteLockAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={_path};Pooling=False");
        await connection.OpenAsync(cancellationToken);

        await using var begin = connection.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE; INSERT INTO items (value) VALUES ('holder');";
        await begin.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }

    private static async Task ReleaseAfterAsync(SqliteConnection holder, TimeSpan delay, CancellationToken cancellationToken)
    {
        // The lock must outlast one busy_timeout attempt; that duration is the claim.
        await Task.Delay(delay, cancellationToken); // delay: product

        // Rolled back, not committed: the holder's row must not count as the write under test.
        await using var rollback = holder.CreateCommand();
        rollback.CommandText = "ROLLBACK;";
        await rollback.ExecuteNonQueryAsync(cancellationToken);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            try
            {
                File.Delete(_path + suffix);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A temp file that is still held is cleaned up by the OS.
            }
        }
    }
}
