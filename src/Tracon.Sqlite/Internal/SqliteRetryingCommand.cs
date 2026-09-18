using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Tracon;

/// <summary>
/// A <see cref="DbCommand"/> decorator that resends a statement SQLite refused
/// because another writer held the database.
/// </summary>
/// <remarks>
/// <para>
/// SQLite serializes writers. <c>PRAGMA busy_timeout</c> makes a connection
/// WAIT for a lock, and <see cref="SqliteDataSource"/> sets it, but it does not
/// cover every refusal: when a deferred transaction upgrades from reading to
/// writing and another writer has already changed the database since the read
/// began, SQLite returns <c>SQLITE_BUSY</c> IMMEDIATELY, whatever the timeout
/// is, because waiting could not help - the reader's snapshot is already
/// stale. The only answer to that one is to send the statement again.
/// </para>
/// <para>
/// So the layer is a statement-level retry with exponential backoff and
/// jitter, bounded in both attempts and total wait. It sits in the SQLite
/// package alone: Postgres and SQL Server report a write conflict as a
/// deadlock victim, which the stores already handle where it matters.
/// </para>
/// <para>
/// A statement inside a caller-managed transaction is NEVER retried. Only
/// the whole transaction can be retried - resending one statement of it would
/// reapply part of a unit of work whose earlier statements may already have
/// been rolled back by SQLite. The retry therefore applies to the
/// autocommit statements the stores send through the data source, which is
/// where the measured failures were: a contract test writing a single row
/// (<c>SqlSkillScriptGrantStore.GrantAsync</c>) under a saturated machine.
/// </para>
/// </remarks>
internal sealed class SqliteRetryingCommand : DbCommand
{
    /// <summary><c>SQLITE_BUSY</c> (5) and <c>SQLITE_LOCKED</c> (6).</summary>
    private static readonly int[] WriteLockConflicts = [5, 6];

    /// <summary>
    /// How many times a refused statement is sent again.
    /// </summary>
    /// <remarks>
    /// Six attempts with the delays below wait at most about 1.5 seconds in
    /// total, on top of the connection's own <c>busy_timeout</c>. The bound is
    /// deliberate: a database that is still locked after that is not
    /// contended, it is held, and reporting the error is the right answer.
    /// </remarks>
    private const int MaxAttempts = 6;

    private readonly DbCommand _inner;

    public SqliteRetryingCommand(DbCommand inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string CommandText
    {
        get => _inner.CommandText;
        set => _inner.CommandText = value!;
    }

    /// <inheritdoc />
    public override int CommandTimeout
    {
        get => _inner.CommandTimeout;
        set => _inner.CommandTimeout = value;
    }

    /// <inheritdoc />
    public override CommandType CommandType
    {
        get => _inner.CommandType;
        set => _inner.CommandType = value;
    }

    /// <inheritdoc />
    public override bool DesignTimeVisible
    {
        get => _inner.DesignTimeVisible;
        set => _inner.DesignTimeVisible = value;
    }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource
    {
        get => _inner.UpdatedRowSource;
        set => _inner.UpdatedRowSource = value;
    }

    /// <inheritdoc />
    protected override DbConnection? DbConnection
    {
        get => _inner.Connection;
        set => _inner.Connection = value;
    }

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

    /// <inheritdoc />
    protected override DbTransaction? DbTransaction
    {
        get => _inner.Transaction;
        set => _inner.Transaction = value;
    }

    /// <inheritdoc />
    public override void Cancel() => _inner.Cancel();

    /// <inheritdoc />
    public override void Prepare() => _inner.Prepare();

    /// <inheritdoc />
    public override int ExecuteNonQuery() => Retry(() => _inner.ExecuteNonQuery());

    /// <inheritdoc />
    public override object? ExecuteScalar() => Retry(() => _inner.ExecuteScalar());

    /// <inheritdoc />
    public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        => RetryAsync(token => _inner.ExecuteNonQueryAsync(token), cancellationToken);

    /// <inheritdoc />
    public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        => RetryAsync(token => _inner.ExecuteScalarAsync(token), cancellationToken);

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter() => _inner.CreateParameter();

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => Retry(() => _inner.ExecuteReader(behavior));

    /// <inheritdoc />
    protected override Task<DbDataReader> ExecuteDbDataReaderAsync(
        CommandBehavior behavior,
        CancellationToken cancellationToken)
        => RetryAsync(token => _inner.ExecuteReaderAsync(behavior, token), cancellationToken);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);

        GC.SuppressFinalize(this);
    }

    /// <summary>Whether a refused statement may be sent again.</summary>
    private bool CanRetry(Exception exception)
        => _inner.Transaction is null
            && exception is SqliteException sql
            && Array.IndexOf(WriteLockConflicts, sql.SqliteErrorCode) >= 0;

    /// <summary>The wait before the given attempt: 25ms doubling, plus jitter.</summary>
    /// <remarks>
    /// The jitter matters more than the curve. Several writers refused at the
    /// same instant and backing off by the same amount collide again on the
    /// next attempt, which is the shape that turns contention into a cascade.
    /// </remarks>
    private static TimeSpan Backoff(int attempt)
        => TimeSpan.FromMilliseconds((25 * Math.Pow(2, attempt - 1)) + Random.Shared.Next(0, 25));

    private T Retry<T>(Func<T> execute)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return execute();
            }
            catch (Exception exception) when (attempt < MaxAttempts && CanRetry(exception))
            {
                Thread.Sleep(Backoff(attempt));
            }
        }
    }

    private async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> execute, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await execute(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (attempt < MaxAttempts && CanRetry(exception))
            {
                await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
