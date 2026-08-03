using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Bir calistirma sirasinda uretilen olay tipleri. Arayuz bu tipleri dogrudan
/// gorsel ogelere esler, bu yuzden degerler kararli tutulmalidir.
/// </summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir (<c>"Code"</c>), sayi olarak degil.
/// Kablo sozlesmesi boylece kendini anlatir ve deger sirasi degisirse bile kirilmaz.
/// Donusturucu tip duzeyindedir: tuketicinin uygulama genelindeki JSON ayarlarina
/// dokunmadan her yerde ayni bicimi verir. Hicbir enum JSON olarak KALICI degildir
/// (RunStatus ve RunEventType veritabaninda smallint, AgentDefinitionOrigin okumada
/// yeniden kurulur), bu yuzden bicim degisikligi saklanan veriyi etkilemez.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunEventType>))]
public enum RunEventType
{
    /// <summary>Calistirma basladi.</summary>
    RunStarted = 0,

    /// <summary>Modelden metin parcasi geldi. Yalnizca akisli calistirmalarda uretilir.</summary>
    MessageDelta = 1,

    /// <summary>Bir mesaj tamamlandi.</summary>
    MessageCompleted = 2,

    /// <summary>Bir tool cagrilmak uzere. Argumanlar olay yukunde bulunur.</summary>
    ToolInvoking = 3,

    /// <summary>Bir tool basariyla tamamlandi. Sonuc olay yukunde bulunur.</summary>
    ToolInvoked = 4,

    /// <summary>Bir tool hata verdi.</summary>
    ToolFailed = 5,

    /// <summary>Calistirma basariyla tamamlandi.</summary>
    RunCompleted = 6,

    /// <summary>Calistirma hata ile sonlandi.</summary>
    RunFailed = 7,

    /// <summary>
    /// Bu calistirma bir alt agent calistirmasi baslatti.
    /// <c>Text</c> alt agent'in adini, <c>Payload</c> alt calistirmanin kimligini tasir.
    /// </summary>
    /// <remarks>
    /// Alt calistirmanin kendi olaylari kok akisa aynalanmaz; yalnizca basladigi
    /// ve bittigi bildirilir. Tam aynalama olay hacmini agac boyunca katlar ve
    /// istemciye ayni metni iki kez gonderir.
    /// </remarks>
    ChildRunStarted = 8,

    /// <summary>
    /// Baslatilan bir alt agent calistirmasi sonuclandi. <c>Text</c> alt agent'in
    /// adini, <c>Payload</c> alt calistirmanin kimligini tasir.
    /// </summary>
    ChildRunCompleted = 9,

    /// <summary>
    /// Konusma gecmisi sikistirildi. <c>Text</c> kisa bir ozet cumleyi,
    /// <c>Payload</c> once/sonra mesaj ve token sayilarini tasir.
    /// </summary>
    HistoryCompacted = 10,

    /// <summary>
    /// Bir workflow yurutmesi basladi. <c>Text</c> workflow adini tasir.
    /// </summary>
    /// <remarks>
    /// <see cref="RunStarted"/>'dan ayridir: o, <c>runs</c> satirinin acildigini
    /// bildirir; bu ise Microsoft Agent Framework yurutme motorunun grafi
    /// gercekten devraldigini bildirir. Ikisi arasinda derleme ve dogrulama yer
    /// alir ve orada olusan bir hata graf hic baslamadan calistirmayi bitirir.
    /// </remarks>
    WorkflowStarted = 11,

    /// <summary>
    /// Bir super-step basladi. <c>Text</c> adim numarasini, <c>Payload</c> mesaj
    /// gonderen executor adlarini tasir.
    /// </summary>
    SuperStepStarted = 12,

    /// <summary>
    /// Bir super-step tamamlandi. <c>Text</c> adim numarasini, <c>Payload</c>
    /// etkinlesen executor adlarini ve varsa kontrol noktasi kimligini tasir.
    /// </summary>
    SuperStepCompleted = 13,

    /// <summary>Bir executor cagrildi. <c>Text</c> executor kimligini tasir.</summary>
    ExecutorInvoked = 14,

    /// <summary>Bir executor tamamlandi. <c>Text</c> executor kimligini tasir.</summary>
    ExecutorCompleted = 15,

    /// <summary>
    /// Bir executor hata verdi. <c>Text</c> executor kimligini, <c>Payload</c>
    /// hata mesajini tasir.
    /// </summary>
    ExecutorFailed = 16,

    /// <summary>
    /// Workflow bir cikti uretti. <c>Text</c> ciktinin metin ozetini tasir.
    /// </summary>
    WorkflowOutput = 17,

    /// <summary>
    /// Workflow disaridan bir yanit bekliyor (human-in-the-loop).
    /// <c>Text</c> istek kimligini, <c>Payload</c> istegin JSON ozetini tasir.
    /// </summary>
    /// <remarks>
    /// Yuk, bekleyen istegi <em>yeniden kurmaya yetecek</em> kadar bilgi tasir:
    /// port kimligi, istek kimligi, istek/yanit tip adlari ve gosterilecek veri.
    /// Bekleyen istekler bu olaylardan okunur; ayri bir tablo acilmadi (Faz 16).
    /// </remarks>
    WorkflowRequest = 18,

    /// <summary>
    /// Calistirma bir insan yaniti bekledigi icin durdu. Akisin son olayidir.
    /// </summary>
    /// <remarks>
    /// <see cref="RunCompleted"/> ve <see cref="RunFailed"/>'dan ayridir: is ne
    /// bitmistir ne de basarisiz olmustur. Arayuz bu olayi gorunce bekleyen
    /// istek kartini gosterir. Faz 16'da eklendi.
    /// </remarks>
    RunAwaitingInput = 19,
}
