using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir agent tanimin nereden geldigini bildirir.</summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir (<c>"Code"</c>), sayi olarak degil.
/// Kablo sozlesmesi boylece kendini anlatir ve deger sirasi degisirse bile kirilmaz.
/// Donusturucu tip duzeyindedir: tuketicinin uygulama genelindeki JSON ayarlarina
/// dokunmadan her yerde ayni bicimi verir. Hicbir enum JSON olarak KALICI degildir
/// (RunStatus ve RunEventType veritabaninda smallint, AgentDefinitionOrigin okumada
/// yeniden kurulur), bu yuzden bicim degisikligi saklanan veriyi etkilemez.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AgentDefinitionOrigin>))]
public enum AgentDefinitionOrigin
{
    /// <summary>
    /// Tanim kodda yapilmistir. Derleme zamaninda dogrulanmistir, bu yuzden ad
    /// cakismasinda veritabani tanimina karsi oncelik kazanir.
    /// </summary>
    Code = 0,

    /// <summary>Tanim veritabaninda saklanir ve calisma aninda derlenir.</summary>
    Database = 1,
}
