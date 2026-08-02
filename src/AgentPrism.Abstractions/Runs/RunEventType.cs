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
}
