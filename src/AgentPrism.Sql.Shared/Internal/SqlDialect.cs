using System.Data;
using System.Data.Common;

namespace AgentPrism;

/// <summary>
/// Saglayiciya ozgu her davranisin gectigi tek kapi.
/// </summary>
/// <remarks>
/// <para>
/// <c>AgentPrism.Sql.Shared</c> altindaki depo uygulamalari yalnizca ADO.NET taban
/// tiplerini (<see cref="DbCommand"/>, <see cref="DbDataReader"/>,
/// <see cref="DbDataSource"/>) tanir. <c>Npgsql</c> veya
/// <c>Microsoft.Data.SqlClient</c> ad alanina <strong>hicbir</strong> paylasilan
/// dosya referans veremez; farklarin tamami bu sinifin turevlerinde toplanir.
/// </para>
/// <para>
/// Farklar uc kumede toplanir:
/// </para>
/// <list type="number">
///   <item><description>
///     <strong>Parametre tiplemesi.</strong> <c>jsonb</c>, dizi ve aralik gibi
///     tiplerin ADO.NET'te ortak bir karsiligi yoktur.
///   </description></item>
///   <item><description>
///     <strong>Dizi tasima bicimi.</strong> PostgreSQL yerel dizi gonderir
///     (<c>unnest</c>); SQL Server JSON metni gonderir (<c>OPENJSON</c>).
///     Metin farki SQL'in icinde kalir, C# akisi ayni olur.
///   </description></item>
///   <item><description>
///     <strong>Migration kilidi.</strong> <c>pg_advisory_lock</c> ve
///     <c>sp_getapplock</c>.
///   </description></item>
/// </list>
/// <para>
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-176.
/// </para>
/// </remarks>
internal abstract class SqlDialect
{
    /// <summary>Bu saglayicinin SQL metinleri.</summary>
    public abstract SqlQueriesBase Queries { get; }

    /// <summary>
    /// Gomulu migration kaynaklarinin ad oneki.
    /// </summary>
    /// <remarks>
    /// Her saglayicinin kendi migration seti vardir ve numaralandirma
    /// <c>0001</c>'den baslar. Iki setin numaralarinin eslesmesi
    /// <strong>gerekmez</strong>. Gerekce: <c>docs/KARARLAR.md</c>, karar K-178.
    /// </remarks>
    public abstract string MigrationResourcePrefix { get; }

    // --- Migration kilidi ---

    /// <summary>Migration kilidini alir.</summary>
    /// <param name="connection">Kilidin uzerinde tutulacagi baglanti.</param>
    /// <param name="commandTimeout">Komut ust suresi (saniye).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// Kilit <em>oturum kapsamlidir</em>; bu yuzden tum migration adimlari ayni
    /// baglanti uzerinde yurutulur.
    /// </remarks>
    public abstract ValueTask AcquireMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken);

    /// <summary>Migration kilidini birakir.</summary>
    /// <param name="connection">Kilidin tutuldugu baglanti.</param>
    /// <param name="commandTimeout">Komut ust suresi (saniye).</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public abstract ValueTask ReleaseMigrationLockAsync(
        DbConnection connection,
        int commandTimeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Migration uygulanirken olusan saglayiciya ozgu hatayi anlasilir bir
    /// <see cref="AgentPrismException"/> mesajina cevirir.
    /// </summary>
    /// <param name="exception">Yakalanan istisna.</param>
    /// <returns>
    /// Bu saglayicinin veritabani hatasiysa aciklama metni; degilse
    /// <see langword="null"/> (istisna yeniden firlatilir).
    /// </returns>
    public abstract string? DescribeDatabaseError(Exception exception);

    /// <summary>Istisna bir benzersizlik kisiti ihlali mi.</summary>
    /// <param name="exception">Yakalanan istisna.</param>
    /// <returns>Oyleyse <see langword="true"/>.</returns>
    /// <remarks>
    /// PostgreSQL SQLSTATE <c>23505</c> verir; SQL Server 2601/2627 numarali
    /// hatalari kullanir. Depolar bu farki gormez.
    /// </remarks>
    public abstract bool IsUniqueViolation(Exception exception);

    /// <summary>Istisna bir yabanci anahtar kisiti ihlali mi.</summary>
    /// <param name="exception">Yakalanan istisna.</param>
    /// <returns>Oyleyse <see langword="true"/>.</returns>
    /// <remarks>
    /// PostgreSQL SQLSTATE <c>23503</c> verir; SQL Server 547 numarali hatayi
    /// kullanir.
    /// </remarks>
    public abstract bool IsForeignKeyViolation(Exception exception);

    // --- Saglayiciya ozgu parametre tiplemesi ---

    /// <summary>Bir JSON metnini <c>json</c> sutunu icin parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">JSON metni; <see langword="null"/> olabilir.</param>
    /// <remarks>
    /// PostgreSQL'de <c>json</c> ile <c>jsonb</c> ayrimi anlamlidir: <c>jsonb</c>
    /// nesne anahtarlarini yeniden siralar ve polimorfik <c>$type</c> ayracini
    /// bozar (karar K-027). SQL Server'da ikisi de <c>nvarchar(max)</c>'tir ve
    /// sira zaten korunur; ayrim orada islevsizdir ama <em>zararsizdir</em> —
    /// paylasilan kod tek bir sozlesme kullanir.
    /// </remarks>
    public abstract void AddJson(DbCommand command, string name, string? value);

    /// <summary>Bir JSON metnini <c>jsonb</c> sutunu icin parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">JSON metni; <see langword="null"/> olabilir.</param>
    public abstract void AddJsonb(DbCommand command, string name, string? value);

    /// <summary>Bir metin dizisini parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="values">Dizi; <see langword="null"/> olabilir.</param>
    public abstract void AddTextArray(DbCommand command, string name, IReadOnlyList<string>? values);

    /// <summary>Bir kimlik dizisini parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="values">Dizi; <see langword="null"/> olabilir.</param>
    public abstract void AddUuidArray(DbCommand command, string name, IReadOnlyList<Guid>? values);

    /// <summary>Bir zaman araligini parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Aralik.</param>
    public abstract void AddInterval(DbCommand command, string name, TimeSpan value);

    /// <summary>Bir metin dizisi sutununu okur.</summary>
    /// <param name="reader">Okuyucu.</param>
    /// <param name="ordinal">Sutun sirasi.</param>
    /// <returns>Dizi; sutun <c>NULL</c> ise bos dizi.</returns>
    public abstract IReadOnlyList<string> ReadTextArray(DbDataReader reader, int ordinal);

    /// <summary>Yalin bir tablo adini bu saglayicinin sema/onek kuraliyla nitelendirir.</summary>
    /// <param name="tableName">Sema/onek olmadan tablo adi (ornegin <c>"sessions"</c>).</param>
    /// <returns>Calistirilabilir SQL'e gomulmeye hazir, nitelendirilmis ad.</returns>
    /// <remarks>
    /// PostgreSQL ve SQL Server <c>{sema}.{tablo}</c> (nokta ile) kullanir;
    /// SQLite'ta nesne adlari veritabani genelinde tek ad alanini paylastigi
    /// icin onek dogrudan bitistirilir, nokta YOKTUR (K-193). Varsayilan
    /// uygulama nokta ile nitelendirir; <see cref="RetentionTargetRegistry"/>
    /// gibi saglayicidan bagimsiz SQL uretimi bunu kullanir.
    /// </remarks>
    public virtual string QualifyTable(string tableName) => $"{Queries.Schema}.{tableName}";

    // --- Saklama (Faz 25): veri duzlemi parti sorgulari ---

    /// <summary>
    /// Bir hedefte <paramref name="wherePredicate"/>'e uyan satir sayisini
    /// donduren SQL metnini kurar.
    /// </summary>
    /// <param name="table">Sema onekli tablo adi.</param>
    /// <param name="wherePredicate"><c>@cutoff</c>'a atifta bulunan SQL kosulu.</param>
    /// <returns>Calistirilabilir SQL. Tek parametre: <c>@cutoff</c>.</returns>
    /// <remarks>
    /// Bu ucu saglayicilar arasinda ozdestir (yalniz <c>COUNT(*)</c>); yine de
    /// diyalekt uzerinden gecer cunku <see cref="RetentionTargetRegistry"/>'nin
    /// urettigi metin saglayiciya BAGIMSIZDIR ve K1/K-176 geregi tum SQL
    /// metninin tek gecidi diyalekttir.
    /// </remarks>
    public abstract string BuildRetentionCountSql(string table, string wherePredicate);

    /// <summary>
    /// <paramref name="wherePredicate"/>'e uyan bir parti satiri (arsivlemek
    /// icin) okuyan SQL metnini kurar. Silmez.
    /// </summary>
    /// <param name="table">Sema onekli tablo adi.</param>
    /// <param name="wherePredicate"><c>@cutoff</c>'a atifta bulunan SQL kosulu.</param>
    /// <param name="orderColumn">Determinizm icin siralama sutunu.</param>
    /// <returns>Calistirilabilir SQL. Parametreler: <c>@cutoff</c>, <c>@batchSize</c>.</returns>
    public abstract string BuildRetentionArchiveSelectSql(string table, string wherePredicate, string orderColumn);

    /// <summary>
    /// <paramref name="wherePredicate"/>'e uyan bir parti satiri silen SQL
    /// metnini kurar. Toplu tek bir <c>DELETE</c> DEGILDIR.
    /// </summary>
    /// <param name="table">Sema onekli tablo adi.</param>
    /// <param name="wherePredicate"><c>@cutoff</c>'a atifta bulunan SQL kosulu.</param>
    /// <returns>Calistirilabilir SQL. Parametreler: <c>@cutoff</c>, <c>@batchSize</c>.</returns>
    /// <remarks>
    /// Uc saglayici uc farkli teknik kullanir: PostgreSQL <c>ctid</c> alt
    /// sorgusu, SQL Server <c>DELETE TOP (n)</c>, SQLite <c>rowid</c> alt
    /// sorgusu. Hicbiri satirlarin belirli bir sirada silinecegini garanti
    /// etmez — parti sirasi onemli degildir, yalniz boyutu onemlidir.
    /// </remarks>
    public abstract string BuildRetentionDeleteBatchSql(string table, string wherePredicate);

    // --- Ortak tiplemeler (gerekirse turevde degistirilir) ---

    /// <summary>Zaman damgasini parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Zaman damgasi; <see langword="null"/> olabilir.</param>
    /// <remarks>
    /// Deger her zaman UTC'ye cevrilerek yazilir. PostgreSQL <c>timestamptz</c>
    /// icin <see cref="DateTime"/> (<c>Kind = Utc</c>) bekler; SQL Server
    /// <c>datetimeoffset</c> icin <see cref="DateTimeOffset"/> alir. Cevirim
    /// turevlerdedir.
    /// </remarks>
    public abstract void AddTimestamp(DbCommand command, string name, DateTimeOffset? value);

    /// <summary>Bos olabilen bir metni parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger; <see langword="null"/> olabilir.</param>
    public virtual void AddText(DbCommand command, string name, string? value)
        => AddTyped(command, name, DbType.String, value);

    /// <summary>Bos olabilen bir kimligi parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger; <see langword="null"/> olabilir.</param>
    public virtual void AddUuid(DbCommand command, string name, Guid? value)
        => AddTyped(command, name, DbType.Guid, value);

    /// <summary>Bos olabilen bir <c>smallint</c> degerini parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger; <see langword="null"/> olabilir.</param>
    public virtual void AddInt16(DbCommand command, string name, short? value)
        => AddTyped(command, name, DbType.Int16, value);

    /// <summary>Bos olabilen bir <c>integer</c> degerini parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger; <see langword="null"/> olabilir.</param>
    public virtual void AddInt32(DbCommand command, string name, int? value)
        => AddTyped(command, name, DbType.Int32, value);

    /// <summary>Bos olabilen bir <c>bigint</c> degerini parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger; <see langword="null"/> olabilir.</param>
    public virtual void AddInt64(DbCommand command, string name, long? value)
        => AddTyped(command, name, DbType.Int64, value);

    /// <summary>Bos olabilen bir ondalik degeri parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger; <see langword="null"/> olabilir.</param>
    public virtual void AddDecimal(DbCommand command, string name, decimal? value)
        => AddTyped(command, name, DbType.Decimal, value);

    /// <summary>Bos olabilen ikili veriyi parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger; <see langword="null"/> olabilir.</param>
    public virtual void AddBinary(DbCommand command, string name, byte[]? value)
        => AddTyped(command, name, DbType.Binary, value);

    /// <summary>Bir mantiksal degeri parametreye baglar.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="value">Deger.</param>
    public virtual void AddBoolean(DbCommand command, string name, bool value)
        => AddTyped(command, name, DbType.Boolean, value);

    /// <summary>Verilen tiple bir parametre ekler.</summary>
    /// <param name="command">Komut.</param>
    /// <param name="name">Parametre adi.</param>
    /// <param name="type">Parametre tipi.</param>
    /// <param name="value">Deger; <see langword="null"/> ise <see cref="DBNull"/> yazilir.</param>
    /// <returns>Eklenen parametre.</returns>
    /// <remarks>
    /// 🚨 Istege bagli suzgec parametreleri (<c>@p IS NULL OR col = @p</c> deseni)
    /// <strong>her zaman</strong> acikca tiplenmelidir. Tipsiz bir <c>NULL</c>
    /// gonderildiginde PostgreSQL tipi cikaramaz ve <c>42P08</c> verir; hata
    /// yalnizca calisma aninda gorunur. Ayrinti: <c>docs/hafiza/postgresql.md</c>.
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
