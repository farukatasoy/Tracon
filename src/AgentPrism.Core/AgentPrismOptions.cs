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
