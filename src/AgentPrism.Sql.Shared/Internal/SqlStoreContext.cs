using System.Data.Common;

namespace AgentPrism;

/// <summary>
/// Paylasilan depo uygulamalarinin ihtiyac duydugu her seyi tasiyan baglam.
/// </summary>
/// <remarks>
/// <para>
/// Depolar bir <c>Options</c> tipine bagli degildir: her saglayicinin kendi
/// ayar sinifi (<c>AgentPrismPostgreSqlOptions</c>, <c>AgentPrismSqlServerOptions</c>)
/// vardir ve bunlar public'tir. Paylasilan kod tek bir tipe bagimli olamayacagi
/// icin ayarlardan tureyen degerler burada toplanir ve saglayicinin
/// <c>Use*</c> uzantisi bu baglami DI'ya kaydeder.
/// </para>
/// <para>
/// Tek bir nesne enjekte etmek ayrica <c>ActivatorUtilities.CreateInstance</c>
/// cagrilarini sadelestirir.
/// </para>
/// </remarks>
internal sealed class SqlStoreContext
{
    /// <summary>Veri kaynagi. Baglanti havuzunu saglayicinin surucusu yonetir.</summary>
    public required DbDataSource DataSource { get; init; }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    public required SqlDialect Dialect { get; init; }

    /// <summary>Tek bir SQL komutunun ust sure siniri (saniye). 0 sinirsiz demektir.</summary>
    public required int CommandTimeoutSeconds { get; init; }

    /// <summary>Uygulama baslarken bekleyen migration'lar otomatik uygulansin mi.</summary>
    public bool AutoApplyMigrations { get; init; } = true;

    /// <summary>Bu baglami kuran saglayicinin adi. Gunluk mesajlarinda gorunur.</summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Migration metnindeki <c>{sema-disi}</c> yer tutucularini degistiren ek
    /// anahtar/deger cifti (ornegin <c>{dimension}</c> — Faz 51'in vektor boyutu).
    /// </summary>
    /// <remarks>
    /// Sema yer tutucusu (<see cref="SqlQueriesBase.SchemaPlaceholder"/>) her
    /// zaman ayri ve zorunlu olarak degistirilir; bu sozluk KURULUM ANINDA
    /// bilinen, saglayiciya ozgu ek degerler icindir. Bos ise hicbir ek
    /// degistirme yapilmaz.
    /// </remarks>
    public IReadOnlyDictionary<string, string> MigrationTemplateValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Bu saglayicinin SQL metinleri.</summary>
    public SqlQueriesBase Sql => Dialect.Queries;

    /// <summary>Ust suresi ayarlanmis bir komut olusturur.</summary>
    /// <param name="sql">Komut metni.</param>
    /// <returns>Calistirilmaya hazir komut.</returns>
    /// <remarks>
    /// <see cref="DbDataSource.CreateCommand(string)"/> baglanti omrunu kendisi
    /// yonetir: komut calistirildiginda havuzdan bir baglanti alir, komut
    /// birakildiginda geri verir.
    /// </remarks>
    public DbCommand CreateCommand(string sql)
    {
        var command = DataSource.CreateCommand(sql);
        command.CommandTimeout = CommandTimeoutSeconds;

        return command;
    }

    /// <summary>Var olan bir baglanti ve islem uzerinde komut olusturur.</summary>
    /// <param name="sql">Komut metni.</param>
    /// <param name="connection">Kullanilacak baglanti.</param>
    /// <param name="transaction">Kullanilacak islem; yoksa <see langword="null"/>.</param>
    /// <returns>Calistirilmaya hazir komut.</returns>
    public DbCommand CreateCommand(string sql, DbConnection connection, DbTransaction? transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        command.Transaction = transaction;

        return command;
    }
}
