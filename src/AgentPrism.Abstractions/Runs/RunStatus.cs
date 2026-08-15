using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir calistirmanin durumu.</summary>
/// <remarks>
/// JSON'da <strong>ad olarak</strong> yazilir (<c>"Code"</c>), sayi olarak degil.
/// Kablo sozlesmesi boylece kendini anlatir ve deger sirasi degisirse bile kirilmaz.
/// Donusturucu tip duzeyindedir: tuketicinin uygulama genelindeki JSON ayarlarina
/// dokunmadan her yerde ayni bicimi verir. Hicbir enum JSON olarak KALICI degildir
/// (RunStatus ve RunEventType veritabaninda smallint, AgentDefinitionOrigin okumada
/// yeniden kurulur), bu yuzden bicim degisikligi saklanan veriyi etkilemez.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunStatus>))]
public enum RunStatus
{
    /// <summary>Calistirma devam ediyor.</summary>
    Running = 0,

    /// <summary>Calistirma basariyla tamamlandi.</summary>
    Completed = 1,

    /// <summary>Calistirma hata ile sonlandi.</summary>
    Failed = 2,

    /// <summary>Calistirma iptal edildi.</summary>
    Canceled = 3,

    /// <summary>
    /// Calistirma bir insandan girdi bekliyor ve bu girdi gelmeden ilerleyemez.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Yalnizca <see cref="RunKind.Workflow"/> satirlarinda gorulur. Workflow bir
    /// <em>dis istek portu</em>na ulastiginda (ornegin Magentic plan onayi)
    /// yurutme durur, durumu bir kontrol noktasina yazilir ve akis kapanir.
    /// Yanit <c>POST /api/workflows/runs/{runId}/respond</c> ile verilir; bu,
    /// kontrol noktasindan devam eden <strong>yeni</strong> bir calistirma acar.
    /// </para>
    /// <para>
    /// Deger sona eklenmistir: durumlar veritabaninda <c>smallint</c> olarak
    /// saklanir ve mevcut degerlerin kaymasi eski satirlari yanlis okurdu.
    /// Yanitlanmis bir calistirma <c>AwaitingInput</c> olarak <em>kalir</em>;
    /// gecmisi geriye donuk degistirmek olay akisinin append-only kuralini
    /// (K-014) bozardi. Devam eden is, yeni calistirma satirinda gorulur.
    /// </para>
    /// </remarks>
    AwaitingInput = 4,

    /// <summary>
    /// Calistirma kuyruga alindi ama isci henuz baslatmadi.
    /// </summary>
    /// <remarks>
    /// Yalnizca <c>Prefer: respond-async</c> ile baslatilan calistirmalarda
    /// gorulur (Faz 46). Satir kuyruga alma aninda <c>Queued</c> olarak yazilir;
    /// isci is'i gercekten calistirdiginda ayni kimlikle tekrar yazilir ve
    /// <see cref="Running"/>'e gecer. Deger sona eklenmistir; gerekce
    /// <see cref="AwaitingInput"/> aciklamasindaki ile aynidir.
    /// </remarks>
    Queued = 5,

    /// <summary>
    /// Calistirma bir tool cagrisi icin operator onayi bekliyor ve bu karar
    /// gelmeden ilerleyemez.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Herhangi bir <em>kok</em> (<c>Depth == 0</c>) agent calistirmasinda gorulur
    /// — yonetim API'si, OpenAI-uyumlu uc, MCP, A2A veya kuyruktan kosan
    /// (<c>Prefer: respond-async</c>, Faz 46) fark etmeksizin (HATA-S2-004/
    /// MT-MCP-023, Faz 55'in yalniz kuyruk yoluna ozgu ilk halini genisletti:
    /// senkron yollar oncesinde bu durumu hic yansitmiyor, onay bekleyen bir
    /// tool cagrisi sessizce <see cref="Completed"/> gorunuyordu). Karar nasil
    /// ulastirilirsa ulastirilsin (kuyruk icin <c>POST /api/approvals/{id}/decide</c>,
    /// senkron cagiran icin kendi bir sonraki turu) durum etiketi ayni ilkeyi
    /// izler.
    /// </para>
    /// <para>
    /// <see cref="AwaitingInput"/> ile AYNI ilkeyi izler: yanitlanmis bir
    /// calistirma bu durumda <em>kalir</em>, gecmisi geriye donuk degistirmek
    /// olay akisinin append-only kuralini (K-014) bozardi. Kuyruk yolunda karar
    /// <c>POST /api/approvals/{id}/decide</c> ile verilir; bu, <strong>yeni</strong>
    /// bir calistirma kuyruga dusurur (ayni <c>sessionId</c>, yeni <c>RunId</c>).
    /// </para>
    /// <para>
    /// Deger sona eklenmistir; gerekce <see cref="AwaitingInput"/> aciklamasindaki
    /// ile aynidir.
    /// </para>
    /// </remarks>
    AwaitingApproval = 6,
}
