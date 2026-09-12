using System.Data;
using System.Data.Common;

namespace Tracon;

/// <summary>
/// The single gateway that every provider-specific behaviour passes through.
/// </summary>
/// <remarks>
/// <para>
/// The store implementations under <c>Tracon.Sql.Shared</c> know only the ADO.NET
/// base types (<see cref="DbCommand"/>, <see cref="DbDataReader"/>, <see
/// cref="DbDataSource"/>). <strong>No</strong> shared file may reference the
/// <c>Npgsql</c> or <c>Microsoft.Data.SqlClient</c> namespace; all the differences are
/// collected in the derived types of this class.
/// </para>
/// <para>
/// The differences fall into three groups:
/// </para>
/// <list type="number">
/// <item>
/// <description> <strong>Parameter typing.</strong> Types such as <c>jsonb</c>, arrays and intervals have no common counterpart in ADO.NET. </description>
/// </item>
/// <item>
/// <description> <strong>Array transport format.</strong> PostgreSQL sends a native array (<c>unnest</c>); SQL Server sends JSON text (<c>OPENJSON</c>). The text difference stays inside the SQL and the C# flow is the same. </description>
/// </item>
/// <item>
/// <description> <strong>Migration lock.</strong> <c>pg_advisory_lock</c> and <c>sp_getapplock</c>. </description>
/// </item>
/// </list>
/// </remarks>
internal abstract class SqlDialect
{
    /// <summary>Gets the SQL texts of this provider.</summary>
    public abstract SqlQueriesBase Queries { get; }

    /// <summary>
    /// Gets the name prefix of the embedded migration resources.
    /// </summary>
    /// <remarks>
    /// Every provider has its own migration set and the numbering starts at
    /// <c>0001</c>. The numbers of two sets do <strong>not</strong> have to match.
    /// </remarks>
    public abstract string MigrationResourcePrefix { get; }

    /// <summary>
    /// Gets the optional migration sets this provider offers, keyed by set
    /// name, each value the embedded resource prefix for that set.
    /// </summary>
    /// <remarks>
    /// Empty by default: a provider opts in by overriding this. Today
    /// only PostgreSQL offers one ("knowledge") — it needs the
    /// <c>pgvector</c> extension and is therefore not part of the core set
    /// that every consumer pays for. <see cref="SqlStoreContext.EnabledMigrationSets"/>
    /// selects which of these actually apply.
    /// </remarks>
    public virtual IReadOnlyDictionary<string, string> OptionalMigrationResourcePrefixes { get; }
        = System.Collections.Immutable.ImmutableDictionary<string, string>.Empty;

    /// <summary>
    /// Gets the core-set sequence numbers that used to belong to a migration
    /// file later relocated into an optional set, mapped to the set it moved
    /// to.
    /// </summary>
    /// <remarks>
    /// Empty by default. A database that applied the migration back when it
    /// was still numbered under the core set carries a ledger row the current
    /// core file list no longer discovers; <see cref="MigrationRunner"/> uses
    /// this map to log (once, at information level) that the row is orphaned
    /// but harmless when the corresponding optional set stays disabled
    /// (decision 67.3 — no migration code, a diagnostic only).
    /// </remarks>
    public virtual IReadOnlyDictionary<int, string> RelocatedCoreMigrationSets { get; }
        = System.Collections.Immutable.ImmutableDictionary<int, string>.Empty;

    // --- Migration lock ---

    /// <summary>Acquires the migration lock.</summary>
    /// <param name="connection">The connection the lock is held on.</param>
    /// <param name="commandTimeout">The command timeout, in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the lock is acquired.</returns>
    /// <remarks>
    /// The lock is <em>session scoped</em>; every migration step therefore runs over
    /// the same connection.
    /// </remarks>
    public abstract ValueTask AcquireMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken);

    /// <summary>Releases the migration lock.</summary>
    /// <param name="connection">The connection the lock is held on.</param>
    /// <param name="commandTimeout">The command timeout, in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the lock is released.</returns>
    public abstract ValueTask ReleaseMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Upgrades an existing ledger table created before optional sets existed (no
    /// <c>set_name</c> column, a single-column primary key) to the shape a
    /// fresh database now gets directly from <see cref="SqlQueriesBase.CreateMigrationsTable"/>.
    /// </summary>
    /// <param name="connection">The connection to run on. Runs OUTSIDE any migration's own transaction (see remarks).</param>
    /// <param name="commandTimeout">The command timeout, in seconds.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <remarks>
    /// <para>
    /// Called once per <see cref="MigrationRunner.ApplyAsync"/>, right after
    /// <see cref="SqlQueriesBase.CreateMigrationsTable"/> and BEFORE any
    /// migration applies. It must be idempotent — safe to run on every
    /// startup, including a fresh database that already has the target shape.
    /// </para>
    /// <para>
    /// This intentionally runs as ITS OWN command, never combined with a
    /// migration's <c>InsertMigration</c> text. SQL Server compiles a whole
    /// batch up front; a statement that references a column ADDED earlier IN
    /// THE SAME BATCH via plain <c>ALTER TABLE</c> fails with "Invalid column
    /// name" (the same trap documented on PostgreSQL 0031/SqlServer
    /// 0018_audit_chain.sql) — and <c>InsertMigration</c>'s fixed text
    /// references <c>set_name</c> on <em>every</em> migration insert from
    /// onward. Running the upgrade as a separate, already-completed
    /// command before the per-migration loop starts means the column exists
    /// in the catalog by the time any <c>InsertMigration</c> text is compiled.
    /// </para>
    /// <para>
    /// The default implementation runs <see cref="SqlQueriesBase.UpgradeMigrationsTable"/>
    /// as plain SQL when it is not empty (PostgreSQL, SQL Server: a single
    /// idempotent statement suffices). SQLite cannot express "add this column
    /// only if missing" or change a primary key in a static SQL string at
    /// all — <c>SqliteDialect</c> overrides this method with the check +
    /// rebuild done in code (the same technique/0006_sessions_tenant_key.sql).
    /// </para>
    /// </remarks>
    public virtual async ValueTask UpgradeMigrationsTableAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (Queries.UpgradeMigrationsTable.Length == 0)
        {
            return;
        }

        var command = connection.CreateCommand();
        command.CommandText = Queries.UpgradeMigrationsTable;
        command.CommandTimeout = commandTimeout;

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Converts a provider-specific error raised while a migration is applied into
    /// an understandable <see cref="TraconException"/> message.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns>
    /// The description text when it is a database error of this provider; otherwise
    /// <see langword="null"/> (the exception is rethrown).
    /// </returns>
    public abstract string? DescribeDatabaseError(Exception exception);

    /// <summary>Determines whether the exception is a unique constraint violation.</summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    /// <remarks>
    /// PostgreSQL gives SQLSTATE <c>23505</c>; SQL Server uses error numbers 2601
    /// and 2627. The stores do not see this difference.
    /// </remarks>
    public abstract bool IsUniqueViolation(Exception exception);

    /// <summary>Determines whether the exception is a foreign key constraint violation.</summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    /// <remarks>
    /// PostgreSQL gives SQLSTATE <c>23503</c>; SQL Server uses error number 547.
    /// </remarks>
    public abstract bool IsForeignKeyViolation(Exception exception);

    /// <summary>Determines whether the exception is a deadlock the server resolved by killing this transaction.</summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    /// <remarks>
    /// A deadlock victim is TRANSIENT: the server rolled the transaction back and
    /// the same statement succeeds when it is sent again. SQL Server reports error
    /// 1205; PostgreSQL gives SQLSTATE <c>40P01</c>; SQLite serializes writers and
    /// reports <c>SQLITE_BUSY</c>/<c>SQLITE_LOCKED</c> instead of detecting a cycle.
    /// Only callers whose work is safe to repeat may retry on this — see
    /// <c>MigrationRunner.ApplyOneAsync</c>, whose DDL is "IF NOT EXISTS"-guarded.
    /// </remarks>
    public abstract bool IsDeadlock(Exception exception);

    /// <summary>
    /// Determines whether the exception comes from this provider treating a regular
    /// expression pushed down to the server as invalid.
    /// </summary>
    /// <param name="exception">The caught exception.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    /// <remarks>
    /// Only PostgreSQL uses the <c>~</c> operator as a prefilter (item A);
    /// the <see cref="System.Text.RegularExpressions.Regex"/> syntax of .NET is
    /// richer than the ARE syntax of PostgreSQL (named groups, for example). When
    /// such a pattern is sent to the server, <see cref="SqlAgentFileStore"/> detects
    /// it with this method and retries WITHOUT the prefilter; the final match is
    /// always done on the client with the .NET <c>Regex</c>, so the behaviour does
    /// not change. SQL Server and SQLite never take this path and always return
    /// <see langword="false"/>.
    /// </remarks>
    public abstract bool IsInvalidRegexError(Exception exception);

    // --- Provider-specific parameter typing ---

    /// <summary>Binds a JSON text to a parameter for a <c>json</c> column.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The JSON text; it can be <see langword="null"/>.</param>
    /// <remarks>
    /// On PostgreSQL the distinction between <c>json</c> and <c>jsonb</c> matters:
    /// <c>jsonb</c> reorders the object keys and breaks the polymorphic <c>$type</c>
    /// discriminator. On SQL Server both are <c>nvarchar(max)</c>
    /// and the order is preserved anyway; the distinction is inert there but
    /// <em>harmless</em> — the shared code uses a single contract.
    /// </remarks>
    public abstract void AddJson(DbCommand command, string name, string? value);

    /// <summary>Binds a JSON text to a parameter for a <c>jsonb</c> column.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The JSON text; it can be <see langword="null"/>.</param>
    public abstract void AddJsonb(DbCommand command, string name, string? value);

    /// <summary>Binds a text array to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="values">The array; it can be <see langword="null"/>.</param>
    public abstract void AddTextArray(DbCommand command, string name, IReadOnlyList<string>? values);

    /// <summary>Binds an identifier array to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="values">The array; it can be <see langword="null"/>.</param>
    public abstract void AddUuidArray(DbCommand command, string name, IReadOnlyList<Guid>? values);

    /// <summary>Binds a time interval to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The interval.</param>
    public abstract void AddInterval(DbCommand command, string name, TimeSpan value);

    /// <summary>Reads a text array column.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The array; an empty array when the column is <c>NULL</c>.</returns>
    public abstract IReadOnlyList<string> ReadTextArray(DbDataReader reader, int ordinal);

    /// <summary>Qualifies a bare table name with the schema/prefix rule of this provider.</summary>
    /// <param name="tableName">The table name without schema or prefix (for example <c>"sessions"</c>).</param>
    /// <returns>The qualified name, ready to embed into runnable SQL.</returns>
    /// <remarks>
    /// PostgreSQL and SQL Server use <c>{schema}.{table}</c> (with a dot); on SQLite
    /// the object names share a single namespace across the database, so the prefix
    /// is concatenated directly and there is NO dot. Provider-independent SQL
    /// generation such as <see cref="RetentionTargetRegistry"/> uses this.
    /// </remarks>
    public virtual string QualifyTable(string tableName) => Queries.QualifyTable(tableName);

    // --- Retention (phase 25): data plane batch queries ---

    /// <summary>
    /// Builds the SQL text that returns the number of rows in a target matching
    /// <paramref name="wherePredicate"/>.
    /// </summary>
    /// <param name="table">The schema-prefixed table name.</param>
    /// <param name="wherePredicate">The SQL condition that refers to <c>@cutoff</c>.</param>
    /// <returns>Runnable SQL. Single parameter: <c>@cutoff</c>.</returns>
    /// <remarks>
    /// This one is identical across the providers (just <c>COUNT(*)</c>); it still
    /// goes through the dialect, because the text produced by
    /// <see cref="RetentionTargetRegistry"/> is provider-INDEPENDENT and under
    /// the no-surprises rule, the dialect is the single gateway for all SQL text.
    /// </remarks>
    public abstract string BuildRetentionCountSql(string table, string wherePredicate);

    /// <summary>
    /// Builds the SQL text that reads one batch of rows matching
    /// <paramref name="wherePredicate"/> (in order to archive them). It does not
    /// delete.
    /// </summary>
    /// <param name="table">The schema-prefixed table name.</param>
    /// <param name="wherePredicate">The SQL condition that refers to <c>@cutoff</c>.</param>
    /// <param name="orderColumn">The ordering column used for determinism.</param>
    /// <returns>Runnable SQL. Parameters: <c>@cutoff</c>, <c>@batchSize</c>.</returns>
    public abstract string BuildRetentionArchiveSelectSql(string table, string wherePredicate, string orderColumn);

    /// <summary>
    /// Builds the SQL text that deletes one batch of rows matching
    /// <paramref name="wherePredicate"/>. It is NOT a single bulk <c>DELETE</c>.
    /// </summary>
    /// <param name="table">The schema-prefixed table name.</param>
    /// <param name="wherePredicate">The SQL condition that refers to <c>@cutoff</c>.</param>
    /// <returns>Runnable SQL. Parameters: <c>@cutoff</c>, <c>@batchSize</c>.</returns>
    /// <remarks>
    /// The three providers use three different techniques: PostgreSQL a <c>ctid</c>
    /// subquery, SQL Server <c>DELETE TOP (n)</c>, SQLite a <c>rowid</c> subquery.
    /// None of them guarantees that the rows are deleted in a particular order — the
    /// order of the batch does not matter, only its size does.
    /// </remarks>
    public abstract string BuildRetentionDeleteBatchSql(string table, string wherePredicate);

    /// <summary>
    /// Builds the SQL text that returns the value of the ordering expression for the
    /// Nth row counted from the newest (<c>MaxRows</c>).
    /// </summary>
    /// <param name="table">The schema-prefixed table name.</param>
    /// <param name="orderExpression">
    /// The SQL expression used for counting and ordering (see the
    /// <c>RowLimitOrderExpression</c> produced by
    /// <see cref="RetentionTargetRegistry.Resolve"/>). NULL values are filtered out.
    /// </param>
    /// <param name="extraPredicate">
    /// An additional condition such as the tenant filter; when it is
    /// <see langword="null"/> the query runs over all rows.
    /// </param>
    /// <returns>Runnable SQL. Parameters: <c>@n</c> (bigint) and, when present, <c>@tenant_id</c>.</returns>
    /// <remarks>
    /// The caller feeds the single returned value straight into the other three
    /// templates (count/read/delete) as <c>@cutoff</c> — volume-based trimming uses
    /// THE SAME batch mechanism as age-based deletion (36.1).
    /// </remarks>
    public abstract string BuildRetentionFindNthRowCutoffSql(string table, string orderExpression, string? extraPredicate);

    /// <summary>
    /// Converts the given condition into a fragment that can be appended with <c>AND</c>.
    /// </summary>
    /// <param name="predicate">The additional condition; when it is empty nothing is appended.</param>
    /// <returns>An empty string or <c>" AND (condition)"</c>.</returns>
    protected static string AndAlso(string? predicate)
        => string.IsNullOrWhiteSpace(predicate) ? string.Empty : $" AND ({predicate})";

    /// <summary>
    /// Combines two conditions with <c>AND</c>.
    /// </summary>
    /// <param name="predicate">The required condition.</param>
    /// <param name="extraPredicate">The additional condition; it can be empty.</param>
    /// <returns>The combined condition.</returns>
    public static string Combine(string predicate, string? extraPredicate)
        => string.IsNullOrWhiteSpace(extraPredicate) ? predicate : $"({predicate}) AND ({extraPredicate})";

    // --- Data subject export/erasure (phase 64) ---

    /// <summary>
    /// Builds a fragment that is true when <paramref name="column"/>'s value is one
    /// of the values bound to the array parameter <paramref name="paramName"/>
    /// (bound with <see cref="AddTextArray"/> or <see cref="AddUuidArray"/>).
    /// </summary>
    /// <param name="column">The column to test, already schema-qualified if needed.</param>
    /// <param name="paramName">The array parameter's name, WITHOUT the leading <c>@</c>.</param>
    /// <returns>A boolean SQL fragment, safe to combine with <c>AND</c>/<c>OR</c>.</returns>
    /// <remarks>
    /// PostgreSQL has a native array type and uses <c>= ANY(@array)</c>; SQL Server
    /// and SQLite receive the array as JSON text and test membership with
    /// <c>OPENJSON</c>/<c>json_each</c> — the mirror image of the <c>= ANY(events)</c>
    /// pattern already used elsewhere (matching a stored JSON array against one
    /// scalar parameter), with which value is the array and which is scalar swapped.
    /// </remarks>
    public abstract string ArrayContains(string column, string paramName);

    /// <summary>
    /// Builds the SQL text that reads the given columns of the rows matching
    /// <paramref name="wherePredicate"/>
    /// .
    /// </summary>
    /// <param name="table">The schema-prefixed table name.</param>
    /// <param name="columns">The column list (<c>"*"</c> for every column; see <see cref="DataSubjectTargetRegistry"/>).</param>
    /// <param name="wherePredicate">The <c>WHERE</c> condition.</param>
    /// <returns>Runnable SQL.</returns>
    /// <remarks>
    /// Identical across all three providers — there is no provider-specific
    /// batching or cursor concern here, unlike the retention read,
    /// because an export runs once for one data subject, not batch by batch over
    /// an entire table. It still passes through the dialect so every runnable SQL
    /// string has the same single gateway.
    /// </remarks>
    public virtual string BuildDataSubjectSelectSql(string table, string columns, string wherePredicate)
        => $"SELECT {columns} FROM {table} WHERE {wherePredicate};";

    /// <summary>Builds the SQL text that deletes every row matching <paramref name="wherePredicate"/>.</summary>
    /// <param name="table">The schema-prefixed table name.</param>
    /// <param name="wherePredicate">The <c>WHERE</c> condition.</param>
    /// <returns>Runnable SQL.</returns>
    /// <remarks>
    /// A single unbounded <c>DELETE</c>: a data subject's own rows are never large
    /// enough to need retention's batch loop.
    /// </remarks>
    public virtual string BuildDataSubjectDeleteSql(string table, string wherePredicate)
        => $"DELETE FROM {table} WHERE {wherePredicate};";

    // --- Common typings (overridden in a derived type when needed) ---

    /// <summary>Binds a timestamp to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The timestamp; it can be <see langword="null"/>.</param>
    /// <remarks>
    /// The value is always converted to UTC before it is written. PostgreSQL expects
    /// a <see cref="DateTime"/> (<c>Kind = Utc</c>) for <c>timestamptz</c>; SQL
    /// Server takes a <see cref="DateTimeOffset"/> for <c>datetimeoffset</c>. The
    /// conversion lives in the derived types.
    /// </remarks>
    public abstract void AddTimestamp(DbCommand command, string name, DateTimeOffset? value);

    /// <summary>Binds a nullable text to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    public virtual void AddText(DbCommand command, string name, string? value)
        => AddTyped(command, name, DbType.String, value);

    /// <summary>Binds a nullable identifier to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    public virtual void AddUuid(DbCommand command, string name, Guid? value)
        => AddTyped(command, name, DbType.Guid, value);

    /// <summary>Binds a nullable <c>smallint</c> value to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    public virtual void AddInt16(DbCommand command, string name, short? value)
        => AddTyped(command, name, DbType.Int16, value);

    /// <summary>Binds a nullable <c>integer</c> value to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    public virtual void AddInt32(DbCommand command, string name, int? value)
        => AddTyped(command, name, DbType.Int32, value);

    /// <summary>Binds a nullable <c>bigint</c> value to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    public virtual void AddInt64(DbCommand command, string name, long? value)
        => AddTyped(command, name, DbType.Int64, value);

    /// <summary>Binds a nullable double-precision value to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    /// <remarks>
    /// For a measured quantity that is not money. Money uses
    /// <see cref="AddDecimal"/>: SQL Server needs an explicit precision/scale
    /// there, and a binary float would round it.
    /// </remarks>
    public virtual void AddDouble(DbCommand command, string name, double? value)
        => AddTyped(command, name, DbType.Double, value);

    /// <summary>Binds a nullable decimal value to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    public virtual void AddDecimal(DbCommand command, string name, decimal? value)
        => AddTyped(command, name, DbType.Decimal, value);

    /// <summary>Binds nullable binary data to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    public virtual void AddBinary(DbCommand command, string name, byte[]? value)
        => AddTyped(command, name, DbType.Binary, value);

    /// <summary>Binds a boolean value to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value.</param>
    public virtual void AddBoolean(DbCommand command, string name, bool value)
        => AddTyped(command, name, DbType.Boolean, value);

    /// <summary>Binds a nullable boolean value to a parameter.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The value; it can be <see langword="null"/>.</param>
    /// <remarks>
    /// It is for a three-state field ("yes" / "no" / "unknown"). Do not confuse it
    /// with <c>AddBoolean</c>: there a <see langword="null"/> cannot be written and
    /// missing information would silently become <see langword="false"/>.
    /// </remarks>
    public virtual void AddNullableBoolean(DbCommand command, string name, bool? value)
        => AddTyped(command, name, DbType.Boolean, value);

    /// <summary>
    /// Adds a parameter with the given type.
    /// </summary>
    /// <param name="command">
    /// The command.
    /// </param>
    /// <param name="name">
    /// The parameter name.
    /// </param>
    /// <param name="type">
    /// The parameter type.
    /// </param>
    /// <param name="value">
    /// The value; when it is <see langword="null"/>, <see cref="DBNull"/> is written.
    /// </param>
    /// <returns>
    /// The added parameter.
    /// </returns>
    /// <remarks>
    /// Optional filter parameters (the <c>@p IS NULL OR col = @p</c> pattern) must
    /// <strong>always</strong> be typed explicitly. When an untyped <c>NULL</c> is
    /// sent, PostgreSQL cannot infer the type and gives <c>42P08</c>; the error appears
    /// at run time only.
    /// </remarks>
    protected static DbParameter AddTyped(DbCommand command, string name, DbType type, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);

        return parameter;
    }
}
