using System.Data.Common;
using System.Globalization;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// An upsert the server picks as a deadlock victim is run again, whole, and its
/// write lands once.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Regression (2026-10-13): <c>RunScoreStoreContract</c>'s concurrent-write case
/// failed once in a full-solution run with <c>SqlException</c> 1205 ("chosen as the
/// deadlock victim") and passed alone five times. The upsert transaction holds a
/// range lock until its commit, so two of them can wait on each other.
/// </para>
/// <para>
/// The race is made deterministic instead of being waited for: a second session
/// holds row 1, the upsert takes row 2 and then waits for row 1, and the second
/// session then asks for row 2. That is a cycle the server must break, and the
/// upsert's <c>DEADLOCK_PRIORITY LOW</c> makes it the victim every time.
/// </para>
/// </remarks>
public sealed class UpsertDeadlockRetryTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    [Fact]
    public async Task An_upsert_chosen_as_the_deadlock_victim_runs_again_and_writes_once()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var table = $"[{schema.Context.SchemaName}].[f430_deadlock_probe]";
        var dataSource = schema.Context.DataSource;

        await ExecuteAsync(
            dataSource,
            $"IF OBJECT_ID(N'{table}') IS NOT NULL DROP TABLE {table}; " +
            $"CREATE TABLE {table} (k int NOT NULL PRIMARY KEY, v int NOT NULL); " +
            $"INSERT INTO {table} (k, v) VALUES (1, 0), (2, 0);",
            cancellationToken);

        await using var blocker = await dataSource.OpenConnectionAsync(cancellationToken);
        var blockerSession = Convert.ToInt32(await ScalarAsync(blocker, null, "SELECT @@SPID;", cancellationToken), CultureInfo.InvariantCulture);
        await using var blockerTransaction = await blocker.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(blocker, blockerTransaction, $"UPDATE {table} SET v = v + 100 WHERE k = 1;", cancellationToken);

        var attempts = 0;
        var upsert = schema.Context.StoreContext.ExecuteUpsertAsync(
            $"SET DEADLOCK_PRIORITY LOW; UPDATE {table} SET v = v + 1 WHERE k = 2; UPDATE {table} SET v = v + 1 WHERE k = 1;",
            _ => Interlocked.Increment(ref attempts),
            DbHelpers.ExecuteAsync,
            cancellationToken).AsTask();

        // The upsert holds row 2 once it waits on the blocker's row 1.
        await WaitUntilBlockedByAsync(dataSource, blockerSession, cancellationToken);

        // Closes the cycle. The server kills the upsert's transaction, which frees
        // row 2 for this statement; the retry then waits for this commit.
        await ExecuteAsync(blocker, blockerTransaction, $"UPDATE {table} SET v = v + 100 WHERE k = 2;", cancellationToken);
        await blockerTransaction.CommitAsync(cancellationToken);

        await upsert;

        attempts.ShouldBe(2);
        await using var reader = await dataSource.OpenConnectionAsync(cancellationToken);
        var values = await ScalarAsync(
            reader,
            null,
            $"SELECT CONCAT((SELECT v FROM {table} WHERE k = 1), ',', (SELECT v FROM {table} WHERE k = 2));",
            cancellationToken);
        values.ShouldBe("101,101");
    }

    private static async Task WaitUntilBlockedByAsync(DbDataSource dataSource, int blockerSession, CancellationToken cancellationToken)
    {
        await using var probe = await dataSource.OpenConnectionAsync(cancellationToken);

        await WaitUntil.TrueAsync(
            async () => Convert.ToInt32(
                await ScalarAsync(
                    probe,
                    null,
                    $"SELECT COUNT(*) FROM sys.dm_exec_requests WHERE blocking_session_id = {blockerSession};",
                    cancellationToken),
                CultureInfo.InvariantCulture) > 0,
            "the upsert to wait on the blocking session",
            cancellationToken: cancellationToken);
    }

    private static async Task ExecuteAsync(DbDataSource dataSource, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, null, sql, cancellationToken);
    }

    private static async Task ExecuteAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(DbConnection connection, DbTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        return await command.ExecuteScalarAsync(cancellationToken);
    }
}
