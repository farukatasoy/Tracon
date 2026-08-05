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
    /// <c>Data Source=:memory:</c> desteklenir ama kalici DEGILDIR: baglanti
    /// kapaninca veri gider. Baglanti dizesi tipik olarak yalniz bir dosya yolu
    /// tasir ve SQL Server/PostgreSQL'in aksine kimlik bilgisi icermez; yine de
    /// bir dagitim ayrintisidir ve kaynak kontrolune yazilmaz.
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
