using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Bir span'in OpenTelemetry tipi. Degerler veritabaninda <c>smallint</c>
/// olarak saklanir; sayilar kararlidir.
/// </summary>
/// <remarks>
/// <see cref="System.Diagnostics.ActivityKind"/> ile birebir ayni sirayi izler.
/// Kendi enum'umuzu tasimamizin sebebi <c>AgentPrism.Abstractions</c>'in
/// <c>System.Diagnostics.DiagnosticSource</c> tipini public yuzeyine
/// sizdirmamasidir.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<TraceSpanKind>))]
public enum TraceSpanKind
{
    /// <summary>Ic islem. Varsayilan.</summary>
    Internal = 0,

    /// <summary>Gelen istegi karsilayan span.</summary>
    Server = 1,

    /// <summary>Disa giden cagriyi yapan span.</summary>
    Client = 2,

    /// <summary>Mesaj ureten span.</summary>
    Producer = 3,

    /// <summary>Mesaj tuketen span.</summary>
    Consumer = 4,
}

/// <summary>Bir span'in sonuc durumu. Veritabaninda <c>smallint</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TraceSpanStatus>))]
public enum TraceSpanStatus
{
    /// <summary>Durum bildirilmedi.</summary>
    Unset = 0,

    /// <summary>Islem basarili bitti.</summary>
    Ok = 1,

    /// <summary>Islem hata verdi.</summary>
    Error = 2,
}

/// <summary>
/// Kalicilastirilmis tek bir OpenTelemetry span'i.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Id"/> ve <see cref="ParentId"/> degerleri W3C kimliklerinden
/// <strong>turetilir</strong>, rastgele uretilmez. Sebep: bir span tamamlandiginda
/// ebeveyni henuz tamamlanmamis olabilir ve ebeveynin veritabani kimligi
/// bilinmez. Turetme (<c>trace_id</c> + <c>span_id</c> → SHA-256 → ilk 16 bayt)
/// eslestirmeyi haritasiz ve sirasiz calisir hale getirir; ayni span iki kez
/// yazilirsa da ayni kimlik uretilir.
/// </para>
/// <para>
/// W3C kimlikleri <see cref="SpanId"/> alaninda ayrica saklanir: kullanicinin
/// kendi APM sisteminde (Jaeger, Application Insights) ayni span'i bulabilmesi
/// icin gereklidir.
/// </para>
/// </remarks>
public sealed record TraceSpan
{
    /// <summary>Veritabani kimligi. W3C kimliklerinden turetilir.</summary>
    public required Guid Id { get; init; }

    /// <summary>Ust span'in veritabani kimligi. Kok span'de <see langword="null"/>.</summary>
    public Guid? ParentId { get; init; }

    /// <summary>W3C span kimligi (16 karakterlik onaltilik).</summary>
    public required string SpanId { get; init; }

    /// <summary>Span adi. Ornek: <c>chat gpt-5.4-mini</c>, <c>invoke_agent destek</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Span tipi.</summary>
    public TraceSpanKind Kind { get; init; }

    /// <summary>Baslangic zamani (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Bitis zamani (UTC).</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Sonuc durumu.</summary>
    public TraceSpanStatus Status { get; init; }

    /// <summary>
    /// Span oznitelikleri. GenAI semantic convention anahtarlari
    /// (<c>gen_ai.request.model</c>, <c>gen_ai.usage.input_tokens</c>) burada gelir.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Span suresi. Span bitmemisse <see langword="null"/>.</summary>
    [JsonIgnore]
    public TimeSpan? Duration => EndedAt is { } ended ? ended - StartedAt : null;
}

/// <summary>
/// Bir calistirmanin span agaci. Waterfall gorunumu bunun uzerine kurulur.
/// </summary>
public sealed record RunTrace
{
    /// <summary>Veritabani kimligi.</summary>
    public required Guid Id { get; init; }

    /// <summary>W3C trace kimligi (32 karakterlik onaltilik).</summary>
    public required string TraceId { get; init; }

    /// <summary>Iliskili calistirma. Calistirmasiz span'lerde <see langword="null"/>.</summary>
    public Guid? RunId { get; init; }

    /// <summary>Kiraci kimligi.</summary>
    public required string TenantId { get; init; }

    /// <summary>Ilk span'in baslangici (UTC).</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Son span'in bitisi (UTC).</summary>
    public DateTimeOffset? EndedAt { get; init; }

    /// <summary>Span'ler; baslangic zamanina gore sirali.</summary>
    public IReadOnlyList<TraceSpan> Spans { get; init; } = [];
}
