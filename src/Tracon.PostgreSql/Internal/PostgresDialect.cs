using System.Data.Common;
using Npgsql;
using NpgsqlTypes;

namespace Tracon;

/// <summary>
/// The PostgreSQL implementation of the <see cref="SqlDialect"/> abstraction.
/// </summary>
/// <remarks>
/// <para>
/// This is the only Npgsql touch point the shared store code sees. Behavior
/// is <strong>unchanged</strong> from the original schema: the same
/// <see cref="NpgsqlDbType"/> values, the same UTC conversion.
/// </para>
/// <para>
/// Because <c>EnableDynamicJson()</c> is not used, <c>json</c> and <c>jsonb</c>
/// fields are carried as text; the payload therefore arrives already as a
/// serialized <see cref="string"/>.
/// </para>
/// </remarks>
internal sealed class PostgresDialect : SqlDialect
{
    /// <summary>SQLSTATE code for a uniqueness constraint violation.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>SQLSTATE code for a foreign key constraint violation.</summary>
    private const string ForeignKeyViolation = "23503";

    /// <summary><c>deadlock_detected</c>: the server broke the cycle by aborting this transaction.</summary>
    private const string DeadlockDetected = "40P01";

    /// <summary>SQLSTATE code for an invalid regular expression.</summary>
    private const string InvalidRegularExpression = "2201B";

    private readonly PostgresQueries _queries;
    private readonly long _advisoryLockKey;

    /// <summary>Creates a new PostgreSQL dialect.</summary>
    /// <param name="schemaName">The schema name to validate.</param>
    public PostgresDialect(string schemaName)
    {
        _queries = new PostgresQueries(schemaName);
        _advisoryLockKey = MigrationLockKey.ForSchema(_queries.Schema);
    }

    /// <inheritdoc />
    public override SqlQueriesBase Queries => _queries;

    /// <inheritdoc />
    public override string MigrationResourcePrefix => "Tracon.PostgreSql.Migrations.";

    /// <inheritdoc />
    public override string TenantIdTableCatalogSql =>
        """
        SELECT c.table_name
          FROM information_schema.columns c
          JOIN information_schema.tables t
            ON t.table_schema = c.table_schema
           AND t.table_name = c.table_name
         WHERE c.table_schema = '{schema}'
           AND c.column_name = 'tenant_id'
           AND t.table_type = 'BASE TABLE'
         ORDER BY c.table_name
        """;

    /// <inheritdoc />
    /// <remarks>
    /// The "knowledge" set needs the <c>pgvector</c> extension and
    /// is therefore opt-in; a consumer without permission to
    /// install extensions on a managed PostgreSQL never sees it.
    /// </remarks>
    public override IReadOnlyDictionary<string, string> OptionalMigrationResourcePrefixes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["knowledge"] = "Tracon.PostgreSql.MigrationsKnowledge.",
            ["views"] = "Tracon.PostgreSql.MigrationsViews.",
        };

    /// <inheritdoc />
    /// <remarks>Core id 24 (<c>0024_vector</c>) relocated to the "knowledge" set as <c>0001_vector</c>.</remarks>
    public override IReadOnlyDictionary<int, string> RelocatedCoreMigrationSets { get; } =
        new Dictionary<int, string>
        {
            [24] = "knowledge",
        };

    /// <inheritdoc />
    /// <remarks>
    /// The lock key is scoped to the schema: <see cref="MigrationLockKey"/>
    /// derives a deterministic key from the schema name so that independent
    /// Tracon deployments sharing the same database with different
    /// schemas don't block each other's startup.
    /// </remarks>
    public override async ValueTask AcquireMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
        => await ExecuteLockAsync(
            connection,
            "SELECT pg_advisory_lock(@key);",
            _advisoryLockKey,
            commandTimeout,
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public override async ValueTask ReleaseMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
        => await ExecuteLockAsync(
            connection,
            "SELECT pg_advisory_unlock(@key);",
            _advisoryLockKey,
            commandTimeout,
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public override string? DescribeDatabaseError(Exception exception)
        => exception is PostgresException postgres
            ? $"{postgres.MessageText} (SQLSTATE {postgres.SqlState})"
            : null;

    /// <inheritdoc />
    public override bool IsUniqueViolation(Exception exception)
        => exception is PostgresException { SqlState: UniqueViolation };

    /// <inheritdoc />
    public override bool IsForeignKeyViolation(Exception exception)
        => exception is PostgresException { SqlState: ForeignKeyViolation };

    /// <inheritdoc />
    public override bool IsDeadlock(Exception exception)
        => exception is PostgresException { SqlState: DeadlockDetected };

    /// <inheritdoc />
    public override bool IsInvalidRegexError(Exception exception)
        => exception is PostgresException { SqlState: InvalidRegularExpression };

    /// <inheritdoc />
    public override void AddJson(DbCommand command, string name, string? value)
        => AddNpgsql(command, name, NpgsqlDbType.Json, value);

    /// <inheritdoc />
    public override void AddJsonb(DbCommand command, string name, string? value)
        => AddNpgsql(command, name, NpgsqlDbType.Jsonb, value);

    /// <inheritdoc />
    public override void AddTextArray(DbCommand command, string name, IReadOnlyList<string>? values)
        => AddNpgsql(command, name, NpgsqlDbType.Array | NpgsqlDbType.Text, values?.ToArray());

    /// <inheritdoc />
    public override void AddUuidArray(DbCommand command, string name, IReadOnlyList<Guid>? values)
        => AddNpgsql(command, name, NpgsqlDbType.Array | NpgsqlDbType.Uuid, values?.ToArray());

    /// <inheritdoc />
    public override void AddInterval(DbCommand command, string name, TimeSpan value)
        => AddNpgsql(command, name, NpgsqlDbType.Interval, value);

    /// <inheritdoc />
    public override string ArrayContains(string column, string paramName) => $"{column} = ANY(@{paramName})";

    /// <inheritdoc />
    public override IReadOnlyList<string> ReadTextArray(DbDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<string[]>(ordinal);
    }

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
    /// PostgreSQL does not support <c>DELETE ... LIMIT</c>; a batch is selected
    /// with a <c>ctid</c> subquery. There is NO ordering — batch order does not matter.
    /// </remarks>
    public override string BuildRetentionDeleteBatchSql(string table, string wherePredicate)
        => $"""
            DELETE FROM {table}
             WHERE ctid IN (
                   SELECT ctid FROM {table}
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
            OFFSET @n - 1
            LIMIT 1;
            """;

    /// <inheritdoc />
    /// <remarks>
    /// The <c>timestamptz</c> column expects a <see cref="DateTime"/> (<c>Kind = Utc</c>).
    /// </remarks>
    public override void AddTimestamp(DbCommand command, string name, DateTimeOffset? value)
        => AddNpgsql(command, name, NpgsqlDbType.TimestampTz, value?.UtcDateTime);

    /// <inheritdoc />
    public override void AddText(DbCommand command, string name, string? value)
        => AddNpgsql(command, name, NpgsqlDbType.Text, value);

    /// <inheritdoc />
    public override void AddUuid(DbCommand command, string name, Guid? value)
        => AddNpgsql(command, name, NpgsqlDbType.Uuid, value);

    /// <inheritdoc />
    public override void AddInt16(DbCommand command, string name, short? value)
        => AddNpgsql(command, name, NpgsqlDbType.Smallint, value);

    /// <inheritdoc />
    public override void AddInt32(DbCommand command, string name, int? value)
        => AddNpgsql(command, name, NpgsqlDbType.Integer, value);

    /// <inheritdoc />
    public override void AddInt64(DbCommand command, string name, long? value)
        => AddNpgsql(command, name, NpgsqlDbType.Bigint, value);

    /// <inheritdoc />
    public override void AddDecimal(DbCommand command, string name, decimal? value)
        => AddNpgsql(command, name, NpgsqlDbType.Numeric, value);

    /// <inheritdoc />
    public override void AddBinary(DbCommand command, string name, byte[]? value)
        => AddNpgsql(command, name, NpgsqlDbType.Bytea, value);

    private static void AddNpgsql(DbCommand command, string name, NpgsqlDbType type, object? value)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
    }

    private static async ValueTask ExecuteLockAsync(
        DbConnection connection,
        string sql,
        long key,
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeout;
        command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Bigint) { Value = key });

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
