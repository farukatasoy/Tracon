using System.Data.Common;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>
/// <see cref="SqlDialect"/> soyutlamasinin PostgreSQL uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// Paylasilan depo kodunun gordugu tek Npgsql temas noktasi budur. Davranis
/// Faz 2'de kurulan halinden <strong>degismemistir</strong>: ayni
/// <see cref="NpgsqlDbType"/> degerleri, ayni UTC cevirimi.
/// </para>
/// <para>
/// <c>EnableDynamicJson()</c> kullanilmadigi icin <c>json</c> ve <c>jsonb</c>
/// alanlari metin olarak tasinir; bu yuzden yuk zaten seri hale getirilmis bir
/// <see cref="string"/> olarak gelir.
/// </para>
/// </remarks>
internal sealed class PostgresDialect : SqlDialect
{
    /// <summary>Benzersizlik kisiti ihlali SQLSTATE kodu.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>Yabanci anahtar kisiti ihlali SQLSTATE kodu.</summary>
    private const string ForeignKeyViolation = "23503";

    /// <summary>
    /// Migration kilidinin sabit anahtari.
    /// </summary>
    /// <remarks>
    /// Deger AgentPrism'e ozgudur ve <strong>degistirilmemelidir</strong>: eski surumu
    /// calistiran bir replika farkli bir anahtar kullanirsa kilit koruma saglamaz.
    /// </remarks>
    private const long AdvisoryLockKey = 0x41_50_52_49_53_4D_00_01;

    private readonly PostgresQueries _queries;

    /// <summary>Yeni bir PostgreSQL diyalekti olusturur.</summary>
    /// <param name="schemaName">Dogrulanacak sema adi.</param>
    public PostgresDialect(string schemaName) => _queries = new PostgresQueries(schemaName);

    /// <inheritdoc />
    public override SqlQueriesBase Queries => _queries;

    /// <inheritdoc />
    public override string MigrationResourcePrefix => "AgentPrism.PostgreSql.Migrations.";

    /// <inheritdoc />
    public override async ValueTask AcquireMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken)
        => await ExecuteLockAsync(
            connection,
            "SELECT pg_advisory_lock(@key);",
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
    public override IReadOnlyList<string> ReadTextArray(DbDataReader reader, int ordinal)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return reader.IsDBNull(ordinal) ? [] : reader.GetFieldValue<string[]>(ordinal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>timestamptz</c> sutunu <see cref="DateTime"/> (<c>Kind = Utc</c>) bekler.
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
        int commandTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = commandTimeout;
        command.Parameters.Add(new NpgsqlParameter("key", NpgsqlDbType.Bigint) { Value = AdvisoryLockKey });

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }
}
