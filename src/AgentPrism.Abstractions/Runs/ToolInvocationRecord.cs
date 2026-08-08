using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Tamamlanmis tek bir tool cagrisinin ozeti.
/// </summary>
/// <remarks>
/// <para>
/// Bu kayit olay akisinin bir <em>izdusumudur</em>: ayni bilgi
/// <see cref="RunEventType.ToolInvoking"/> ve <see cref="RunEventType.ToolInvoked"/>
/// olaylarinda da vardir. Ayri bir tabloda tutulmasinin sebebi
/// <see cref="Duration"/> ve tool bazli toplamlarin olay akisini bastan sona
/// taramadan sorgulanabilmesidir.
/// </para>
/// <para>
/// Kayit yalnizca cagri <strong>sonuclandiginda</strong> yazilir; suresi
/// <c>ToolInvoking</c> ve <c>ToolInvoked</c> olay ciftinin korelasyonundan
/// hesaplanir.
/// </para>
/// </remarks>
public sealed record ToolInvocationRecord
{
    /// <summary>Kayit kimligi. Zaman sirali UUID (v7).</summary>
    public required Guid Id { get; init; }

    /// <summary>Cagriyi yapan calistirma.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Cagrilan tool'un adi.</summary>
    public required string ToolName { get; init; }

    /// <summary>Modelin urettigi cagri kimligi. Ayni turda birden cok cagri ayirt edilir.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// Tool'un kaynagi. Kodda tanimli tool'lar icin <see langword="null"/>;
    /// MCP tool'larinda sunucu adi.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>Cagri argumanlari. Ham metin; gecerli JSON olmayabilir.</summary>
    public string? Arguments { get; init; }

    /// <summary>Cagri sonucu. Ham metin; gecerli JSON olmayabilir.</summary>
    public string? Result { get; init; }

    /// <summary>Cagri suresi. Olay cifti eslesmediyse <see langword="null"/>.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Hata mesaji. Yalnizca cagri hata verdiyse dolu.</summary>
    public string? Error { get; init; }

    /// <summary>Cagrinin sonuclandigi an (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Cagrinin token DISI olcumu ve maliyeti. Tool olcum bildirmediyse
    /// <see langword="null"/> — yani cagrilarin buyuk cogunlugunda bostur.
    /// </summary>
    /// <remarks>
    /// Deger <c>AgentPrismToolUsage.Report(...)</c> ile tool'un kendi govdesinden
    /// bildirilir ve cagri kimligiyle bu kayda baglanir. Gerekce:
    /// <c>docs/28-SES-TOOLLARI.md</c>, bolum 28.5.
    /// </remarks>
    public ToolCallUsage? Usage { get; init; }

    /// <summary>
    /// Cagriyi yapan calistirmanin BEKLENEN kiracisi. Derinlemesine savunma;
    /// <see langword="null"/> ise kiraci denetimi yapilmaz.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gerekce ve ambient kiracinin neden kullanilmadigi:
    /// <see cref="RunEvent.TenantId"/>. Karar K-355.
    /// </para>
    /// <para>
    /// 🚨 Alan YALNIZ YAZMA tarafindadir: bir sutuna yazilmaz, yalnizca yazmanin
    /// <c>WHERE</c> muhafizi olarak kullanilir. Geri okundugunda her zaman
    /// <see langword="null"/> olurdu; bu yuzden HTTP sozlesmesinden
    /// <see cref="JsonIgnoreAttribute"/> ile cikarilir. Aksi hâlde OpenAPI
    /// belgesi hicbir zaman dolmayan bir alan ilan ederdi.
    /// </para>
    /// </remarks>
    [JsonIgnore]
    public string? TenantId { get; init; }

    /// <summary>Cagri basarili mi bitti.</summary>
    public bool Succeeded => Error is null;
}

/// <summary>Tool kullanim ozetinin filtresi.</summary>
public sealed record ToolUsageQuery
{
    /// <summary>Kiraci filtresi. Bos birakilirsa gecerli kiraci kullanilir.</summary>
    public string? TenantId { get; init; }

    /// <summary>Yalnizca bu andan sonra baslayan calistirmalarin cagrilari sayilir.</summary>
    public DateTimeOffset? StartedAfter { get; init; }

    /// <summary>Dondurulecek ust tool sayisi. Cagri sayisi yuksek olanlar oncelikli.</summary>
    public int MaxTools { get; init; } = 50;
}

/// <summary>Bir tool'un kullanim ozeti. Arayuzdeki Tools ekrani bunu gosterir.</summary>
public sealed record ToolUsage
{
    /// <summary>Tool adi.</summary>
    public required string ToolName { get; init; }

    /// <summary>Toplam cagri sayisi.</summary>
    public required long TotalCalls { get; init; }

    /// <summary>Hata veren cagri sayisi.</summary>
    public required long FailedCalls { get; init; }

    /// <summary>Ortalama sure (milisaniye). Hicbir cagri sure tasimiyorsa <see langword="null"/>.</summary>
    public double? AverageDurationMs { get; init; }

    /// <summary>En son cagrildigi an (UTC).</summary>
    public DateTimeOffset? LastCalledAt { get; init; }

    /// <summary>
    /// Sonuclanmis cagrilar icindeki hata orani (0–1). Hic cagri yoksa
    /// <see langword="null"/>.
    /// </summary>
    public double? ErrorRate => TotalCalls == 0 ? null : (double)FailedCalls / TotalCalls;
}
