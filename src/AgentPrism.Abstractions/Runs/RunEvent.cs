namespace AgentPrism;

/// <summary>
/// Bir calistirma sirasinda olusan tek bir olay. Olaylar <em>append-only</em>'dir:
/// hicbir zaman guncellenmez, yalnizca eklenir.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Sequence"/> tek bir yazicidan uretilir ve calistirma icinde 0'dan
/// baslayarak artar. Bu sayede canli akis (SSE) ve gecmise donuk yeniden oynatma
/// ayni kod yolundan gecer; istemci kopan baglantidan kaldigi sira numarasindan
/// devam edebilir.
/// </para>
/// <para>
/// Olay tipine gore hangi alanlarin dolduruldugu:
/// <list type="table">
///   <item><term><see cref="RunEventType.MessageDelta"/></term><description><see cref="Text"/></description></item>
///   <item><term><see cref="RunEventType.ToolInvoking"/></term><description><see cref="ToolName"/>, <see cref="ToolCallId"/>, <see cref="Payload"/> (argumanlar)</description></item>
///   <item><term><see cref="RunEventType.ToolInvoked"/></term><description><see cref="ToolName"/>, <see cref="ToolCallId"/>, <see cref="Payload"/> (sonuc)</description></item>
///   <item><term><see cref="RunEventType.ToolFailed"/></term><description><see cref="ToolName"/>, <see cref="ToolCallId"/>, <see cref="Text"/> (hata mesaji)</description></item>
///   <item><term><see cref="RunEventType.RunFailed"/></term><description><see cref="Text"/> (hata mesaji)</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed record RunEvent
{
    /// <summary>Olayin ait oldugu calistirma.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Calistirma icindeki sira numarasi. 0'dan baslar ve bosluksuz artar.</summary>
    public required long Sequence { get; init; }

    /// <summary>Olay tipi.</summary>
    public required RunEventType Type { get; init; }

    /// <summary>Olayin olustugu an (UTC).</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Metin icerik. Tipe gore anlami degisir.</summary>
    public string? Text { get; init; }

    /// <summary>Tool adi. Yalnizca tool olaylarinda dolu.</summary>
    public string? ToolName { get; init; }

    /// <summary>Tool cagri kimligi. Ayni tool'un birden cok cagrisini ayirt eder.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Serbest JSON yuku. Tool argumanlari ve sonuclari burada tasinir.</summary>
    public string? Payload { get; init; }
}
