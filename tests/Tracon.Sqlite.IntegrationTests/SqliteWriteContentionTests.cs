using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// A write SQLite refuses because another writer holds the database is sent
/// again instead of surfacing as <c>database is locked</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The measured failure: a full-solution run saturated the machine and
/// <c>Tracon.Sqlite.IntegrationTests</c> lost a real test plus 39 class
/// cleanups to <c>SQLite Error 5: 'database is locked'</c>, thrown inside the
/// product's own store. The same project alone passed 825 of 825. A consumer
/// writing from more than one place hits the same wall - this is the product's
/// concurrency limit, not the test harness's.
/// </para>
/// <para>
/// <c>busy_timeout</c> alone does not cover it: when a deferred transaction
/// upgrades from reading to writing and another writer has already changed the
/// database, SQLite returns <c>SQLITE_BUSY</c> immediately, whatever the
/// timeout, because waiting cannot help.
/// </para>
/// </remarks>
public sealed class SqliteWriteContentionTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        $"tracon-contention-{Guid.NewGuid():N}.db");

    /// <summary>
    /// Every command the stores send through the data source carries the
    /// retry.
    /// </summary>
    /// <remarks>
    /// The wiring is the part an integration test can prove. Staging real
    /// contention cannot: <c>busy_timeout</c> already absorbs a lock held for
    /// a short while, so such a test passes with the retry removed - measured,
    /// and the reason it is not written that way here.
    /// </remarks>
    [Fact]
    public async Task Every_command_the_data_source_hands_out_carries_the_retry()
    {
        await using var dataSource = new SqliteDataSource($"Data Source={_path}");

        await using var command = dataSource.CreateCommand("SELECT 1;");

        command.ShouldBeOfType<SqliteRetryingCommand>();

        Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), provider: null)
            .ShouldBe(1);
    }

    /// <summary>
    /// A statement refused because the database was busy or locked is sent
    /// again and lands.
    /// </summary>
    /// <remarks>
    /// <c>SQLITE_LOCKED</c> (6) is the code that makes this layer necessary
    /// rather than convenient: SQLite does NOT invoke the busy handler for it,
    /// so <c>busy_timeout</c> - whatever it is set to - never sees it.
    /// </remarks>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task A_statement_refused_while_the_database_was_busy_is_sent_again(int errorCode)
    {
        var inner = new AlwaysBusyCommand { ErrorCode = errorCode, SucceedFromAttempt = 3 };

        await using var command = new SqliteRetryingCommand(inner);

        (await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        inner.Attempts.ShouldBe(3);
    }

    /// <summary>
    /// A refused statement is sent again a BOUNDED number of times.
    /// </summary>
    /// <remarks>
    /// Deterministic on purpose: staging real contention proves the wiring
    /// (the test above) but cannot prove the bound, and an unbounded retry
    /// turns a held database into a hang.
    /// </remarks>
    [Fact]
    public async Task A_statement_that_stays_refused_gives_up()
    {
        var inner = new AlwaysBusyCommand();

        await using var command = new SqliteRetryingCommand(inner);

        await Should.ThrowAsync<SqliteException>(
            async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        inner.Attempts.ShouldBe(6);
    }

    /// <summary>
    /// A statement inside a caller-managed transaction is not retried on its
    /// own: only the whole transaction can be, and resending one statement of
    /// it would reapply part of a unit of work SQLite may already have rolled
    /// back.
    /// </summary>
    [Fact]
    public async Task A_statement_inside_a_transaction_is_not_retried()
    {
        var inner = new AlwaysBusyCommand { InsideTransaction = true };

        await using var command = new SqliteRetryingCommand(inner);

        await Should.ThrowAsync<SqliteException>(
            async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        inner.Attempts.ShouldBe(1);
    }

    /// <summary>A command that always reports the database as busy.</summary>
    private sealed class AlwaysBusyCommand : DbCommand
    {
        public int Attempts { get; private set; }

        public bool InsideTransaction { get; init; }

        public int ErrorCode { get; init; } = 5;

        /// <summary>The attempt that succeeds; never, when zero.</summary>
        public int SucceedFromAttempt { get; init; }

        [System.Diagnostics.CodeAnalysis.AllowNull]
        public override string CommandText { get; set; } = string.Empty;

        public override int CommandTimeout { get; set; }

        public override CommandType CommandType { get; set; }

        public override bool DesignTimeVisible { get; set; }

        public override UpdateRowSource UpdatedRowSource { get; set; }

        protected override DbConnection? DbConnection { get; set; }

        protected override DbParameterCollection DbParameterCollection
            => throw new NotSupportedException("The retry layer does not read parameters.");

        protected override DbTransaction? DbTransaction
        {
            get => InsideTransaction ? new FakeTransaction() : null;
            set => throw new NotSupportedException("The retry layer does not assign a transaction.");
        }

        public override void Cancel() => throw new NotSupportedException("Not exercised.");

        public override void Prepare() => throw new NotSupportedException("Not exercised.");

        public override int ExecuteNonQuery()
        {
            Attempts++;

            if (SucceedFromAttempt > 0 && Attempts >= SucceedFromAttempt)
            {
                return 1;
            }

            throw new SqliteException($"SQLite Error {ErrorCode}: 'database is locked'.", ErrorCode);
        }

        public override object ExecuteScalar() => throw new NotSupportedException("Not exercised.");

        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
            => Task.FromResult(ExecuteNonQuery());

        protected override DbParameter CreateDbParameter()
            => throw new NotSupportedException("Not exercised.");

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException("Not exercised.");
    }

    /// <summary>Stands in for "this command is inside a transaction".</summary>
    private sealed class FakeTransaction : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.Serializable;

        protected override DbConnection? DbConnection => null;

        public override void Commit() => throw new NotSupportedException("Not exercised.");

        public override void Rollback() => throw new NotSupportedException("Not exercised.");
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
