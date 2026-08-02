using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>AgentPrism'in calisma zamani ayarlari.</summary>
/// <remarks>Dogrulama <see cref="AgentPrismOptionsValidator"/> icinde elle yapilir.</remarks>
public sealed class AgentPrismOptions
{
    /// <summary>Yapilandirma bolumunun varsayilan adi.</summary>
    public const string SectionName = "AgentPrism";

    /// <summary>
    /// Istekten kiraci cozulemedigi durumda kullanilacak kiraci kimligi.
    /// Tek kiracili kurulumda her zaman bu deger kullanilir.
    /// </summary>
    public string DefaultTenantId { get; set; } = "default";

    /// <summary>Calistirma kaydi ayarlari.</summary>
    public AgentPrismRunRecordingOptions RunRecording { get; set; } = new();

    /// <summary>Telemetri ayarlari.</summary>
    public AgentPrismObservabilityOptions Observability { get; set; } = new();

    /// <summary>Model saglayicisi devre kesici ayarlari.</summary>
    public AgentPrismCircuitBreakerOptions CircuitBreaker { get; set; } = new();

    /// <summary>Model saglayicisi saglik denetimi ayarlari.</summary>
    public AgentPrismHealthOptions Health { get; set; } = new();

    /// <summary>Denetim izi ayarlari.</summary>
    public AgentPrismAuditOptions Audit { get; set; } = new();

    /// <summary>Skill yukleme sinirlari.</summary>
    public AgentPrismSkillOptions Skills { get; set; } = new();
}

/// <summary>Skill icerigi ve agent baglantisi icin sinirlar.</summary>
public sealed class AgentPrismSkillOptions
{
    /// <summary>Tek bir agent'a baglanabilecek en fazla skill sayisi.</summary>
    public int MaxSkillsPerAgent { get; set; } = 10;

    /// <summary>Markdown talimatlarinin en fazla bayt sayisi.</summary>
    public int MaxInstructionsLength { get; set; } = 64 * 1024;

    /// <summary>Tek bir kaynak iceriginin en fazla bayt sayisi.</summary>
    public int MaxResourceContentLength { get; set; } = 256 * 1024;

    /// <summary>Bir skill'in tasiyabilecegi en fazla kaynak sayisi.</summary>
    public int MaxResourcesPerSkill { get; set; } = 20;
}

/// <summary>Denetim izi aktor cozumlemesinin ayarlari.</summary>
public sealed class AgentPrismAuditOptions
{
    /// <summary>
    /// Aktorun okunacagi claim tipi. <see langword="null"/> ise varsayilan sira
    /// izlenir: <c>ClaimTypes.NameIdentifier</c> → <c>ClaimTypes.Name</c> → <c>sub</c>.
    /// </summary>
    public string? ActorClaimType { get; set; }
}

/// <summary>
/// Bir model saglayicisi ardisik hata verdiginde istekleri gecici olarak kesen devre
/// kesicinin ayarlari.
/// </summary>
/// <remarks>
/// Devre kesici <see cref="IChatClient"/> boru hattina bir dekoratordur, saglayici
/// uygulamasinin icine gomulmez — bu yuzden her saglayici (OpenAI, uyumlu sunucular,
/// gelecekteki Anthropic/Gemini) ayni korumayi bedava alir.
/// Gerekce: <c>docs/KARARLAR.md</c>, K-007 (yeni paket alinmadi) ve
/// <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>, bolum 8.3.
/// </remarks>
public sealed class AgentPrismCircuitBreakerOptions
{
    /// <summary>
    /// Devre kesici acik mi. Kapatilirsa istekler her zaman saglayiciya gider ve
    /// hicbir ardisik hata sayaci tutulmaz.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Devrenin acilmasi icin gereken ardisik hata sayisi.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Devre actiktan sonra tekrar tek bir deneme (yari-acik) icin beklenecek sure.
    /// </summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>Model saglayicisi saglik denetiminin ayarlari.</summary>
/// <remarks>
/// Denetim <c>GET {endpoint}/models</c> ucuna gider ve ucret uretmez. Sonuc
/// onbelleklenir; <c>/api/models/health</c> ucu her acilista saglayiciya gitmez.
/// </remarks>
public sealed class AgentPrismHealthOptions
{
    /// <summary>Bir denetim sonucunun onbellekte tazeliğini koruyacagi sure.</summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Arka planda otomatik denetim araligi. <see langword="null"/> ise (varsayilan)
    /// arka plan zamanlayicisi hic calismaz; denetim yalnizca aray uzden "simdi
    /// denetle" ile veya <c>/api/models/health</c> ucu cagrildiginda tetiklenir.
    /// </summary>
    /// <remarks>
    /// Bos birakildi: bosta duran bir kurulumun saglayiciya duzenli istek atmasi
    /// istenmeyen bir varsayilandir. Gerekce: <c>docs/08-SAGLAYICI-GENISLEMESI.md</c>,
    /// acik soru 3.
    /// </remarks>
    public TimeSpan? BackgroundInterval { get; set; }
}

/// <summary>
/// Telemetri toplama ve kalicilastirma ayarlari.
/// </summary>
/// <remarks>
/// AgentPrism <strong>akisi ele gecirmez</strong>: tuketicinin kendi OTLP
/// exporter'i calismaya devam eder. Buradaki ayarlar yalnizca AgentPrism'in
/// <em>kendi</em> span deposuna ne yazacagini belirler.
/// </remarks>
public sealed class AgentPrismObservabilityOptions
{
    /// <summary>
    /// Span ve metrik uretimi acik mi. Kapatilirsa hicbir <c>Activity</c>
    /// baslatilmaz ve hicbir olcum kaydedilmez.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Span'ler <see cref="ITraceStore"/> icine yazilsin mi. Kapatildiginda
    /// span'ler yine uretilir ve tuketicinin exporter'ina gider; yalnizca
    /// AgentPrism'in kendi deposuna yazilmaz.
    /// </summary>
    public bool PersistSpans { get; set; } = true;

    /// <summary>
    /// Basarili calistirmalarin ne kadarinin span'lerinin yazilacagi (0–1).
    /// Varsayilan 0,1 yani onda biri.
    /// </summary>
    /// <remarks>
    /// Her span'i yazmak yuksek hacimde veritabanini darbogaza sokar. Hata
    /// ayiklama icin degerli olan hatali calistirmalardir; onlar
    /// <see cref="AlwaysPersistFailures"/> ile ayrica korunur.
    /// </remarks>
    public double SuccessSampleRatio { get; set; } = 0.1;

    /// <summary>
    /// Hata ile biten calistirmalarin span'leri ornekleme oranina bakilmaksizin
    /// yazilsin mi.
    /// </summary>
    public bool AlwaysPersistFailures { get; set; } = true;

    /// <summary>
    /// Tek bir calistirma icin bellekte tutulacak ust span sayisi. Asan span'ler
    /// atilir ve bir uyari loglanir.
    /// </summary>
    /// <remarks>
    /// Ornekleme karari calistirma <em>bittiginde</em> verilir (basarili mi
    /// hatali mi bilinmelidir), bu yuzden span'ler o ana kadar bellekte tutulur.
    /// Bu sinir, bellek kullanimini es zamanli calistirma sayisiyla carpimla
    /// sinirlandirir.
    /// </remarks>
    public int MaxSpansPerRun { get; set; } = 200;

    /// <summary>
    /// Istem ve yanit metinleri span'lere yazilsin mi.
    /// <strong>Varsayilan kapali</strong> — bu icerikler kisisel veri tasiyabilir.
    /// </summary>
    public bool RecordSensitiveData { get; set; }
}

/// <summary>Calistirma kaydinin ne kadar ayrinti tutacagini belirler.</summary>
public sealed class AgentPrismRunRecordingOptions
{
    /// <summary>Calistirma kaydi acik mi. Kapatilirsa hicbir olay yazilmaz.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Akisli calistirmalarda her metin parcasi ayri bir olay olarak yazilsin mi.
    /// Kapatilirsa yalnizca tamamlanan mesajlar kaydedilir; olay hacmi ciddi olcude duser.
    /// </summary>
    public bool RecordMessageDeltas { get; set; } = true;

    /// <summary>Tool argumanlari ve sonuclari kaydedilsin mi.</summary>
    /// <remarks>
    /// Tool argumanlari kisisel veri tasiyabilir. Bu bayrak, veri saklama
    /// politikasi geregi kapatilabilir.
    /// </remarks>
    public bool RecordToolPayloads { get; set; } = true;

    /// <summary>
    /// Tek bir olay yukunun ust karakter siniri. Asan yukler kirpilir ve
    /// sonuna kirpildigini belirten bir isaret eklenir.
    /// </summary>
    public int MaxPayloadLength { get; set; } = 8 * 1024;
}
