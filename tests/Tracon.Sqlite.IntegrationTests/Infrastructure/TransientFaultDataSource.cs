using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Tracon.Sqlite.IntegrationTests.Infrastructure;

/// <summary>
/// Wraps a real <see cref="DbDataSource"/> and makes the first N executions of a
/// MATCHING statement fail with a transient write-lock conflict.
/// </summary>
/// <remarks>
/// <para>
/// The seam is the connection, not the dialect, deliberately: every statement the
/// migration runner sends — the schema, the ledger, the ledger upgrade, the ledger
/// read and each migration — goes through <see cref="DbConnection.CreateCommand"/>.
/// A dialect-level fake would only reach the one method it overrides and would
/// leave the other statements untested.
/// </para>
/// <para>
/// The injected error is <c>SQLITE_BUSY</c> (5), which
/// <c>SqliteDialect.IsDeadlock</c> classifies as transient — SQLite serializes
/// writers instead of detecting a cycle, but the CALLER sees what SQL Server's
/// deadlock victim sees: a write that was refused and succeeds when sent again.
/// </para>
/// </remarks>
internal sealed class TransientFaultDataSource : DbDataSource
{
    private readonly DbDataSource _inner;
    private readonly Func<string, bool> _matches;
    private readonly int _budget;
    private int _remaining;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="inner">The real data source every call is forwarded to.</param>
    /// <param name="matches">Decides whether a command text is a target.</param>
    /// <param name="failures">How many matching executions fail before the fault stops.</param>
    public TransientFaultDataSource(DbDataSource inner, Func<string, bool> matches, int failures)
    {
        _inner = inner;
        _matches = matches;
        _budget = failures;
        _remaining = failures;
    }

    /// <inheritdoc />
    public override string ConnectionString => _inner.ConnectionString;

    /// <summary>Gets the number of injected failures that were actually consumed.</summary>
    public int ConsumedFailures => _budget - Volatile.Read(ref _remaining);

    /// <inheritdoc />
    protected override DbConnection CreateDbConnection()
        => new FaultConnection(_inner.CreateConnection(), this);

    /// <summary>Decides whether this execution must fail, consuming one budgeted failure.</summary>
    /// <param name="commandText">The command text about to run.</param>
    /// <returns><see langword="true"/> when the caller must throw.</returns>
    internal bool ShouldFail(string commandText)
    {
        if (!_matches(commandText))
        {
            return false;
        }

        while (true)
        {
            var current = Volatile.Read(ref _remaining);

            if (current <= 0)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _remaining, current - 1, current) == current)
            {
                return true;
            }
        }
    }

    /// <summary>Builds the transient error the injected failure throws.</summary>
    /// <returns>A <c>SQLITE_BUSY</c> exception.</returns>
    internal static SqliteException Conflict()
        => new("SQLite Error 5: 'database is locked'.", 5, 5);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A connection that hands out fault-injecting commands.</summary>
    /// <param name="inner">The real connection.</param>
    /// <param name="owner">The data source that owns the failure budget.</param>
    private sealed class FaultConnection(DbConnection inner, TransientFaultDataSource owner) : DbConnection
    {
        /// <inheritdoc />
        [AllowNull]
        public override string ConnectionString
        {
            get => inner.ConnectionString;
            set => inner.ConnectionString = value;
        }

        /// <inheritdoc />
        public override string Database => inner.Database;

        /// <inheritdoc />
        public override string DataSource => inner.DataSource;

        /// <inheritdoc />
        public override string ServerVersion => inner.ServerVersion;

        /// <inheritdoc />
        public override ConnectionState State => inner.State;

        /// <inheritdoc />
        public override void ChangeDatabase(string databaseName) => inner.ChangeDatabase(databaseName);

        /// <inheritdoc />
        public override void Close() => inner.Close();

        /// <inheritdoc />
        public override Task CloseAsync() => inner.CloseAsync();

        /// <inheritdoc />
        public override void Open() => inner.Open();

        /// <inheritdoc />
        public override Task OpenAsync(CancellationToken cancellationToken) => inner.OpenAsync(cancellationToken);

        /// <inheritdoc />
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => inner.BeginTransaction(isolationLevel);

        /// <inheritdoc />
        protected override DbCommand CreateDbCommand() => new FaultCommand(inner.CreateCommand(), owner);

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>A command that throws a transient conflict while the budget lasts.</summary>
    /// <param name="inner">The real command.</param>
    /// <param name="owner">The data source that owns the failure budget.</param>
    private sealed class FaultCommand(DbCommand inner, TransientFaultDataSource owner) : DbCommand
    {
        /// <inheritdoc />
        [AllowNull]
        public override string CommandText
        {
            get => inner.CommandText;
            set => inner.CommandText = value;
        }

        /// <inheritdoc />
        public override int CommandTimeout
        {
            get => inner.CommandTimeout;
            set => inner.CommandTimeout = value;
        }

        /// <inheritdoc />
        public override CommandType CommandType
        {
            get => inner.CommandType;
            set => inner.CommandType = value;
        }

        /// <inheritdoc />
        public override bool DesignTimeVisible
        {
            get => inner.DesignTimeVisible;
            set => inner.DesignTimeVisible = value;
        }

        /// <inheritdoc />
        public override UpdateRowSource UpdatedRowSource
        {
            get => inner.UpdatedRowSource;
            set => inner.UpdatedRowSource = value;
        }

        /// <inheritdoc />
        protected override DbConnection? DbConnection
        {
            get => inner.Connection;
            set => inner.Connection = value;
        }

        /// <inheritdoc />
        protected override DbParameterCollection DbParameterCollection => inner.Parameters;

        /// <inheritdoc />
        protected override DbTransaction? DbTransaction
        {
            get => inner.Transaction;
            set => inner.Transaction = value;
        }

        /// <inheritdoc />
        public override void Cancel() => inner.Cancel();

        /// <inheritdoc />
        public override void Prepare() => inner.Prepare();

        /// <inheritdoc />
        protected override DbParameter CreateDbParameter() => inner.CreateParameter();

        /// <inheritdoc />
        public override int ExecuteNonQuery()
        {
            ThrowIfInjected();

            return inner.ExecuteNonQuery();
        }

        /// <inheritdoc />
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken)
        {
            ThrowIfInjected();

            return inner.ExecuteNonQueryAsync(cancellationToken);
        }

        /// <inheritdoc />
        public override object? ExecuteScalar()
        {
            ThrowIfInjected();

            return inner.ExecuteScalar();
        }

        /// <inheritdoc />
        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            ThrowIfInjected();

            return inner.ExecuteScalarAsync(cancellationToken);
        }

        /// <inheritdoc />
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            ThrowIfInjected();

            return inner.ExecuteReader(behavior);
        }

        /// <inheritdoc />
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(
            CommandBehavior behavior,
            CancellationToken cancellationToken)
        {
            ThrowIfInjected();

            return await inner.ExecuteReaderAsync(behavior, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ThrowIfInjected()
        {
            if (owner.ShouldFail(inner.CommandText))
            {
                throw Conflict();
            }
        }
    }
}
