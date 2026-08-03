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

    /// <summary>Agent'in agent cagirmasi icin gecerli sinirlar.</summary>
    public AgentPrismAgentGraphOptions AgentGraph { get; set; } = new();

    /// <summary>Ek (goruntu, ses, belge) yukleme sinirlari.</summary>
    public AgentPrismAttachmentOptions Attachments { get; set; } = new();

    /// <summary>Calistirma maliyetinin fiyat kaynagi (Faz 20).</summary>
    public AgentPrismPricingOptions Pricing { get; set; } = new();

    /// <summary>
    /// Baglam sikistirmasinda ozetleme icin kullanilacak varsayilan model.
    /// </summary>
    /// <remarks>
    /// Bir agent tanimi kendi <c>CompactionSettings.SummarizationModel</c>'ini
    /// vermezse bu deger kullanilir; o da bos ise agent'in kendi modeli
    /// ozetleme icin de kullanilir. Amac: ozetleme gibi ucuz bir is icin
    /// pahali bir modelle maliyet uretmemek.
    /// </remarks>
    public ModelBinding? UtilityModel { get; set; }
}

/// <summary>
/// Bir agent baska bir agent'i cagirdiginda gecerli olan sinirlar.
/// </summary>
/// <remarks>
/// <para>
/// Alt agent cagrisi maliyeti <strong>carpar</strong>: her katman kendi model
/// cagrilarini yapar. Sinirsiz birakilirsa yanlis yazilmis tek bir tanim, tek bir
/// istekte onlarca calistirma baslatabilir. Bu yuzden hem derinlik hem token hem
/// de sayi sinirinin varsayilan bir degeri vardir.
/// </para>
/// <para>
/// Sinirlar <em>agac basina</em> uygulanir: kok calistirma bir
/// <see cref="AgentRunBudget"/> uretir, agactaki her calistirma ayni ornegi
/// paylasir.
/// </para>
/// </remarks>
public sealed class AgentPrismAgentGraphOptions
{
    /// <summary>
    /// Izin verilen en buyuk cagri derinligi. Kok calistirma 0'dir, dolayisiyla
    /// varsayilan deger uc katmanli bir agaca izin verir.
    /// </summary>
    public int MaxDepth { get; set; } = 3;

    /// <summary>
    /// Bir agac boyunca harcanabilecek en fazla token. 0 veya negatif deger
    /// sinirlamayi kaldirir.
    /// </summary>
    /// <remarks>
    /// Varsayilan deger bilerek <em>vardir</em>. Sinirsiz birakilan bir kurulumda
    /// ilk yanlis tanim faturayla ogrenilir.
    /// </remarks>
    public long MaxTotalTokens { get; set; } = 200_000;

    /// <summary>
    /// Bir agac boyunca baslatilabilecek en fazla <em>alt</em> calistirma sayisi.
    /// Kok calistirma sayilmaz. 0 veya negatif deger sinirlamayi kaldirir.
    /// </summary>
    public int MaxTotalRuns { get; set; } = 25;

    /// <summary>Bu ayarlardan yeni bir agac butcesi uretir.</summary>
    /// <returns>Kok calistirmanin agac boyunca paylasacagi butce.</returns>
    public AgentRunBudget CreateBudget()
        => new()
        {
            MaxDepth = Math.Max(MaxDepth, 0),
            MaxTotalTokens = MaxTotalTokens > 0 ? MaxTotalTokens : null,
            MaxTotalRuns = MaxTotalRuns > 0 ? MaxTotalRuns : null,
        };
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

    /// <summary>Script calistirma ayarlari. Varsayilan olarak kapalidir.</summary>
    public AgentPrismSkillScriptOptions Scripts { get; set; } = new();
}

/// <summary>
/// Skill script'lerinin sunucuda calistirilmasini yoneten ayarlar.
/// </summary>
/// <remarks>
/// <para>
/// <strong>AgentPrism isletim sistemi duzeyinde yalitim saglamaz.</strong> Ag
/// erisimini kesmek, dosya sistemini gercek anlamda kisitlamak, CPU ve bellek
/// kotasi uygulamak ve ayricalik dusurmek .NET ile tasinabilir bicimde
/// yapilamaz. Bunlar barindirma ortaminin isidir: script calistirma acikken
/// AgentPrism <em>container icinde, ayricaliksiz bir kullaniciyla ve kisitli ag
/// ile</em> calistirilmalidir.
/// </para>
/// <para>
/// <see cref="PlatformIsolationAcknowledged"/> bu sinirin okundugunu bildiren
/// bilincli onay adimidir; ayarlanmadan <see cref="Enabled"/> acilamaz ve
/// uygulama acilista hata verir.
/// </para>
/// </remarks>
public sealed class AgentPrismSkillScriptOptions
{
    /// <summary>Script calistirma acik mi. Varsayilan <see langword="false"/>.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Tuketicinin isletim sistemi yalitiminin AgentPrism tarafindan
    /// saglanmadigini kabul ettigini bildirir.
    /// </summary>
    public bool PlatformIsolationAcknowledged { get; set; }

    /// <summary>
    /// Veritabaninda saklanan script'ler calistirilabilir mi.
    /// Varsayilan <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Acilirsa aray uze erisen bir Admin sunucuda calisacak kodu yazabilir.
    /// Uc kapi birden gereklidir: bu bayrak, Admin rolu ve izin kaydi.
    /// </remarks>
    public bool AllowStoredScripts { get; set; }

    /// <summary>
    /// Diskteki skill dizinlerinin arandigi kokler.
    /// </summary>
    /// <remarks>
    /// Kokler <strong>kodda veya yapilandirmada</strong> verilir; arayuzden
    /// degistirilemez. Script icerigini yazan kisi, uygulamayi dagitan kisidir.
    /// </remarks>
    public IList<string> SkillRoots { get; } = [];

    /// <summary>
    /// Uzantidan yorumlayici yoluna beyaz liste. Ornek: <c>["py"] = "/usr/bin/python3"</c>.
    /// </summary>
    /// <remarks>
    /// Liste bos oldugu surece <strong>hicbir script calismaz</strong>. Uzanti
    /// noktasiz ve kucuk harfle yazilir.
    /// </remarks>
    public IDictionary<string, string> Interpreters { get; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Script surecine aktarilacak ortam degiskenlerinin beyaz listesi.
    /// </summary>
    /// <remarks>
    /// Listede olmayan hicbir degisken aktarilmaz. Baglanti dizesi ve API
    /// anahtari bu sayede surece hic ulasmaz.
    /// </remarks>
    public IList<string> EnvironmentAllowList { get; } = ["PATH", "HOME"];

    /// <summary>Tek bir script'in calisabilecegi en uzun sure.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>stdout ve stderr toplaminin en fazla bayt sayisi. Asan kisim kirpilir.</summary>
    public int MaxOutputBytes { get; set; } = 256 * 1024;

    /// <summary>Script'e verilecek JSON argumanlarinin en fazla bayt sayisi.</summary>
    public int MaxArgumentBytes { get; set; } = 16 * 1024;

    /// <summary>Veritabaninda saklanan bir script icerigi icin en fazla bayt sayisi.</summary>
    public int MaxScriptContentLength { get; set; } = 64 * 1024;

    /// <summary>Bir skill'in tasiyabilecegi en fazla script sayisi.</summary>
    public int MaxScriptsPerSkill { get; set; } = 10;

    /// <summary>Ayni anda calisabilecek script sayisi, kiraci basina.</summary>
    public int MaxConcurrentPerTenant { get; set; } = 2;

    /// <summary>Ayni anda calisabilecek toplam script sayisi.</summary>
    public int MaxConcurrentTotal { get; set; } = 8;

    /// <summary>Skill koklerinde inilecek en fazla dizin derinligi.</summary>
    public int SearchDepth { get; set; } = 2;
}

/// <summary>
/// Ek yukleme sinirlari: boyut ve tur beyaz listesi.
/// </summary>
/// <remarks>
/// Ikili icerik <c>attachments</c> tablosunda, mesajda yalnizca referans olarak
/// yasar; bu ayarlar YALNIZ yukleme aninda uygulanir. Tur denetimi istemcinin
/// bildirdigi <c>Content-Type</c>'a degil sihirli bayta dayanir — bkz.
/// <see cref="AttachmentTypeGuard"/>. Gerekce: <c>docs/14-COK-MODLULUK.md</c>.
/// </remarks>
public sealed class AgentPrismAttachmentOptions
{
    /// <summary>Tek bir ekin en fazla bayt sayisi. Varsayilan 20 MB.</summary>
    public long MaxBytes { get; set; } = 20 * 1024 * 1024;

    /// <summary>
    /// Izin verilen MIME turleri. <c>"audio/*"</c> gibi bir alt tur joker
    /// karakteri kabul eder. Yurutulebilir icerik turleri BILEREK yoktur.
    /// </summary>
    public ISet<string> AllowedMediaTypes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp",
        "image/gif",
        "application/pdf",
        "text/plain",
        "audio/*",
    };
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

    /// <summary>
    /// <c>agentprism.agent.version</c> etiketi span'lere ve <c>agentprism.runs</c>/
    /// <c>agentprism.run.duration</c> metriklerine eklensin mi.
    /// </summary>
    /// <remarks>
    /// Varsayilan <see langword="true"/>'dur: surum numarasi zamanla artar ve agent
    /// basina onlarca zaman serisi uretir — kabul edilebilir bir kardinalite. Cok
    /// sik surum degistiren kurulumlarda kapatilabilir.
    /// </remarks>
    public bool IncludeAgentVersionTag { get; set; } = true;
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

/// <summary>
/// Calistirma maliyeti icin fiyat kaynagi. Model kataloguna (<see cref="ModelDescriptor"/>)
/// gore fiyati olmayan bir model icin ikincil bir kaynaktir.
/// </summary>
/// <remarks>
/// AgentPrism fiyat <strong>uydurmaz</strong> (karar K-032): burada yalnizca
/// tuketicinin yapilandirmadan verdigi degerler tutulur. Yapilandirma yolu
/// <c>AgentPrism:Pricing:{saglayici}:{model}:Input|Output</c> ve
/// <c>AgentPrism:Pricing:Currency</c> seklindedir; joker karakter desteklenmez.
/// </remarks>
public sealed class AgentPrismPricingOptions
{
    /// <summary>Raporlarda gosterilecek para birimi etiketi. Donusum yapilmaz.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Saglayici adindan, o saglayicinin model basina fiyat gecersiz kilmalarina
    /// eslenir.
    /// </summary>
    public IDictionary<string, IDictionary<string, ModelPriceOverride>> Providers { get; }
        = new Dictionary<string, IDictionary<string, ModelPriceOverride>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Yapilandirmadan verilen tek bir modelin fiyat gecersiz kilmasi.</summary>
public sealed class ModelPriceOverride
{
    /// <summary>Milyon girdi token'i basina maliyet.</summary>
    public decimal? InputCostPerMillionTokens { get; set; }

    /// <summary>Milyon cikti token'i basina maliyet.</summary>
    public decimal? OutputCostPerMillionTokens { get; set; }
}
