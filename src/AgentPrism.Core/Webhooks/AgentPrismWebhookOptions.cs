namespace AgentPrism;

/// <summary>Olay yayini (webhook) ayarlari — Faz 21.</summary>
/// <remarks>
/// <c>AgentPrism:Webhooks</c> yapilandirma bolumunden okunur.
/// </remarks>
public sealed class AgentPrismWebhookOptions
{
    /// <summary>Yapilandirma bolumu adi.</summary>
    public const string SectionName = "AgentPrism:Webhooks";

    /// <summary>
    /// Olay yayini etkin mi. Kapaliyken abonelik kaydedilebilir ama hicbir olay
    /// kuyruga yazilmaz.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 🚨 Ozel ag adreslerine teslim yapilabilir mi. <strong>Varsayilan kapali.</strong>
    /// </summary>
    /// <remarks>
    /// Acmak SSRF yuzeyini acar: sunucu, ic agdaki herhangi bir servise ve
    /// bulut metadata ucuna (<c>169.254.169.254</c>) istek atabilir hale gelir.
    /// Yalnizca kapali bir agda, bilincli olarak acilmalidir.
    /// </remarks>
    public bool AllowPrivateNetworkTargets { get; set; }

    /// <summary>
    /// Sifrelenmemis <c>http</c> hedeflerine izin verilir mi. Izin verilse bile
    /// yalnizca <strong>loopback</strong> adresleri kabul edilir.
    /// </summary>
    /// <remarks>
    /// Yerel gelistirmede bir dinleyiciyi sinamak icin gerekir. Loopback
    /// kisiti kaldirilmaz: disari acik bir adrese sifrelenmemis olay gondermek
    /// olay ozetini aga acar.
    /// </remarks>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>Tek bir teslim denemesinin zaman asimi.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Alicinin yanit govdesinden okunacak en fazla bayt.</summary>
    /// <remarks>
    /// Yanit govdesi yalnizca tanilama icin saklanir. Sinirsiz okumak, kotu
    /// niyetli bir alicinin bellegi tuketmesine izin verirdi.
    /// </remarks>
    public int MaxResponseBytes { get; set; } = 8 * 1024;

    /// <summary>
    /// Bir abonelik bu sayida ust uste basarisiz olursa kendiliginden devre disi
    /// kalir ve denetim izine yazilir.
    /// </summary>
    public int DisableAfterConsecutiveFailures { get; set; } = 20;

    /// <summary>
    /// Yeniden deneme merdiveni. Liste uzunlugu ayni zamanda en fazla deneme
    /// sayisidir.
    /// </summary>
    /// <remarks>
    /// Bu merdiven Faz 17'nin kuyruguna <c>ReleaseForRetryAsync(retryAfter)</c>
    /// ile verilir; ikinci bir kuyruk veya zamanlayici yazilmaz (K-160).
    /// </remarks>
    public IList<TimeSpan> RetryDelays { get; } =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(6),
    ];

    /// <summary>
    /// Alicinin kabul edecegi imza zaman damgasi toleransi. README'de
    /// aliciya onerilen degerdir; AgentPrism bunu yalnizca belgeler.
    /// </summary>
    public TimeSpan SignatureTolerance { get; set; } = TimeSpan.FromMinutes(5);
}
