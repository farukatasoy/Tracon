using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir webhook teslim denemesinin durumu.</summary>
/// <remarks>
/// JSON'da ad olarak yazilir, veritabaninda <c>smallint</c> olarak saklanir.
/// Deger sirasi <strong>degistirilemez</strong> — yalnizca sona eklenir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<WebhookDeliveryStatus>))]
public enum WebhookDeliveryStatus
{
    /// <summary>Teslim kuyruga alindi, henuz denenmedi veya yeniden denenecek.</summary>
    Pending = 0,

    /// <summary>Alici <c>2xx</c> dondu.</summary>
    Delivered = 1,

    /// <summary>Tum denemeler tukendi.</summary>
    Failed = 2,

    /// <summary>Teslim hic denenmedi: abonelik kapali veya hedef adres reddedildi.</summary>
    Dropped = 3,
}

/// <summary>AgentPrism'in yayabilecegi olay turleri.</summary>
/// <remarks>
/// Olay adlari sozlesmenin parcasidir ve <strong>degistirilemez</strong>:
/// aboneler bu dizeleri kaydeder.
/// </remarks>
public static class WebhookEvents
{
    /// <summary>Bir calistirma basariyla bitti.</summary>
    public const string RunCompleted = "run.completed";

    /// <summary>Bir calistirma hatayla bitti.</summary>
    public const string RunFailed = "run.failed";

    /// <summary>Bir tool cagrisi onay bekliyor (Faz 6).</summary>
    public const string ApprovalPending = "approval.pending";

    /// <summary>Bir workflow insan girdisi bekliyor (Faz 16).</summary>
    public const string WorkflowRequestPending = "workflow.request.pending";

    /// <summary>Bir kuyruk isi basariyla bitti (Faz 17).</summary>
    public const string JobCompleted = "job.completed";

    /// <summary>Bir kuyruk isi hatayla bitti (Faz 17).</summary>
    public const string JobFailed = "job.failed";

    /// <summary>Bir degerlendirme kosusu bitti (Faz 18).</summary>
    public const string EvalCompleted = "eval.completed";

    /// <summary>Bir kota esigi asildi (%80 veya %100).</summary>
    public const string QuotaThreshold = "quota.threshold";

    /// <summary>
    /// Cevrimici degerlendirme penceresinin ortalama puani esigin altina dustu
    /// (Faz 49). Tek bir dusuk puan bu olayi TETIKLEMEZ — asgari ornek sayisi
    /// asilmis olmalidir.
    /// </summary>
    public const string RunScoreLow = "run.score.low";

    /// <summary>Abonelik ucunun dogrulanmasi icin gonderilen sinama olayi.</summary>
    public const string Test = "test.ping";

    /// <summary>Taninan tum olay adlari.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        RunCompleted,
        RunFailed,
        ApprovalPending,
        WorkflowRequestPending,
        JobCompleted,
        JobFailed,
        EvalCompleted,
        QuotaThreshold,
        RunScoreLow,
        Test,
    ];

    /// <summary>Bir olay adinin taninip taninmadigini bildirir.</summary>
    /// <param name="eventType">Olay adi.</param>
    /// <returns>Ad taniniyorsa <see langword="true"/>.</returns>
    public static bool IsKnown(string? eventType)
        => eventType is not null && All.Contains(eventType, StringComparer.Ordinal);
}

/// <summary>Bir dis sistemin olay aboneligi.</summary>
/// <remarks>
/// 🚨 Bu kayitta <strong>sir alani yoktur</strong>. Imzalama sirri
/// veritabaninda durmaz; yalnizca degerin okunacagi yapilandirma anahtarinin
/// adi (<see cref="SecretConfigurationKey"/>) durur ve deger calisma aninda
/// <c>IConfiguration</c> uzerinden cozulur (K-059).
/// </remarks>
public sealed record WebhookSubscription
{
    /// <summary>Abonelik kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>Aboneligin ait oldugu kiraci.</summary>
    public required string TenantId { get; init; }

    /// <summary>Kiraci icinde benzersiz ad.</summary>
    public required string Name { get; init; }

    /// <summary>Olaylarin gonderilecegi adres.</summary>
    /// <remarks>
    /// Adres SSRF denetiminden gecer: yalnizca <c>https</c> (veya loopback
    /// icin <c>http</c>), ozel ag araliklari reddedilir, yonlendirme izlenmez.
    /// </remarks>
    public required string Url { get; init; }

    /// <summary>Abone olunan olay adlari. Bkz. <see cref="WebhookEvents"/>.</summary>
    public required IReadOnlyList<string> Events { get; init; }

    /// <summary>
    /// Imzalama sirrinin okunacagi yapilandirma anahtarinin <strong>adi</strong>.
    /// Sirrin kendisi degildir.
    /// </summary>
    /// <remarks>
    /// Ornek: <c>"AgentPrism:Webhooks:Secrets:siparis-servisi"</c>. Deger
    /// <c>dotnet user-secrets</c> veya ortam degiskeninde yasar. Bos ise
    /// istekler imzalanmaz.
    /// </remarks>
    public string? SecretConfigurationKey { get; init; }

    /// <summary>Her istege eklenecek ek basliklar.</summary>
    /// <remarks>
    /// Kimlik dogrulama basligi <strong>buraya yazilmaz</strong>: deger
    /// veritabaninda saklanirdi. Imzalama icin
    /// <see cref="SecretConfigurationKey"/> kullanin.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Abonelik etkin mi.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Ust uste basarisiz teslim sayisi. Esik asilinca abonelik kendiliginden
    /// kapanir ve denetim izine yazilir.
    /// </summary>
    public int ConsecutiveFailures { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Son guncelleme zamani (UTC).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Tek bir teslim kaydi.</summary>
/// <remarks>
/// Bu kayit bir <strong>kuyruk satiri degildir</strong>: zamanlama, kiralama ve
/// yeniden deneme Faz 17'nin <c>jobs</c> tablosunda yasar (K-160). Buradaki
/// <see cref="Attempt"/> yalnizca gecmisi raporlar.
/// </remarks>
public sealed record WebhookDelivery
{
    /// <summary>Teslim kimligi. Istekte <c>X-AgentPrism-Delivery</c> basligi olarak gider.</summary>
    public required Guid Id { get; init; }

    /// <summary>Aboneligin kimligi.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Olay adi.</summary>
    public required string EventType { get; init; }

    /// <summary>Gonderilen JSON govde.</summary>
    /// <remarks>
    /// Yalnizca <strong>ozet</strong> tasir: kimlik, durum, agent, token,
    /// maliyet. Mesaj icerikleri hicbir zaman burada durmaz (K-161).
    /// </remarks>
    public required string Payload { get; init; }

    /// <summary>Teslimin durumu.</summary>
    public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>Yapilan deneme sayisi.</summary>
    public int Attempt { get; init; }

    /// <summary>Alicinin dondugu HTTP durum kodu. Baglanti kurulamadiysa <see langword="null"/>.</summary>
    public int? ResponseCode { get; init; }

    /// <summary>Son hata mesaji.</summary>
    public string? Error { get; init; }

    /// <summary>Olusturulma zamani (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Basarili teslim zamani (UTC).</summary>
    public DateTimeOffset? DeliveredAt { get; init; }
}

/// <summary>Bir teslim denemesinin sonucu.</summary>
public sealed record WebhookDeliveryResult
{
    /// <summary>Teslim kimligi.</summary>
    public required Guid DeliveryId { get; init; }

    /// <summary>Son durum.</summary>
    public required WebhookDeliveryStatus Status { get; init; }

    /// <summary>Deneme sirasi.</summary>
    public required int Attempt { get; init; }

    /// <summary>Alicinin dondugu HTTP durum kodu.</summary>
    public int? ResponseCode { get; init; }

    /// <summary>Hata mesaji.</summary>
    public string? Error { get; init; }

    /// <summary>Sonucun kaydedildigi an (UTC).</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}

/// <summary>Teslim gecmisini filtrelemek icin sorgu.</summary>
public sealed record WebhookDeliveryQuery
{
    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Yalnizca bu aboneligin teslimlerini getirir.</summary>
    public Guid? SubscriptionId { get; init; }

    /// <summary>Yalnizca bu durumdaki teslimleri getirir.</summary>
    public WebhookDeliveryStatus? Status { get; init; }

    /// <summary>Atlanacak kayit sayisi.</summary>
    public int Skip { get; init; }

    /// <summary>Getirilecek ust kayit sayisi.</summary>
    public int Take { get; init; } = 50;
}
