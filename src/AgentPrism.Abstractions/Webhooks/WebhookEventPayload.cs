using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Bir webhook isteginin JSON govdesi.</summary>
/// <remarks>
/// <para>
/// 🚨 Bu yuk yalnizca <strong>ozet</strong> tasir (K-161): kimlik, durum, agent,
/// token, maliyet. Mesaj icerikleri ve model yanitlari <strong>hicbir zaman</strong>
/// burada durmaz. Icerik isteyen alici <c>GET {prefix}/api/runs/{id}</c> cagirir.
/// </para>
/// <para>
/// Sebep iki katmanlidir: (1) yuk dis bir sisteme gider ve hassas veri
/// tasiyamaz; (2) ayni metin <c>webhook_deliveries.payload</c> sutununda da
/// saklanir — icerik tasisaydi veritabani yedegi de hassas veri tasirdi.
/// </para>
/// </remarks>
public sealed record WebhookEventPayload
{
    /// <summary>Olay adi. Yayinci doldurur.</summary>
    public string? Event { get; init; }

    /// <summary>Teslim kimligi. Yayinci doldurur.</summary>
    public string? DeliveryId { get; init; }

    /// <summary>Kiraci kimligi. Yayinci doldurur.</summary>
    public string? TenantId { get; init; }

    /// <summary>Olayin gerceklestigi an (UTC). Yayinci doldurur.</summary>
    public DateTimeOffset? OccurredAt { get; init; }

    /// <summary>Calistirma ozeti. <c>run.*</c> olaylarinda dolu.</summary>
    public WebhookRunSummary? Run { get; init; }

    /// <summary>Kuyruk isi ozeti. <c>job.*</c> olaylarinda dolu.</summary>
    public WebhookJobSummary? Job { get; init; }

    /// <summary>Onay ozeti. <c>approval.pending</c> ve <c>workflow.request.pending</c> olaylarinda dolu.</summary>
    public WebhookApprovalSummary? Approval { get; init; }

    /// <summary>Kota ozeti. <c>quota.threshold</c> olayinda dolu.</summary>
    public WebhookQuotaSummary? Quota { get; init; }

    /// <summary>Puan penceresi ozeti. <c>run.score.low</c> olayinda dolu.</summary>
    public WebhookScoreSummary? Score { get; init; }
}

/// <summary>Bir calistirmanin webhook ozeti.</summary>
public sealed record WebhookRunSummary
{
    /// <summary>Calistirma kimligi.</summary>
    public required string RunId { get; init; }

    /// <summary>Agac kokunun kimligi. Kok calistirmada kendisiyle aynidir.</summary>
    public string? RootRunId { get; init; }

    /// <summary>Oturum kimligi.</summary>
    public string? SessionId { get; init; }

    /// <summary>Calistirmayi yapan agent'in adi.</summary>
    public string? AgentName { get; init; }

    /// <summary>Kullanilan modelin kimligi.</summary>
    public string? ModelId { get; init; }

    /// <summary>Son durum.</summary>
    public string? Status { get; init; }

    /// <summary>Sure (milisaniye).</summary>
    public long? DurationMs { get; init; }

    /// <summary>Girdi token'i.</summary>
    public long? InputTokens { get; init; }

    /// <summary>Cikti token'i.</summary>
    public long? OutputTokens { get; init; }

    /// <summary>Toplam tutar. Fiyat tanimsizsa <see langword="null"/> — sifir degil.</summary>
    public decimal? Cost { get; init; }

    /// <summary>Tutarin para birimi.</summary>
    public string? Currency { get; init; }

    /// <summary>Hata mesaji. <c>run.failed</c> olayinda dolu.</summary>
    public string? Error { get; init; }
}

/// <summary>Bir kuyruk isinin webhook ozeti.</summary>
public sealed record WebhookJobSummary
{
    /// <summary>Is kimligi.</summary>
    public required string JobId { get; init; }

    /// <summary>Isin turu.</summary>
    public string? Kind { get; init; }

    /// <summary>Calistirilan agent veya workflow adi.</summary>
    public string? TargetName { get; init; }

    /// <summary>Son durum.</summary>
    public string? Status { get; init; }

    /// <summary>Toplam oge sayisi.</summary>
    public int TotalItems { get; init; }

    /// <summary>Tamamlanan oge sayisi.</summary>
    public int DoneItems { get; init; }

    /// <summary>Basarisiz oge sayisi.</summary>
    public int FailedItems { get; init; }

    /// <summary>Hata mesaji.</summary>
    public string? Error { get; init; }
}

/// <summary>Bekleyen bir onayin veya insan girdisi isteginin webhook ozeti.</summary>
public sealed record WebhookApprovalSummary
{
    /// <summary>Onay veya istek kimligi.</summary>
    public required string RequestId { get; init; }

    /// <summary>Ilgili calistirma kimligi.</summary>
    public string? RunId { get; init; }

    /// <summary>Onay bekleyen tool'un adi. Tool onayinda dolu.</summary>
    public string? ToolName { get; init; }

    /// <summary>Ilgili workflow'un adi. Insan girdisi isteginde dolu.</summary>
    public string? WorkflowName { get; init; }

    /// <summary>Istegin kullaniciya gosterilecek metni.</summary>
    public string? Prompt { get; init; }
}

/// <summary>Asilan bir kota esiginin webhook ozeti.</summary>
public sealed record WebhookQuotaSummary
{
    /// <summary>Asilan olcut.</summary>
    public required QuotaMetric Metric { get; init; }

    /// <summary>Kuralin baglandigi agent. Kiraci genelinde <see langword="null"/>.</summary>
    public string? AgentName { get; init; }

    /// <summary>Sayacin araligi.</summary>
    public required QuotaPeriod Period { get; init; }

    /// <summary>Asilan esik yuzdesi: 80 veya 100.</summary>
    public required int ThresholdPercent { get; init; }

    /// <summary>Tanimli sinir.</summary>
    public required decimal Limit { get; init; }

    /// <summary>Guncel tuketim.</summary>
    public required decimal Used { get; init; }

    /// <summary>Sayacin sifirlanacagi zaman (UTC).</summary>
    public DateTimeOffset? ResetsAt { get; init; }
}

/// <summary>Bir puan penceresi esiginin webhook ozeti (Faz 49).</summary>
public sealed record WebhookScoreSummary
{
    /// <summary>Pencere icindeki ortalama puan (0-100).</summary>
    public required double AverageScore { get; init; }

    /// <summary>Pencere icindeki ornek (puanlanmis calistirma) sayisi.</summary>
    public required long SampleCount { get; init; }

    /// <summary>Asilan dusuk puan esigi.</summary>
    public required int Threshold { get; init; }

    /// <summary>Pencerenin baslangici (UTC).</summary>
    public DateTimeOffset? WindowStart { get; init; }

    /// <summary>Pencerenin bitisi (UTC).</summary>
    public DateTimeOffset? WindowEnd { get; init; }
}

/// <summary>
/// <see cref="WebhookEventPayload"/> ve alt tipleri icin kaynak uretilmis JSON
/// baglami.
/// </summary>
/// <remarks>
/// AOT uyumlulugu icin gereklidir: webhook govdesi yansimaya dayanan
/// serilestirme kullanmaz.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(WebhookEventPayload))]
public partial class WebhookEventPayloadJsonContext : JsonSerializerContext;
