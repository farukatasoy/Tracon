using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Bir calistirma hatasinin sinifi.
/// </summary>
/// <remarks>
/// <para>
/// Taksonomi kucuk ve kararli tutulur. <see cref="Unknown"/> bir basarisizlik
/// degil bir olcum aracidir: bir sinifcandirici hicbir kurala uymayan bir
/// hatayi TAHMIN ETMEZ, bu kovaya yazar. Oraninin yuksek olmasi taksonominin
/// eksik oldugunu gosterir.
/// </para>
/// <para>
/// JSON'da <strong>ad olarak</strong> yazilir; veritabaninda <c>smallint</c>
/// olarak saklanir (diger tum <c>runs</c> enum'lariyla ayni desen, ornek
/// <see cref="RunKind"/>). Sayisal degerler bir kez atandiktan sonra
/// <strong>asla yeniden numaralanmaz</strong>: gecmis satirlarda kalici olarak
/// saklanirlar. Yeni bir sinif eklerken siraya degil, listenin sonuna eklenir.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunErrorClass>))]
public enum RunErrorClass
{
    /// <summary>Hicbir kurala uymadi. Bir basarisizlik degil, bir olcum aracidir.</summary>
    Unknown = 0,

    /// <summary>Model saglayicisi hata dondurdu (5xx, baglanti, zaman asimi disi).</summary>
    ProviderError = 1,

    /// <summary>Devre kesici acik (<see cref="AgentPrismProviderUnavailableException"/>).</summary>
    ProviderUnavailable = 2,

    /// <summary>Saglayici 429 dondurdu.</summary>
    RateLimited = 3,

    /// <summary>AgentPrism kotasi doldu.</summary>
    QuotaExceeded = 4,

    /// <summary>Model yaniti guvenlik/icerik filtresiyle kesildi.</summary>
    ContentFiltered = 5,

    /// <summary>Bir tool istisna firlatti.</summary>
    ToolError = 6,

    /// <summary>Calistirma sure sinirini asti.</summary>
    Timeout = 7,

    /// <summary>Agent tanimi derlenemedi.</summary>
    CompilationFailed = 8,

    /// <summary>Agac veya baglam butcesi asildi.</summary>
    BudgetExceeded = 9,

    /// <summary>Iptal edildi.</summary>
    Canceled = 10,
}
