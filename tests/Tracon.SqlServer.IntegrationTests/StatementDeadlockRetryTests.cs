using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Tracon.SqlServer.IntegrationTests.Infrastructure;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// A single statement outside a transaction that the server picks as a deadlock victim is sent again, whole, and its
/// effect lands once.
/// </summary>
/// <remarks>
/// <para>
/// The race is made deterministic instead of being waited for. For every attempt a blocker session takes row 2 and the
/// statement takes row 1 and then waits for row 2; the blocker then asks for row 1. That is a cycle the server must
/// break, and the statement's <c>DEADLOCK_PRIORITY LOW</c> makes it the victim every time. The blocker commits when the
/// victim is gone, so the next attempt finds both rows free.
/// </para>
/// <para>
/// <see cref="UpsertDeadlockRetryTests"/> proves the same for an upsert, which already ran in a transaction that was sent
/// again.
/// </para>
/// </remarks>
public sealed class StatementDeadlockRetryTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const string Victim = "SET DEADLOCK_PRIORITY LOW; ";

    [Fact]
    public async Task A_write_chosen_as_the_deadlock_victim_runs_again_and_writes_once()
    {
        await using var probe = await ProbeAsync();

        var attempts = await probe.RunAsync(
            $"{Victim}UPDATE {probe.Table} SET v = v + 1 WHERE k IN (1, 2);",
            DbHelpers.ExecuteAsync,
            victimAttempts: 1);

        attempts.ShouldBe(2);

        // Each blocker added 100 to both rows; the statement's own +1 landed once, on the attempt that finished.
        (await probe.ValuesAsync()).ShouldBe("101,101");
    }

    [Fact]
    public async Task A_read_chosen_as_the_deadlock_victim_returns_the_rows_of_the_attempt_that_finished_only()
    {
        await using var probe = await ProbeAsync();
        IReadOnlyList<int>? rows = null;

        var attempts = await probe.RunAsync(
            $"{Victim}SELECT v FROM {probe.Table} WITH (UPDLOCK) WHERE k IN (1, 2) ORDER BY k;",
            async (command, token) =>
            {
                rows = await DbHelpers.ReadListAsync(command, static reader => reader.GetInt32(0), token);

                return 0;
            },
            victimAttempts: 1);

        attempts.ShouldBe(2);

        // The first attempt had read row 1 before it was chosen; none of that reaches the caller.
        rows.ShouldNotBeNull().ShouldBe(new[] { 100, 100 });
    }

    [Fact]
    public async Task A_statement_that_is_the_victim_on_every_attempt_gives_the_last_error_after_five()
    {
        await using var probe = await ProbeAsync();

        var error = await Should.ThrowAsync<SqlException>(async () => await probe.RunAsync(
            $"{Victim}UPDATE {probe.Table} SET v = v + 1 WHERE k IN (1, 2);",
            DbHelpers.ExecuteAsync,
            victimAttempts: 10));

        error.Number.ShouldBe(1205);
        probe.Attempts.ShouldBe(SqlStoreContext.DeadlockAttempts);

        // Five blockers wrote; the statement never did.
        (await probe.ValuesAsync()).ShouldBe("500,500");
    }

    [Theory]
    [InlineData("INSERT INTO {0} (k, v) VALUES (1, 0);", 2627)]
    [InlineData("INSERT INTO {0} (k, v) VALUES (NULL, 0);", 515)]
    [InlineData("UPDATE {0} SET s = N'longer than ten' WHERE k = 1;", 2628)]
    public async Task An_error_that_is_not_a_deadlock_reaches_the_caller_after_one_attempt(string sql, int number)
    {
        await using var probe = await ProbeAsync();

        var error = await Should.ThrowAsync<SqlException>(async () => await probe.RunAsync(
            string.Format(CultureInfo.InvariantCulture, sql, probe.Table),
            DbHelpers.ExecuteAsync,
            victimAttempts: 0));

        error.Number.ShouldBe(number);
        probe.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_cancellation_while_the_victim_waits_ends_the_call_without_another_attempt()
    {
        await using var probe = await ProbeAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        await Should.ThrowAsync<OperationCanceledException>(async () => await probe.RunAsync(
            $"{Victim}UPDATE {probe.Table} SET v = v + 1 WHERE k IN (1, 2);",
            async (command, token) =>
            {
                try
                {
                    return await DbHelpers.ExecuteAsync(command, token);
                }
                catch (SqlException exception) when (exception.Number == 1205)
                {
                    // The caller gives up while the context decides whether to try again.
                    await cancellation.CancelAsync();

                    throw;
                }
            },
            victimAttempts: 1,
            cancellation.Token));

        probe.Attempts.ShouldBe(1);
        (await probe.ValuesAsync()).ShouldBe("100,100");
    }

    [Fact]
    public async Task A_report_of_a_job_item_chosen_as_the_deadlock_victim_counts_the_item_once()
    {
        // The report is two statements: the item row, then the counters of the job. In autocommit the first has committed
        // when the second is the victim, so the caller got the error with the item already marked and nothing counted,
        // and sending the whole text again would find the item reported and count nothing either.
        //
        // The second statement is made the victim by a lock conversion: the blocker holds a shared lock on the job row
        // until it commits, the report takes the update lock and waits to turn it into an exclusive one, and the blocker
        // then asks for the same exclusive lock. Each waits for the other; the blocker has the higher priority.
        await schema.ResetAsync();
        schema.Tenant.TenantId = "default";
        var jobs = schema.Context.Jobs;
        var cancellationToken = TestContext.Current.CancellationToken;

        var job = await jobs.EnqueueAsync(TestData.Job(), ["a", "b"], cancellationToken);
        var table = $"[{schema.Context.SchemaName}]";
        var dataSource = schema.Context.DataSource;

        await using var blocker = await dataSource.OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(blocker, null, "SET DEADLOCK_PRIORITY HIGH;", cancellationToken);
        var blockerSession = Convert.ToInt32(await ScalarAsync(blocker, null, "SELECT @@SPID;", cancellationToken), CultureInfo.InvariantCulture);
        await using var blockerTransaction = await blocker.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        _ = await ScalarAsync(blocker, blockerTransaction, $"SELECT done_items FROM {table}.jobs WHERE id = '{job.Id}';", cancellationToken);

        var report = jobs.ReportItemAsync(
            new JobItemResult { JobId = job.Id, Seq = 0, Status = JobItemStatus.Completed },
            cancellationToken).AsTask();

        await WaitUntilBlockedByAsync(dataSource, blockerSession, cancellationToken);

        // Closes the cycle: the report waits for the shared lock to go, and the blocker asks for the row.
        await ExecuteAsync(blocker, blockerTransaction, $"UPDATE {table}.jobs SET error_message = N'held' WHERE id = '{job.Id}';", cancellationToken);
        await blockerTransaction.CommitAsync(cancellationToken);

        await report;

        var current = (await jobs.GetAsync(job.TenantId, job.Id, cancellationToken)).ShouldNotBeNull();

        current.DoneItems.ShouldBe(1, "the item was reported once and counted once");
        current.FailedItems.ShouldBe(0);
        (await jobs.ListItemsAsync(job.Id, cancellationToken))[0].Status.ShouldBe(JobItemStatus.Completed);
    }

    private async Task<DeadlockProbe> ProbeAsync()
    {
        var probe = new DeadlockProbe(schema, $"[{schema.Context.SchemaName}].[f_statement_probe_{Guid.NewGuid():N}]");

        await probe.CreateAsync();

        return probe;
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
            "the statement to wait on the blocking session",
            cancellationToken: cancellationToken);
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

    /// <summary>A two-row table and the blockers that make a statement on it a deadlock victim.</summary>
    private sealed class DeadlockProbe(SqlServerSchemaFixture schema, string table) : IAsyncDisposable
    {
        private int _attempts;

        public string Table { get; } = table;

        public int Attempts => Volatile.Read(ref _attempts);

        public async Task CreateAsync()
        {
            await using var connection = await schema.Context.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

            await ExecuteAsync(
                connection,
                null,
                $"CREATE TABLE {Table} (k int NOT NULL PRIMARY KEY, v int NOT NULL, s nvarchar(10) NULL); " +
                $"INSERT INTO {Table} (k, v) VALUES (1, 0), (2, 0);",
                TestContext.Current.CancellationToken);
        }

        /// <summary>Runs the statement through the store context; the first attempts are made deadlock victims.</summary>
        /// <typeparam name="T">The result type of the command.</typeparam>
        /// <param name="sql">The statement text.</param>
        /// <param name="execute">Runs the command.</param>
        /// <param name="victimAttempts">How many attempts a blocker closes a cycle on, counted from the first.</param>
        /// <param name="token">The token of the call; the test's own when omitted.</param>
        /// <returns>The number of attempts made.</returns>
        public async Task<int> RunAsync<T>(
            string sql,
            Func<DbCommand, CancellationToken, ValueTask<T>> execute,
            int victimAttempts,
            CancellationToken token = default)
        {
            var cancellationToken = token == default ? TestContext.Current.CancellationToken : token;
            var blockers = new List<Task>();

            try
            {
                await schema.Context.StoreContext.ExecuteStatementAsync(
                    sql,
                    _ =>
                    {
                        var attempt = Interlocked.Increment(ref _attempts);

                        if (attempt <= victimAttempts)
                        {
                            // The blocker holds row 2 before the statement runs; it closes the cycle once the statement waits.
                            blockers.Add(TakeRowTwoAsync().GetAwaiter().GetResult());
                        }
                    },
                    execute,
                    cancellationToken);
            }
            finally
            {
                await Task.WhenAll(blockers);
            }

            return Attempts;
        }

        public async Task<string?> ValuesAsync()
        {
            await using var connection = await schema.Context.DataSource.OpenConnectionAsync(TestContext.Current.CancellationToken);

            return Convert.ToString(
                await ScalarAsync(
                    connection,
                    null,
                    $"SELECT CONCAT((SELECT v FROM {Table} WHERE k = 1), ',', (SELECT v FROM {Table} WHERE k = 2));",
                    TestContext.Current.CancellationToken),
                CultureInfo.InvariantCulture);
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = await schema.Context.DataSource.OpenConnectionAsync(CancellationToken.None);

            await ExecuteAsync(connection, null, $"DROP TABLE IF EXISTS {Table};", CancellationToken.None);
        }

        /// <summary>Takes row 2 and returns the task that, once a statement waits for it, asks for row 1 and commits.</summary>
        /// <returns>The task of the rest of the blocker's work.</returns>
        private async Task<Task> TakeRowTwoAsync()
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            var dataSource = schema.Context.DataSource;
            var blocker = await dataSource.OpenConnectionAsync(cancellationToken);
            var session = Convert.ToInt32(await ScalarAsync(blocker, null, "SELECT @@SPID;", cancellationToken), CultureInfo.InvariantCulture);
            var transaction = await blocker.BeginTransactionAsync(cancellationToken);

            await ExecuteAsync(blocker, transaction, $"UPDATE {Table} SET v = v + 100 WHERE k = 2;", cancellationToken);

            return FinishAsync();

            async Task FinishAsync()
            {
                await using (blocker)
                await using (transaction)
                {
                    await WaitUntilBlockedByAsync(dataSource, session, cancellationToken);

                    await ExecuteAsync(blocker, transaction, $"UPDATE {Table} SET v = v + 100 WHERE k = 1;", cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
            }
        }
    }
}
