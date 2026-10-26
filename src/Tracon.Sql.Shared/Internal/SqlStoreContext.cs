using System.Data.Common;

namespace Tracon;

/// <summary>
/// Carries everything the shared store implementations need.
/// </summary>
/// <remarks>
/// <para>
/// The stores do not depend on an <c>Options</c> type: every provider has its own
/// settings class (<c>TraconPostgreSqlOptions</c>, <c>TraconSqlServerOptions</c>)
/// and those are public. Shared code cannot depend on a single one of them, so the
/// values derived from the settings are collected here and the <c>Use*</c> extension
/// of the provider registers this context in DI.
/// </para>
/// <para>
/// Injecting a single object also simplifies the
/// <c>ActivatorUtilities.CreateInstance</c> calls.
/// </para>
/// </remarks>
internal sealed class SqlStoreContext : IDisposable, IAsyncDisposable
{
    private readonly AsyncLocal<SqlLentTransaction?> _lentTransaction = new();

    /// <summary>Gets the data source. The provider driver manages the connection pool.</summary>
    public required DbDataSource DataSource { get; init; }

    /// <summary>
    /// Gets a value indicating whether Tracon built <see cref="DataSource"/>
    /// itself (from a connection string) and therefore owns it.
    /// </summary>
    /// <remarks>
    /// <see langword="false"/> when the consumer supplied the data source
    /// explicitly (<c>TraconPostgreSqlOptions.DataSource</c> and its SQL
    /// Server/SQLite counterparts): Tracon never disposes an instance it
    /// did not create — doing so would kill the consumer's own <c>DbContext</c>
    /// at host shutdown. Defaults to <see langword="true"/> so direct
    /// construction (test fixtures) keeps today's behavior.
    /// </remarks>
    public bool OwnsDataSource { get; init; } = true;

    /// <summary>Gets the gateway to the provider-specific behaviour.</summary>
    public required SqlDialect Dialect { get; init; }

    /// <summary>Gets the upper time limit of a single SQL command, in seconds. 0 means unlimited.</summary>
    public required int CommandTimeoutSeconds { get; init; }

    /// <summary>Gets a value indicating whether pending migrations are applied automatically at application start.</summary>
    public bool AutoApplyMigrations { get; init; } = true;

    /// <summary>
    /// Gets the names of the optional migration sets to apply, in addition to
    /// the core set that always applies. Empty by default.
    /// </summary>
    /// <remarks>
    /// Every name must be a key of <see cref="SqlDialect.OptionalMigrationResourcePrefixes"/>;
    /// an unknown name fails at startup, the first time <see cref="MigrationRunner"/> runs.
    /// </remarks>
    public IReadOnlySet<string> EnabledMigrationSets { get; init; } =
        System.Collections.Immutable.ImmutableHashSet<string>.Empty;

    /// <summary>Gets the name of the provider that built this context. It appears in log messages.</summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the additional key/value pairs that replace the <c>{non-schema}</c>
    /// placeholders in the migration text (for example <c>{dimension}</c> — the
    /// vector dimension).
    /// </summary>
    /// <remarks>
    /// The schema placeholder (<see cref="SqlQueriesBase.SchemaPlaceholder"/>) is
    /// always replaced separately and unconditionally; this dictionary is for the
    /// additional provider-specific values known AT SETUP TIME. When it is empty,
    /// no additional replacement happens.
    /// </remarks>
    public IReadOnlyDictionary<string, string> MigrationTemplateValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the SQL texts of this provider.</summary>
    public SqlQueriesBase Sql => Dialect.Queries;

    /// <summary>Gets the at-rest content protector.</summary>
    /// <remarks>
    /// <see langword="null"/> is treated the same as a no-op protector by
    /// <c>ProtectedValue</c> — the field is optional only because a handful
    /// of migration test fixtures build a <see cref="SqlStoreContext"/>
    /// directly, without going through <c>AddTracon()</c>. Every real
    /// <c>Use*</c> registration always resolves and sets it (<c>AddTracon()</c>
    /// registers a no-op default when the consumer never calls
    /// <c>AddContentProtection(...)</c>). See <see cref="ProtectedColumns"/> for
    /// which columns it is actually applied to.
    /// </remarks>
    public IContentProtector? ContentProtector { get; init; }

    /// <summary>
    /// Gets the columns <see cref="ContentProtector"/> is applied to on write.
    /// Empty when content protection is off — reads still run
    /// <see cref="IContentProtector.Unprotect"/>/<see cref="IContentProtector.UnprotectBytes"/>
    /// unconditionally, since those are self-describing and safe either way.
    /// </summary>
    public IReadOnlySet<ProtectedColumn> ProtectedColumns { get; init; } =
        System.Collections.Immutable.ImmutableHashSet<ProtectedColumn>.Empty;

    /// <summary>
    /// Gets the open transaction a store lent to the work it is awaiting in the current flow,
    /// or <see langword="null"/> when none is lent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The erasure of a data subject calls the audit write before it commits. On
    /// SQLite the erasure holds the database's single write lock
    /// (<see cref="SqlDialect.SerializesWriters"/>), so the audit write from a second
    /// connection waited for its own process until the command timed out, and the audit
    /// log's lock retry repeated that wait. The erasure now lends its transaction for the
    /// duration of the callback (<see cref="LendTransaction"/>).
    /// </para>
    /// <para>
    /// A store that reads this value runs its commands on the lent connection and
    /// transaction, so its writes commit or roll back with the work that lent them. Today
    /// only <see cref="SqlAuditLog"/> reads it; every other store keeps its own connection.
    /// </para>
    /// </remarks>
    public SqlLentTransaction? LentTransaction => _lentTransaction.Value;

    /// <summary>
    /// Lends an open transaction of this context to the work the caller awaits, until the
    /// returned scope is disposed.
    /// </summary>
    /// <param name="connection">The open connection the transaction runs on.</param>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The scope that ends the loan and puts back the previous value.</returns>
    /// <remarks>
    /// The value is an <see cref="AsyncLocal{T}"/>: it flows DOWN into the work the caller
    /// awaits, never back to the caller. Open the scope in the body of the method that
    /// awaits the work, not in an async helper. The work must not use the lent connection
    /// from two tasks at once.
    /// </remarks>
    public IDisposable LendTransaction(DbConnection connection, DbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        var previous = _lentTransaction.Value;
        _lentTransaction.Value = new SqlLentTransaction(connection, transaction);

        return new TransactionLoan(_lentTransaction, previous);
    }

    /// <summary>Creates a command with the configured timeout.</summary>
    /// <param name="sql">The command text.</param>
    /// <returns>A command ready to run.</returns>
    /// <remarks>
    /// <see cref="DbDataSource.CreateCommand(string)"/> manages the connection
    /// lifetime itself: it takes a connection from the pool when the command runs
    /// and returns it when the command is disposed.
    /// </remarks>
    public DbCommand CreateCommand(string sql)
    {
        var command = DataSource.CreateCommand(sql);
        command.CommandTimeout = CommandTimeoutSeconds;

        return command;
    }

    /// <summary>Creates a command over an existing connection and transaction.</summary>
    /// <param name="sql">The command text.</param>
    /// <param name="connection">The connection to use.</param>
    /// <param name="transaction">The transaction to use; <see langword="null"/> when there is none.</param>
    /// <returns>A command ready to run.</returns>
    public DbCommand CreateCommand(string sql, DbConnection connection, DbTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Transaction = transaction;

        return command;
    }

    /// <summary>Runs an upsert so that it is atomic on every provider.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="sql">The upsert text, one of the <c>Upsert*</c> members of <see cref="Sql"/>.</param>
    /// <param name="bind">Adds the parameters to the command.</param>
    /// <param name="execute">
    /// Runs the command and reads its result; one of the <see cref="DbHelpers"/>
    /// methods, which dispose the command.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value <paramref name="execute"/> returned.</returns>
    /// <remarks>
    /// <para>
    /// When <see cref="SqlDialect.UpsertRequiresTransaction"/> is <see langword="false"/>
    /// this is exactly <see cref="CreateCommand(string)"/> followed by
    /// <paramref name="execute"/>. Otherwise the command runs through
    /// <see cref="ExecuteInTransactionAsync{T}"/>: on its own connection, inside an
    /// ADO.NET <see cref="DbTransaction"/> (not a <c>BEGIN TRAN</c> in the text: the
    /// dialects classify the errors that flow through the transaction object), which
    /// holds the upsert's range lock until the commit.
    /// </para>
    /// <para>
    /// Either way an attempt the server picks as a deadlock victim is sent again,
    /// whole (see <see cref="ExecuteInTransactionAsync{T}"/>). <paramref name="bind"/>
    /// and <paramref name="execute"/> run once per attempt, on a new command.
    /// </para>
    /// </remarks>
    public ValueTask<T> ExecuteUpsertAsync<T>(
        string sql,
        Action<DbCommand> bind,
        Func<DbCommand, CancellationToken, ValueTask<T>> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ArgumentNullException.ThrowIfNull(bind);
        ArgumentNullException.ThrowIfNull(execute);

        if (!Dialect.UpsertRequiresTransaction)
        {
            // One statement in autocommit: a victim's statement was rolled back
            // whole, so sending it again is the same safe boundary as a transaction.
            return RetryDeadlockVictimAsync(
                async () =>
                {
                    var command = CreateCommand(sql);
                    bind(command);

                    return await execute(command, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken);
        }

        return ExecuteInTransactionAsync(
            async (connection, transaction) =>
            {
                var command = CreateCommand(sql, connection, transaction);
                bind(command);

                var result = await execute(command, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return result;
            },
            cancellationToken);
    }

    /// <summary>
    /// Runs a unit of work in a transaction of its own, and runs it again, whole, when
    /// the server picks it as a deadlock victim.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="work">
    /// Runs its commands on the connection and the transaction it is given and commits
    /// the transaction; returning without a commit rolls the work back. It runs once per
    /// attempt, so it must do nothing outside this database: no other store, no file,
    /// no call to another service.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The value <paramref name="work"/> returned on the attempt that finished.</returns>
    /// <remarks>
    /// <para>
    /// Every store write that needs more than one statement to be atomic goes through
    /// here (<c>SqlTransactionSiteTests</c> keeps it that way). An attempt the server
    /// picks as a deadlock victim (<see cref="SqlDialect.IsDeadlock"/>) is run again,
    /// on a new connection and a new transaction, up to <see cref="DeadlockAttempts"/>
    /// attempts in all; any other error, and the last victim's, propagates unchanged.
    /// </para>
    /// </remarks>
    public ValueTask<T> ExecuteInTransactionAsync<T>(
        Func<DbConnection, DbTransaction, ValueTask<T>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        return RetryDeadlockVictimAsync(
            async () =>
            {
                var connection = await DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                await using (connection.ConfigureAwait(false))
                {
                    var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                    await using (transaction.ConfigureAwait(false))
                    {
                        return await work(connection, transaction).ConfigureAwait(false);
                    }
                }
            },
            cancellationToken);
    }

    /// <summary>The result-less overload of <see cref="ExecuteInTransactionAsync{T}"/>.</summary>
    /// <param name="work">The unit of work; see <see cref="ExecuteInTransactionAsync{T}"/>.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion task.</returns>
    public async ValueTask ExecuteInTransactionAsync(
        Func<DbConnection, DbTransaction, ValueTask> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        _ = await ExecuteInTransactionAsync<bool>(
            async (connection, transaction) =>
            {
                await work(connection, transaction).ConfigureAwait(false);

                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets how many times a unit of work the server keeps picking as a deadlock
    /// victim is run, in all.
    /// </summary>
    internal const int DeadlockAttempts = 5;

    private async ValueTask<T> RetryDeadlockVictimAsync<T>(Func<ValueTask<T>> attempt, CancellationToken cancellationToken)
    {
        for (var number = 1; ; number++)
        {
            try
            {
                return await attempt().ConfigureAwait(false);
            }

            // 🚨 F-430: an upsert transaction holds a range lock until its commit
            // (F-313), so two of them can wait on each other and the server kills one.
            // Measured: RunScoreStoreContract's concurrent-write case failed once in a
            // full run with SqlException 1205 and passed alone. The retry wraps the
            // WHOLE attempt because that is the boundary that is safe to repeat: the
            // victim's transaction was rolled back, so nothing of it landed, and the
            // next attempt opens a new connection and a new transaction. A retry of one
            // statement inside it, or of a store call above it, would be neither. The
            // random pause keeps two victims from meeting again at once (K-540).
            catch (Exception exception) when (number < DeadlockAttempts && Dialect.IsDeadlock(exception))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(10, 40) * number), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Disposes <see cref="DataSource"/>, but only when <see cref="OwnsDataSource"/>
    /// is <see langword="true"/>.
    /// </summary>
    public void Dispose()
    {
        if (OwnsDataSource)
        {
            DataSource.Dispose();
        }
    }

    /// <summary>
    /// Disposes <see cref="DataSource"/> asynchronously, but only when
    /// <see cref="OwnsDataSource"/> is <see langword="true"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (OwnsDataSource)
        {
            await DataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Ends a loan made with <see cref="LendTransaction"/> and puts back the previous value.</summary>
    private sealed class TransactionLoan(AsyncLocal<SqlLentTransaction?> slot, SqlLentTransaction? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                slot.Value = previous;
            }
        }
    }
}
