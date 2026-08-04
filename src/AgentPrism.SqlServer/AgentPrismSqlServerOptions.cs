namespace AgentPrism;

/// <summary>AgentPrism'in SQL Server kalicilik katmani ayarlari.</summary>
/// <remarks>
/// Dogrulama <see cref="AgentPrismSqlServerOptionsValidator"/> icinde elle yapilir;
/// <c>DataAnnotations</c> kullanilmaz. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
public sealed class AgentPrismSqlServerOptions
{
    /// <summary>Ayarlarin okundugu yapilandirma bolumunun tam yolu.</summary>
    public const string SectionName = "AgentPrism:SqlServer";

    /// <summary>
    /// SQL Server baglanti dizesi.
    /// </summary>
    /// <remarks>
    /// <strong>Bu deger bir sirdir ve dosyaya yazilmaz.</strong> <c>dotnet user-secrets</c>,
    /// ortam degiskeni veya bir sir yoneticisi kullanin.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// AgentPrism tablolarinin olusturulacagi sema. Tuketicinin <c>dbo</c> semasina
    /// hicbir kosulda dokunulmaz.
    /// </summary>
    /// <remarks>
    /// SQL Server daha genis bir tanimlayici kumesine izin verse de AgentPrism
    /// <em>ayni kati kurali</em> uygular: kucuk harf veya alt cizgi ile baslar,
    /// kucuk harf, rakam ve alt cizgi icerir, en cok 63 karakterdir. Boylece ayni
    /// sema adi PostgreSQL ile SQL Server arasinda degistirilmeden tasinabilir.
    /// </remarks>
    public string SchemaName { get; set; } = "agentprism";

    /// <summary>
    /// Uygulama baslarken bekleyen migration'lar otomatik uygulansin mi.
    /// </summary>
    /// <remarks>
    /// Uretimde <see langword="false"/> yapilip <see cref="MigrationRunner"/> ayri bir
    /// dagitim adiminda calistirilabilir. Boylece uzun suren bir migration uygulama
    /// baslangicini kilitlemez.
    /// </remarks>
    public bool AutoApplyMigrations { get; set; } = true;

    /// <summary>Tek bir SQL komutunun ust sure siniri (saniye). 0 sinirsiz demektir.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;
}
