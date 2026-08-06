using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AgentPrism;

/// <summary>
/// <see cref="SqlDialect"/> soyutlamasinin SQLite uygulamasi.
/// </summary>
/// <remarks>
/// <para>
/// Paylasilan depo kodunun gordugu tek <c>Microsoft.Data.Sqlite</c> temas
/// noktasi budur.
/// </para>
/// <para>
/// Uc nokta olculdu ve buradaki davranis onlara gore kuruldu (Faz 24 acilisi):
/// </para>
/// <list type="number">
///   <item><description>
///     <strong>uuid BUYUK harfle yazilir, ozel islem GEREKMEZ.</strong>
///     <c>Microsoft.Data.Sqlite</c> tipi verilmeden (<see cref="DbHelpers.Add"/>'in
///     yaptigi gibi) veya <see cref="DbType.Guid"/> ile (taban sinifin
///     <see cref="SqlDialect.AddUuid"/> varsayilani) BIREBIR AYNI buyuk harfli,
///     tireli metni yazar — olculdu. Bu yuzden burada <c>AddUuid</c> ozellikle
///     EZILMEZ: kucuk harfe cevirmek, zorunlu (nullable olmayan) Guid'lerin
///     gectigi <see cref="DbHelpers.Add"/> yoluyla BUYUK harf, nullable Guid'lerin
///     gectigi bu yol ile kucuk harf yazardi. Ayni mantiksal kimlik iki farkli
///     metinle saklanir ve <c>WHERE</c>/<c>JOIN</c> esitligi (SQLite'ta BINARY,
///     harf buyuklugune duyarli metin karsilastirmasidir) SESSIZCE BASARISIZ
///     olurdu (ornek: <c>SqlTraceStore.UpsertTraceAsync</c> <c>Dialect.AddUuid</c>
///     ile yazar, <c>GetTraceByRunAsync</c> <c>DbHelpers.Add</c> ile okur — ayni
///     <c>run_id</c> sutunu). Harf buyuklugu tutarli oldugu surece uuid v7'nin
///     zaman sirali onekinin sozluksel sirasi bozulmaz.
///   </description></item>
///   <item><description>
///     <strong>Zaman damgasi.</strong> <c>Microsoft.Data.Sqlite</c>'in varsayilan
///     <see cref="DateTimeOffset"/> yazimi (bosluk ayracli, mikrosaniye kesinlikli)
///     sozluksel olarak zaman sirali DEGILDIR; bu yuzden <c>AddTimestamp</c> elle
///     bicimlendirir (<c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>). Zaman damgasi HER ZAMAN
///     <see cref="SqlDialect.AddTimestamp"/> uzerinden, tek bir yoldan gecer — uuid'deki
///     ikili yol sorunu burada yoktur.
///   </description></item>
///   <item><description>
///     <strong><c>decimal</c> icin ozel islem GEREKMEZ.</strong> Surucu tipi
///     verilmeden de (veya <see cref="DbType.Decimal"/> ile) her zaman TEXT
///     olarak yazar ve kulturden bagimsizdir; <c>REAL</c>'e donusum yoktur.
///     Olculdu: <c>0.1m + 0.2m</c> gidip donuste tam <c>0.3m</c> kaldi.
///   </description></item>
///   <item><description>
///     <strong>Migration kilidi dosya tabanlidir.</strong> SQLite'ta
///     <c>pg_advisory_lock</c>/<c>sp_getapplock</c> karsiligi yoktur ve
///     <c>BEGIN IMMEDIATE</c>'i tum migration suresince acik tutmak
///     <see cref="MigrationRunner"/>'in kendi ic-ice islemleriyle CATISIR
///     (<c>Microsoft.Data.Sqlite</c> ic-ice islem desteklemez). Sidecar bir
///     dosya kilidi (<c>&lt;veritabani&gt;.agentprism-migration-lock</c>)
///     baglantinin islem durumuna hic dokunmadan ayni korumayi verir.
///     <c>:memory:</c> veritabanlarinda atlanir.
///   </description></item>
/// </list>
/// </remarks>
internal sealed class SqliteDialect : SqlDialect, IDisposable
{
    /// <summary><c>SQLITE_CONSTRAINT_UNIQUE</c> uzatilmis hata kodu.</summary>
    private const int UniqueConstraint = 2067;

    /// <summary><c>SQLITE_CONSTRAINT_PRIMARYKEY</c> uzatilmis hata kodu.</summary>
    private const int PrimaryKeyConstraint = 1555;

    /// <summary><c>SQLITE_CONSTRAINT_FOREIGNKEY</c> uzatilmis hata kodu.</summary>
    private const int ForeignKeyConstraint = 787;

    /// <summary>Migration kilit dosyasinin veritabani dosya adina eklenen soneki.</summary>
    private const string LockFileSuffix = ".agentprism-migration-lock";

    /// <summary>Kilit yeniden deneme araligi (milisaniye).</summary>
    private const int PollIntervalMilliseconds = 100;

    /// <summary><c>commandTimeout</c> 0 (sinirsiz) verildiginde kullanilan varsayilan kilit bekleme suresi (saniye).</summary>
    private const int DefaultLockWaitSeconds = 30;

    private readonly SqliteQueries _queries;

    private FileStream? _lockFile;

    /// <summary>Yeni bir SQLite diyalekti olusturur.</summary>
    /// <param name="tablePrefix">Dogrulanacak tablo onceki.</param>
    public SqliteDialect(string tablePrefix) => _queries = new SqliteQueries(tablePrefix);

    /// <inheritdoc />
    public override SqlQueriesBase Queries => _queries;

    /// <inheritdoc />
    public override string MigrationResourcePrefix => "AgentPrism.Sqlite.Migrations.";

    /// <inheritdoc />
    /// <remarks>
    /// SQLite dosyasinin yaninda bir kilit dosyasi acilir
    /// (<see cref="FileShare.None"/>); ikinci bir surec ayni dosyayi acamaz ve
    /// <paramref name="commandTimeout"/> saniye boyunca yoklayarak bekler.
    /// <c>:memory:</c> veritabanlarinda (<see cref="SqliteConnection.DataSource"/>
    /// bos) atlanir: baska bir surec ayni bellek ici veritabanini paylasamaz.
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

        var lockPath = dataSource + LockFileSuffix;
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
                    $"AgentPrism migration kilidi alinamadi: '{lockPath}' baska bir surec tarafindan " +
                    $"kullaniliyor. Kilit en cok {waitSeconds} saniye beklenir; baska bir ornek uzun suren " +
                    "bir migration uyguluyor olabilir.",
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
            ? $"{sql.Message.TrimEnd()} (hata {sql.SqliteErrorCode}, uzatilmis {sql.SqliteExtendedErrorCode})"
            : null;

    /// <inheritdoc />
    /// <remarks>
    /// SQLite hem UNIQUE hem PRIMARY KEY ihlalinde temel hata kodu olarak
    /// <c>SQLITE_CONSTRAINT</c> (19) doner; ayirt edici bilgi UZATILMIS koddadir.
    /// </remarks>
    public override bool IsUniqueViolation(Exception exception)
        => exception is SqliteException sql
            && sql.SqliteExtendedErrorCode is UniqueConstraint or PrimaryKeyConstraint;

    /// <inheritdoc />
    public override bool IsForeignKeyViolation(Exception exception)
        => exception is SqliteException sql && sql.SqliteExtendedErrorCode == ForeignKeyConstraint;

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
    /// SQLite'ta <c>interval</c> tipi yoktur. Zaman serisi sorgusu kova genisligini
    /// <c>@bucket_unit</c> metninden turetir; bu parametre yalnizca paylasilan
    /// imzayi karsilamak icin dakika olarak gonderilir (SQL Server ile ayni desen).
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
    /// 🚨 <c>Microsoft.Data.Sqlite</c>'in varsayilan <see cref="DateTimeOffset"/>
    /// bicimi (<c>2026-08-05 06:41:38.390129+00:00</c>) sozluksel olarak zaman
    /// sirali DEGILDIR (bosluk ayraci, altı basamak). Bicim elle sabitlenir:
    /// <c>yyyy-MM-ddTHH:mm:ss.fffffffZ</c>, her zaman UTC'ye cevrilerek.
    /// </remarks>
    public override void AddTimestamp(DbCommand command, string name, DateTimeOffset? value)
        => AddTyped(
            command,
            name,
            DbType.String,
            value?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));

    /// <inheritdoc />
    /// <remarks>
    /// SQLite'ta nesne adlari veritabani genelinde tek ad alanini paylasir;
    /// sema yoktur, tablo adinin basina dogrudan onek eklenir (nokta YOKTUR).
    /// Gerekce: <c>docs/hafiza/sql-saglayicilari.md</c>, K-193.
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
    /// SQLite <c>DELETE ... LIMIT</c>'i varsayilan derlemede desteklemez
    /// (<c>SQLITE_ENABLE_UPDATE_DELETE_LIMIT</c> gerekir); PostgreSQL'in
    /// <c>ctid</c> deseniyle ayni gerekceyle <c>rowid</c> alt sorgusu kullanilir.
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
    public override string BuildRetentionFindNthRowCutoffSql(string table, string orderExpression)
        => $"""
            SELECT {orderExpression}
            FROM {table}
            WHERE {orderExpression} IS NOT NULL
            ORDER BY {orderExpression} DESC
            LIMIT 1 OFFSET @n - 1;
            """;

    /// <summary>Migration kilidi hala aciksa birakir.</summary>
    public void Dispose()
    {
        _lockFile?.Dispose();
        _lockFile = null;
    }
}
