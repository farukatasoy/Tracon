using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace AgentPrism;

/// <summary>
/// <see cref="SqlDialect"/> soyutlamasinin SQL Server uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// Paylasilan depo kodunun gordugu tek <c>Microsoft.Data.SqlClient</c> temas
/// noktasi budur.
/// </para>
/// <para>
/// Iki tuzak burada kapanir:
/// </para>
/// <list type="number">
///   <item><description>
///     🚨 <strong>Ondalik kesme.</strong> Tipi verilmemis bir
///     <see cref="decimal"/> parametresini SQL Server <c>decimal(18,0)</c> sayar
///     ve ondalik kismi <em>sessizce atar</em> — para tutarlari tam sayiya
///     yuvarlanirdi. Butun ondalik sutunlar <c>decimal(20,10)</c>'dur ve
///     parametreye ayni kesinlik acikca yazilir.
///   </description></item>
///   <item><description>
///     <strong>Dizi tasima.</strong> SQL Server'da dizi parametresi yoktur;
///     diziler JSON metni olarak gonderilir ve SQL tarafinda <c>OPENJSON</c> ile
///     acilir. Karsilik gelen okuma da JSON cozer.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqlServerDialect : SqlDialect
{
    /// <summary>Butun ondalik sutunlarin kesinligi (<c>decimal(20,10)</c>).</summary>
    private const byte DecimalPrecision = 20;

    /// <summary>Butun ondalik sutunlarin olcegi (<c>decimal(20,10)</c>).</summary>
    private const byte DecimalScale = 10;

    /// <summary>Benzersizlik kisiti ihlali hata numaralari.</summary>
    /// <remarks>2601 benzersiz indeks, 2627 benzersiz kisit icindir.</remarks>
    private static readonly int[] UniqueViolations = [2601, 2627];

    /// <summary>Yabanci anahtar kisiti ihlali hata numarasi.</summary>
    private const int ForeignKeyViolation = 547;

    /// <summary>
    /// Migration kilidinin kaynak adi.
    /// </summary>
    /// <remarks>
    /// Deger AgentPrism'e ozgudur ve <strong>degistirilmemelidir</strong>: eski surumu
    /// calistiran bir replika farkli bir ad kullanirsa kilit koruma saglamaz.
    /// </remarks>
    private const string MigrationLockResource = "AgentPrism.Migrations";

    /// <summary>Kilit bekleme ust suresi (milisaniye).</summary>
    private const int LockTimeoutMilliseconds = 30000;

    private readonly SqlServerQueries _queries;

    /// <summary>Yeni bir SQL Server diyalekti olusturur.</summary>
    /// <param name="schemaName">Dogrulanacak sema adi.</param>
    public SqlServerDialect(string schemaName) => _queries = new SqlServerQueries(schemaName);

    /// <inheritdoc />
    public override SqlQueriesBase Queries => _queries;

    /// <inheritdoc />
    public override string MigrationResourcePrefix => "AgentPrism.SqlServer.Migrations.";

    /// <inheritdoc />
    /// <remarks>
    /// <c>sp_getapplock</c> oturum kapsaminda alinir. Donus degeri negatifse kilit
    /// alinamamistir; sessizce devam etmek iki replikanin ayni migration'i ayni
    /// anda uygulamasina izin verirdi.
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

        AddTyped(command, "@Resource", DbType.String, MigrationLockResource);
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
                    $"AgentPrism migration kilidi alinamadi (sp_getapplock donus degeri {code}). " +
                    $"Kilit en cok {LockTimeoutMilliseconds} ms beklenir; baska bir ornek uzun " +
                    "suren bir migration uyguluyor olabilir.");
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

        AddTyped(command, "@Resource", DbType.String, MigrationLockResource);
        AddTyped(command, "@LockOwner", DbType.String, "Session");

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override string? DescribeDatabaseError(Exception exception)
        => exception is SqlException sql
            ? $"{sql.Message.TrimEnd()} (hata {sql.Number}, durum {sql.State})"
            : null;

    /// <inheritdoc />
    public override bool IsUniqueViolation(Exception exception)
        => exception is SqlException sql && Array.IndexOf(UniqueViolations, sql.Number) >= 0;

    /// <inheritdoc />
    public override bool IsForeignKeyViolation(Exception exception)
        => exception is SqlException { Number: ForeignKeyViolation };

    /// <inheritdoc />
    /// <remarks>
    /// SQL Server'da <c>json</c> ile <c>jsonb</c> ayrimi yoktur; ikisi de
    /// <c>nvarchar(max)</c>'tir. K-027'nin anahtar siralamasi sorunu burada
    /// <em>kendiliginden yoktur</em>: metin oldugu gibi saklanir.
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
    /// SQL Server'da <c>interval</c> tipi yoktur. Zaman serisi sorgusu araligi
    /// <c>bucket_unit</c> metninden turetir; bu parametre yalnizca paylasilan
    /// imzayi karsilamak icin dakika olarak gonderilir.
    /// </remarks>
    public override void AddInterval(DbCommand command, string name, TimeSpan value)
        => AddTyped(command, name, DbType.Int32, (int)value.TotalMinutes);

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
    /// <c>datetimeoffset(7)</c> sutunu <see cref="DateTimeOffset"/> alir. Deger
    /// her zaman UTC'ye cevrilerek yazilir; boylece okunan ofset sifirdir ve
    /// PostgreSQL <c>timestamptz</c> davranisiyla ayni olur.
    /// </remarks>
    public override void AddTimestamp(DbCommand command, string name, DateTimeOffset? value)
        => AddTyped(command, name, DbType.DateTimeOffset, value?.ToUniversalTime());

    /// <inheritdoc />
    /// <remarks>
    /// 🚨 Kesinlik ve olcek acikca verilir; verilmezse SQL Server
    /// <c>decimal(18,0)</c> varsayar ve ondalik kismi sessizce keser.
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

        // varbinary(max) icin uzunluk -1 verilir; verilmezse SqlClient degerin
        // uzunluguna gore boyut cikarir ve 8000 baytin uzerinde hata olusur.
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
    /// <c>DELETE TOP (n)</c> T-SQL'e ozgudur ve bir alt sorgu gerektirmez.
    /// <c>TOP (0)</c> hata VERMEZ (OFFSET/FETCH'in aksine) — ayri bir sifir
    /// koruma satiri gerekmez.
    /// </remarks>
    public override string BuildRetentionDeleteBatchSql(string table, string wherePredicate)
        => $"DELETE TOP (@batchSize) FROM {table} WHERE {wherePredicate};";
}
