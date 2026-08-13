namespace AgentPrism;

/// <summary>AgentPrism'in SQLite kalicilik katmani ayarlari.</summary>
/// <remarks>
/// Dogrulama <see cref="AgentPrismSqliteOptionsValidator"/> icinde elle yapilir;
/// <c>DataAnnotations</c> kullanilmaz. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismSqliteOptions
{
    /// <summary>Ayarlarin okundugu yapilandirma bolumunun tam yolu.</summary>
    public const string SectionName = "AgentPrism:Sqlite";

    /// <summary>
    /// SQLite baglanti dizesi (ornek: <c>Data Source=agentprism.db</c>).
    /// </summary>
    /// <remarks>
    /// 🚨 Ciplak <c>Data Source=:memory:</c> DESTEKLENMEZ: bu kutuphane her
    /// islem icin <see cref="System.Data.Common.DbDataSource.CreateDbConnection"/>
    /// ile YENI bir baglanti acar, ve SQLite'ta ciplak <c>:memory:</c> her
    /// baglantiya kendi izole, anonim veritabanini verir — <c>Cache=Shared</c>
    /// eklenmesi bile bunu degistirmez (yalniz URI bicimli ad paylasilabilir).
    /// Sonuc: migration'lar bir baglantida uygulanir, ilk sorgu farkli (bos) bir
    /// veritabanina duser ve "no such table" ile coker. Bunun yerine paylasimli
    /// bellek ici bir veritabani icin URI bicimini kullanin, ornegin
    /// <c>Data Source=file:agentprism?mode=memory&amp;cache=shared</c> veya
    /// <c>Data Source=file::memory:?cache=shared</c>. Bu ayar dogrulama
    /// asamasinda (<see cref="AgentPrismSqliteOptionsValidator"/>) reddedilir.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// AgentPrism tablolarinin adina eklenen onek. Tuketicinin kendi
    /// tablolarina hicbir kosulda dokunulmaz.
    /// </summary>
    /// <remarks>
    /// SQLite'ta sema kavrami yoktur; bu, K-013'un ("tuketicinin semasina
    /// dokunma") SQLite karsiligidir. Onek PostgreSQL/SQL Server'in sema adiyla
    /// AYNI kati dogrulamadan gecer: kucuk harf veya alt cizgi ile baslar,
    /// kucuk harf, rakam ve alt cizgi icerir, en cok 63 karakterdir.
    /// </remarks>
    public string TablePrefix { get; set; } = "agentprism_";

    /// <summary>
    /// Uygulama baslarken bekleyen migration'lar otomatik uygulansin mi.
    /// </summary>
    public bool AutoApplyMigrations { get; set; } = true;

    /// <summary>Tek bir SQL komutunun ust sure siniri (saniye). 0 sinirsiz demektir.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
