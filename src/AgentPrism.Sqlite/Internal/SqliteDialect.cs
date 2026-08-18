using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AgentPrism;

/// <summary>
/// <see cref="SqlDialect"/> abstraction's SQLite implementation.
/// </summary>
/// <remarks>
/// <para>
/// This is the only <c>Microsoft.Data.Sqlite</c> contact point the shared store code sees.
/// </para>
/// <para>
/// Three points were measured and the behavior here was built on them (Phase 24 opening):
/// </para>
/// <list type="number">
///   <item><description>
///     <strong><c>uuid</c> is written UPPERCASE; no special handling is REQUIRED.</strong>
///     Without a <c>Microsoft.Data.Sqlite</c> type given (as <see cref="DbHelpers.Add"/> does)
///     or with <see cref="DbType.Guid"/> (the base class <see cref="SqlDialect.AddUuid"/>
///     default), it writes the IDENTICAL uppercase, hyphenated text — measured. This is why
///     <c>AddUuid</c> is NOT overridden here specifically: lowercasing it would make
///     non-nullable Guids that pass through <see cref="DbHelpers.Add"/> write UPPERCASE and
///     nullable Guids that pass through this path write lowercase. The same logical identity
///     would then be stored with two different texts, and <c>WHERE</c>/<c>JOIN</c> equality
///     (SQLite's default text comparison is BINARY, i.e. case-sensitive) would fail SILENTLY
///     (example: <c>SqlTraceStore.UpsertTraceAsync</c> writes with <c>Dialect.AddUuid</c>,
///     <c>GetTraceByRunAsync</c> reads with <c>DbHelpers.Add</c> — the same <c>run_id</c>
///     column). As long as casing stays consistent, uuid v7's time-ordered prefix keeps its
///     lexicographic order.
///   </description></item>
///   <item><description>
///     <strong>Timestamp.</strong> <c>Microsoft.Data.Sqlite</c>'s default <see cref="DateTimeOffset"/>
///     write format (space-separated, microsecond precision) is NOT lexicographically
///     time-ordered; this is why <c>AddTimestamp</c> formats it manually
///     (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>). Timestamps ALWAYS pass through
///     <see cref="SqlDialect.AddTimestamp"/>, a single path — the dual-path problem that
///     affects uuid does not exist here.
///   </description></item>
///   <item><description>
///     <strong>No special handling is REQUIRED for <c>decimal</c>.</strong> Even without a
///     driver type given (or with <see cref="DbType.Decimal"/>), it always writes as TEXT and
///     is culture-independent; there is no round-trip through <c>REAL</c>. Measured:
///     <c>0.1m + 0.2m</c> stayed exactly <c>0.3m</c> after a round trip.
///   </description></item>
///   <item><description>
///     <strong>The migration lock is file-based.</strong> SQLite has no equivalent of
///     <c>pg_advisory_lock</c>/<c>sp_getapplock</c>, and holding <c>BEGIN IMMEDIATE</c> open
///     for the entire migration duration CONFLICTS with <see cref="MigrationRunner"/>'s own
///     nested transactions (<c>Microsoft.Data.Sqlite</c> does not support nested transactions).
///     A sidecar file lock (<c>&lt;database&gt;.&lt;prefix&gt;.agentprism-migration-lock</c>)
///     gives the same protection without touching the connection's transaction state at all.
///     The lock file is scoped to the TABLE PREFIX (K-389): SQLite has no schema concept, the
///     table prefix is the equivalent that separates AgentPrism installations, and the lock
///     must be scoped the same way. Skipped for <c>:memory:</c> databases.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqliteDialect : SqlDialect, IDisposable
{
    /// <summary><c>SQLITE_CONSTRAINT_UNIQUE</c> extended error code.</summary>
    private const int UniqueConstraint = 2067;

    /// <summary><c>SQLITE_CONSTRAINT_PRIMARYKEY</c> extended error code.</summary>
    private const int PrimaryKeyConstraint = 1555;

    /// <summary><c>SQLITE_CONSTRAINT_FOREIGNKEY</c> extended error code.</summary>
    private const int ForeignKeyConstraint = 787;

    /// <summary>Suffix appended to the database file name for the migration lock file.</summary>
    private const string LockFileSuffix = ".agentprism-migration-lock";

    /// <summary>Lock retry interval (milliseconds).</summary>
    private const int PollIntervalMilliseconds = 100;

    /// <summary>Default lock wait duration (seconds) used when <c>commandTimeout</c> is 0 (unlimited).</summary>
    private const int DefaultLockWaitSeconds = 30;

    private readonly SqliteQueries _queries;
    private readonly string _tablePrefix;

    private FileStream? _lockFile;

    /// <summary>Creates a new SQLite dialect.</summary>
    /// <param name="tablePrefix">The table prefix to validate.</param>
    public SqliteDialect(string tablePrefix)
    {
        _queries = new SqliteQueries(tablePrefix);
        _tablePrefix = _queries.Schema;
    }

    /// <inheritdoc />
    public override SqlQueriesBase Queries => _queries;

    /// <inheritdoc />
    public override string MigrationResourcePrefix => "AgentPrism.Sqlite.Migrations.";

    /// <inheritdoc />
    /// <remarks>
    /// Opens a lock file next to the SQLite file (<see cref="FileShare.None"/>); a second
    /// process cannot open the same file and waits, polling, for up to
    /// <paramref name="commandTimeout"/> seconds. Skipped for <c>:memory:</c> databases
    /// (<see cref="SqliteConnection.DataSource"/> empty): another process cannot share the
    /// same in-memory database anyway.
    /// </remarks>
    public override async ValueTask AcquireMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var dataSource = ((SqliteConnection)connection).DataSource;

        if (string.IsNullOrEmpty(dataSource))
        {
            return;
        }

        var lockPath = dataSource + "." + _tablePrefix + LockFileSuffix;
        var waitSeconds = commandTimeout > 0 ? commandTimeout : DefaultLockWaitSeconds;
        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);

        while (true)
        {
            try
            {
                _lockFile = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(PollIntervalMilliseconds, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new AgentPrismException(
                    $"Could not acquire the AgentPrism migration lock: '{lockPath}' is in use by " +
                    $"another process. The lock is waited on for at most {waitSeconds} seconds; another " +
                    "instance may be applying a long-running migration.",
                    ex);
            }
        }
    }

    /// <inheritdoc />
    public override ValueTask ReleaseMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        _lockFile?.Dispose();
        _lockFile = null;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string? DescribeDatabaseError(Exception exception)
        => exception is SqliteException sql
            ? $"{sql.Message.TrimEnd()} (error {sql.SqliteErrorCode}, extended {sql.SqliteExtendedErrorCode})"
            : null;

    /// <inheritdoc />
    /// <remarks>
    /// SQLite returns <c>SQLITE_CONSTRAINT</c> (19) as the base error code for both UNIQUE and
    /// PRIMARY KEY violations; the distinguishing information is in the EXTENDED code.
    /// </remarks>
    public override bool IsUniqueViolation(Exception exception)
        => exception is SqliteException sql
            && sql.SqliteExtendedErrorCode is UniqueConstraint or PrimaryKeyConstraint;

    /// <inheritdoc />
    public override bool IsForeignKeyViolation(Exception exception)
        => exception is SqliteException sql && sql.SqliteExtendedErrorCode == ForeignKeyConstraint;

    /// <inheritdoc />
    /// <remarks>SQLite never sends the regular expression to a server; this path is never hit.</remarks>
    public override bool IsInvalidRegexError(Exception exception) => false;

    /// <inheritdoc />
    public override void AddJson(DbCommand command, string name, string? value)
        => AddTyped(command, name, DbType.String, value);

    /// <inheritdoc />
    public override void AddJsonb(DbCommand command, string name, string? value)
        => AddTyped(command, name, DbType.String, value);

    /// <inheritdoc />
    public override void AddTextArray(DbCommand command, string name, IReadOnlyList<string>? values)
        => AddTyped(
            command,
            name,
            DbType.String,
            values is null
                ? null
                : JsonSerializer.Serialize(
                    values.ToArray(),
                    AgentPrismJsonContext.Default.StringArray));

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 Serialized through <see cref="AgentPrismJsonContext.StringArray"/> as
    /// UPPERCASE text, NOT through the <c>GuidArray</c> converter's default
    /// (lowercase) formatting. Measured: every scalar Guid parameter in this
    /// dialect writes UPPERCASE text (see <see cref="SqlDialect.AddUuid"/>'s
    /// remarks on this type, K-191), but <c>System.Text.Json</c>'s built-in
    /// <see cref="Guid"/> converter always writes LOWERCASE — comparing a
    /// lowercase array element against an uppercase stored id inside
    /// <see cref="ArrayContains"/> then fails for every id containing an a-f
    /// hex digit, SILENTLY (SQLite text comparison is case-sensitive), and
    /// only for SOME rows depending on which hex digits their id happens to
    /// contain, which read as flaky test failures before the pattern was clear.
    /// </remarks>
    public override void AddUuidArray(DbCommand command, string name, IReadOnlyList<Guid>? values)
        => AddTyped(
            command,
            name,
            DbType.String,
            values is null
                ? null
                : JsonSerializer.Serialize(
                    values.Select(static value => value.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant()).ToArray(),
                    AgentPrismJsonContext.Default.StringArray));

    /// <inheritdoc />
    /// <remarks>
    /// SQLite has no <c>interval</c> type. The time-series query derives bucket width from
    /// the <c>@bucket_unit</c> text; this parameter is only sent in minutes to satisfy the
    /// shared signature (same pattern as SQL Server).
    /// </remarks>
    public override void AddInterval(DbCommand command, string name, TimeSpan value)
        => AddTyped(command, name, DbType.Int32, (int)value.TotalMinutes);

    /// <inheritdoc />
    public override string ArrayContains(string column, string paramName)
        => $"EXISTS (SELECT 1 FROM json_each(@{paramName}) WHERE value = {column})";

    /// <inheritdoc />
    public override IReadOnlyList<string> ReadTextArray(DbDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (reader.IsDBNull(ordinal))
        {
            return [];
        }

        return JsonSerializer.Deserialize(
            reader.GetString(ordinal),
            AgentPrismJsonContext.Default.StringArray) ?? [];
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 <c>Microsoft.Data.Sqlite</c>'s default <see cref="DateTimeOffset"/> format
    /// (<c>2026-08-05 06:41:38.390129+00:00</c>) is NOT lexicographically time-ordered
    /// (space separator, six fractional digits). The format is fixed manually:
    /// <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>, always converted to UTC.
    /// </remarks>
    public override void AddTimestamp(DbCommand command, string name, DateTimeOffset? value)
        => AddTyped(
            command,
            name,
            DbType.String,
            value?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));

    /// <inheritdoc />
    /// <remarks>
    /// SQLite shares a single object namespace across the whole database; there is no schema,
    /// the prefix is prepended directly to the table name (no dot). Rationale:
    /// <c>docs/hafiza/sql-saglayicilari.md</c>, K-193.
    /// </remarks>
    public override string QualifyTable(string tableName) => $"{Queries.Schema}{tableName}";

    /// <inheritdoc />
    public override string BuildRetentionCountSql(string table, string wherePredicate)
        => $"SELECT COUNT(*) FROM {table} WHERE {wherePredicate};";

    /// <inheritdoc />
    public override string BuildRetentionArchiveSelectSql(string table, string wherePredicate, string orderColumn)
        => $"""
            SELECT *
            FROM {table}
            WHERE {wherePredicate}
            ORDER BY {orderColumn}
            LIMIT @batchSize;
            """;

    /// <inheritdoc />
    /// <remarks>
    /// SQLite does not support <c>DELETE ... LIMIT</c> in the default build
    /// (<c>SQLITE_ENABLE_UPDATE_DELETE_LIMIT</c> is required); a <c>rowid</c> subquery is used
    /// for the same reason as PostgreSQL's <c>ctid</c> pattern.
    /// </remarks>
    public override string BuildRetentionDeleteBatchSql(string table, string wherePredicate)
        => $"""
            DELETE FROM {table}
             WHERE rowid IN (
                   SELECT rowid FROM {table}
                    WHERE {wherePredicate}
                    LIMIT @batchSize);
            """;

    /// <inheritdoc />
    public override string BuildRetentionFindNthRowCutoffSql(
        string table,
        string orderExpression,
        string? extraPredicate)
        => $"""
            SELECT {orderExpression}
            FROM {table}
            WHERE {orderExpression} IS NOT NULL{AndAlso(extraPredicate)}
            ORDER BY {orderExpression} DESC
            LIMIT 1 OFFSET @n - 1;
            """;

    /// <summary>Releases the migration lock if it is still open.</summary>
    public void Dispose()
    {
        _lockFile?.Dispose();
        _lockFile = null;
    }
}
