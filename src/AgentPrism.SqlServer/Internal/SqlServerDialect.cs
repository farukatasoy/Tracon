using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AgentPrism;

/// <summary>
/// The SQL Server implementation of the <see cref="SqlDialect"/> abstraction.
/// </summary>
/// <remarks>
/// <para>
/// This is the only <c>Microsoft.Data.SqlClient</c> touch point the shared
/// store code sees.
/// </para>
/// <para>
/// Two traps are closed here:
/// </para>
/// <list type="number">
///   <item><description>
///     🚨 <strong>Decimal truncation.</strong> SQL Server treats an untyped
///     <see cref="decimal"/> parameter as <c>decimal(18,0)</c> and
///     <em>silently drops</em> the fractional part — money amounts would be
///     rounded to whole numbers. Every decimal column is <c>decimal(20,10)</c>,
///     and the same precision is written to the parameter explicitly.
///   </description></item>
///   <item><description>
///     <strong>Array transport.</strong> SQL Server has no array parameter;
///     arrays are sent as JSON text and opened on the SQL side with
///     <c>OPENJSON</c>. The corresponding read also parses JSON.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqlServerDialect : SqlDialect
{
    /// <summary>Precision of every decimal column (<c>decimal(20,10)</c>).</summary>
    private const byte DecimalPrecision = 20;

    /// <summary>Scale of every decimal column (<c>decimal(20,10)</c>).</summary>
    private const byte DecimalScale = 10;

    /// <summary>Error numbers for a uniqueness constraint violation.</summary>
    /// <remarks>2601 is for a unique index, 2627 is for a unique constraint.</remarks>
    private static readonly int[] UniqueViolations = [2601, 2627];

    /// <summary>Error number for a foreign key constraint violation.</summary>
    private const int ForeignKeyViolation = 547;

    /// <summary>The resource name prefix for the migration lock.</summary>
    /// <remarks>
    /// The value is specific to AgentPrism and <strong>must not change</strong>.
    /// The lock is SCOPED TO THE SCHEMA (K-389): <c>SchemaName</c> exists so
    /// two independent AgentPrism deployments can share one database, and the
    /// resource the lock protects is exactly the schema — locking both under
    /// the same name would create an unnecessary startup dependency between
    /// independent deployments. In a rolling upgrade, if an old replica
    /// starts before this name change, the worst outcome is that single
    /// replica hitting the <c>__migrations</c> uniqueness constraint and
    /// restarting; the schema is not corrupted (migrations run in their own
    /// transaction).
    /// </remarks>
    private const string MigrationLockResourcePrefix = "AgentPrism.Migrations:";

    /// <summary>The upper wait time for the lock (milliseconds).</summary>
    private const int LockTimeoutMilliseconds = 30000;

    private readonly SqlServerQueries _queries;
    private readonly string _migrationLockResource;

    /// <summary>Creates a new SQL Server dialect.</summary>
    /// <param name="schemaName">The schema name to validate.</param>
    public SqlServerDialect(string schemaName)
    {
        _queries = new SqlServerQueries(schemaName);
        _migrationLockResource = MigrationLockResourcePrefix + _queries.Schema;
    }

    /// <inheritdoc />
    public override SqlQueriesBase Queries => _queries;

    /// <inheritdoc />
    public override string MigrationResourcePrefix => "AgentPrism.SqlServer.Migrations.";

    /// <inheritdoc />
    /// <remarks>
    /// <c>sp_getapplock</c> is taken at session scope. A negative return value
    /// means the lock was not acquired; silently continuing would let two
    /// replicas apply the same migration at the same time.
    /// </remarks>
    public override async ValueTask AcquireMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = "sys.sp_getapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = commandTimeout;

        AddTyped(command, "@Resource", DbType.String, _migrationLockResource);
        AddTyped(command, "@LockMode", DbType.String, "Exclusive");
        AddTyped(command, "@LockOwner", DbType.String, "Session");
        AddTyped(command, "@LockTimeout", DbType.Int32, LockTimeoutMilliseconds);

        var result = command.CreateParameter();
        result.ParameterName = "@Result";
        result.DbType = DbType.Int32;
        result.Direction = ParameterDirection.ReturnValue;
        command.Parameters.Add(result);

        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (result.Value is int code && code < 0)
            {
                throw new AgentPrismException(
                    $"Could not acquire the AgentPrism migration lock (sp_getapplock returned {code}). " +
                    $"The lock is waited on for at most {LockTimeoutMilliseconds} ms; another instance may " +
                    "be applying a long-running migration.");
            }
        }
    }

    /// <inheritdoc />
    public override async ValueTask ReleaseMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = "sys.sp_releaseapplock";
        command.CommandType = CommandType.StoredProcedure;
        command.CommandTimeout = commandTimeout;

        AddTyped(command, "@Resource", DbType.String, _migrationLockResource);
        AddTyped(command, "@LockOwner", DbType.String, "Session");

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string? DescribeDatabaseError(Exception exception)
        => exception is SqlException sql
            ? $"{sql.Message.TrimEnd()} (error {sql.Number}, state {sql.State})"
            : null;

    /// <inheritdoc />
    public override bool IsUniqueViolation(Exception exception)
        => exception is SqlException sql && Array.IndexOf(UniqueViolations, sql.Number) >= 0;

    /// <inheritdoc />
    public override bool IsForeignKeyViolation(Exception exception)
        => exception is SqlException { Number: ForeignKeyViolation };

    /// <inheritdoc />
    /// <remarks>SQL Server never sends a regular expression to the server; this path is never hit.</remarks>
    public override bool IsInvalidRegexError(Exception exception) => false;

    /// <inheritdoc />
    /// <remarks>
    /// SQL Server has no <c>json</c>/<c>jsonb</c> distinction; both are
    /// <c>nvarchar(max)</c>. K-027's key-ordering problem <em>does not exist
    /// here by construction</em>: the text is stored as-is.
    /// </remarks>
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
    public override void AddUuidArray(DbCommand command, string name, IReadOnlyList<Guid>? values)
        => AddTyped(
            command,
            name,
            DbType.String,
            values is null
                ? null
                : JsonSerializer.Serialize(
                    values.ToArray(),
                    AgentPrismJsonContext.Default.GuidArray));

    /// <inheritdoc />
    /// <remarks>
    /// SQL Server has no <c>interval</c> type. The time-series query derives
    /// the bucket from the <c>bucket_unit</c> text; this parameter is sent in
    /// minutes only to satisfy the shared signature.
    /// </remarks>
    public override void AddInterval(DbCommand command, string name, TimeSpan value)
        => AddTyped(command, name, DbType.Int32, (int)value.TotalMinutes);

    /// <inheritdoc />
    public override string ArrayContains(string column, string paramName)
        => $"EXISTS (SELECT 1 FROM OPENJSON(@{paramName}) WHERE value = {column})";

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
    /// The <c>datetimeoffset(7)</c> column takes a <see cref="DateTimeOffset"/>.
    /// The value is always converted to UTC before writing, so the offset
    /// read back is zero, matching PostgreSQL's <c>timestamptz</c> behavior.
    /// </remarks>
    public override void AddTimestamp(DbCommand command, string name, DateTimeOffset? value)
        => AddTyped(command, name, DbType.DateTimeOffset, value?.ToUniversalTime());

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 Precision and scale are given explicitly; if not, SQL Server
    /// assumes <c>decimal(18,0)</c> and silently truncates the fractional part.
    /// </remarks>
    public override void AddDecimal(DbCommand command, string name, decimal? value)
    {
        var parameter = AddTyped(command, name, DbType.Decimal, value);
        parameter.Precision = DecimalPrecision;
        parameter.Scale = DecimalScale;
    }

    /// <inheritdoc />
    public override void AddBinary(DbCommand command, string name, byte[]? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Length -1 is given for varbinary(max); without it, SqlClient infers
        // the size from the value's length and errors above 8000 bytes.
        var parameter = new SqlParameter(name, SqlDbType.VarBinary, -1)
        {
            Value = (object?)value ?? DBNull.Value,
        };

        command.Parameters.Add(parameter);
    }

    /// <inheritdoc />
    public override string BuildRetentionCountSql(string table, string wherePredicate)
        => $"SELECT COUNT(*) FROM {table} WHERE {wherePredicate};";

    /// <inheritdoc />
    public override string BuildRetentionArchiveSelectSql(string table, string wherePredicate, string orderColumn)
        => $"""
            SELECT TOP (@batchSize) *
            FROM {table}
            WHERE {wherePredicate}
            ORDER BY {orderColumn};
            """;

    /// <inheritdoc />
    /// <remarks>
    /// <c>DELETE TOP (n)</c> is T-SQL-specific and needs no subquery.
    /// <c>TOP (0)</c> does NOT error (unlike OFFSET/FETCH) — no separate
    /// zero-guard line is needed.
    /// </remarks>
    public override string BuildRetentionDeleteBatchSql(string table, string wherePredicate)
        => $"DELETE TOP (@batchSize) FROM {table} WHERE {wherePredicate};";

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 <c>OFFSET</c>/<c>FETCH</c> errors WITHOUT an <c>ORDER BY</c>
    /// (the K-026 trap); this query always carries an <c>ORDER BY</c>.
    /// </remarks>
    public override string BuildRetentionFindNthRowCutoffSql(
        string table,
        string orderExpression,
        string? extraPredicate)
        => $"""
            SELECT {orderExpression}
            FROM {table}
            WHERE {orderExpression} IS NOT NULL{AndAlso(extraPredicate)}
            ORDER BY {orderExpression} DESC
            OFFSET (@n - 1) ROWS FETCH NEXT 1 ROWS ONLY;
            """;
}
