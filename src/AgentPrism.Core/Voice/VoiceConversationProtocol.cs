using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// Konusma WebSocket'inin cerceve adlari ve ses bicimleri.
/// </summary>
/// <remarks>
/// <para>
/// Adlar mevcut SSE sozlesmesiyle <strong>uyumlu</strong> tutulur; arayuz iki
/// farkli zihinsel model tasimaz. Bunlar <strong>kararli</strong> sozlesmedir:
/// degistirmek istemcileri kirar.
/// </para>
/// <para>
/// Metin cerceveleri JSON'dur, ikili cerceveler ham sestir. Yon, cercevenin
/// tipinden degil <em>adindan</em> anlasilir.
/// </para>
/// </remarks>
public static class VoiceConversationProtocol
{
    /// <summary>WebSocket alt protokolunun adi. El sikismada bu ad geri yankilanir.</summary>
    public const string SubProtocol = "agentprism.voice.v1";

    /// <summary>
    /// Bearer token'i tasiyan alt protokol oneki.
    /// </summary>
    /// <remarks>
    /// 🚨 Token <strong>sorgu dizesine konmaz</strong>: adres sunucu gunluklerine,
    /// ters vekil gunluklerine ve tarayici gecmisine yazilir. Tarayici bir
    /// WebSocket el sikismasina ozel baslik ekleyemez; standart kacis yolu
    /// <c>Sec-WebSocket-Protocol</c> basligidir.
    /// </remarks>
    public const string TokenSubProtocolPrefix = "agentprism.token.";

    // --- istemci -> sunucu ---

    /// <summary>Konusmayi baslatir.</summary>
    public const string ClientStart = "start";

    /// <summary>Konusma bitti; simdi cevapla.</summary>
    public const string ClientCommit = "commit";

    /// <summary>Kesinti (barge-in): uretimi kes.</summary>
    public const string ClientCancel = "cancel";

    /// <summary>Baglantiyi kapat.</summary>
    public const string ClientStop = "stop";

    // --- sunucu -> istemci ---

    /// <summary>Sunucu konusmaya hazir.</summary>
    public const string ServerReady = "ready";

    /// <summary>Cozulen konusma metni.</summary>
    public const string ServerTranscript = "transcript";

    /// <summary>Calistirma basladi; kimligi tasir.</summary>
    public const string ServerRunStarted = "runStarted";

    /// <summary>Metin yaniti (altyazi).</summary>
    public const string ServerText = "text";

    /// <summary>Bundan sonraki ikili cerceveler ses parcasidir.</summary>
    public const string ServerAudioStart = "audioStart";

    /// <summary>Ses parcasi bitti.</summary>
    public const string ServerAudioEnd = "audioEnd";

    /// <summary>Hata.</summary>
    public const string ServerError = "error";

    /// <summary>Tur bitti.</summary>
    public const string ServerDone = "done";
}

/// <summary>Istemcinin gonderebilecegi ses bicimleri.</summary>
/// <remarks>
/// Ikisi de desteklenir. Varsayilan <see cref="WebmOpus"/>'tur: her tarayicida
/// <c>MediaRecorder</c> ile uretilebilir, bant genisligi dusuktur ve parcalar
/// birlestirildiginde gecerli bir dosya olusur.
/// </remarks>
public static class VoiceAudioFormats
{
    /// <summary><c>MediaRecorder</c> ciktisi: WebM kabinde Opus.</summary>
    public const string WebmOpus = "webm-opus";

    /// <summary>
    /// <c>AudioWorklet</c> ciktisi: ham 16-bit little-endian PCM, mono.
    /// </summary>
    /// <remarks>
    /// 🚨 Ham PCM <strong>basliksizdir</strong> ve tek basina gecerli bir dosya
    /// degildir. Cozum saglayicisina gonderilmeden once sunucu bir WAV basligi
    /// yazar.
    /// </remarks>
    public const string Pcm16 = "pcm16";

    /// <summary>Bicimin taninip taninmadigini bildirir.</summary>
    /// <param name="format">Bicim adi; bos ise varsayilan kabul edilir.</param>
    /// <returns>Biçim taniniyorsa <see langword="true"/>.</returns>
    public static bool IsKnown(string? format)
        => string.IsNullOrWhiteSpace(format)
           || string.Equals(format, WebmOpus, StringComparison.OrdinalIgnoreCase)
           || string.Equals(format, Pcm16, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Istemciden gelen bir denetim mesaji.</summary>
/// <remarks>
/// Tek bir tip tum mesajlari karsilar: alanlarin cogu istege baglidir ve yalniz
/// <c>start</c> mesajinda doludur. Mesaj basina ayri tipler, ayristirma oncesi
/// tip secimi gerektirirdi.
/// </remarks>
internal sealed record VoiceClientMessage
{
    /// <summary>Mesaj adi.</summary>
    public string? Type { get; init; }

    /// <summary>Konusulacak agent'in adi (<c>start</c>).</summary>
    public string? Agent { get; init; }

    /// <summary>Kullanilacak sesin kimligi (<c>start</c>).</summary>
    public string? VoiceId { get; init; }

    /// <summary>Gonderilecek sesin bicimi (<c>start</c>).</summary>
    public string? InputFormat { get; init; }
}

/// <summary>Sunucudan istemciye giden bir olay cercevesi.</summary>
/// <remarks>
/// <see cref="VoiceClientMessage"/> ile ayni gerekce: tek tip, istege bagli
/// alanlar. <see langword="null"/> alanlar JSON'a yazilmaz.
/// </remarks>
internal sealed record VoiceServerMessage
{
    /// <summary>Olay adi.</summary>
    public required string Type { get; init; }

    /// <summary>Konusulan agent (<c>ready</c>).</summary>
    public string? Agent { get; init; }

    /// <summary>Agent oturumunun kimligi (<c>ready</c>).</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Sesin saklanip saklanmadigi (<c>ready</c>). 🚨 Arayuz bunu kullaniciya
    /// <strong>gosterir</strong>; kayit sessizce yapilmaz.
    /// </summary>
    public bool? PersistAudio { get; init; }

    /// <summary>Cozulen metin (<c>transcript</c>).</summary>
    public string? Text { get; init; }

    /// <summary>Cozumun kesin olup olmadigi (<c>transcript</c>).</summary>
    public bool? Final { get; init; }

    /// <summary>Calistirma kimligi (<c>runStarted</c>).</summary>
    public string? RunId { get; init; }

    /// <summary>Metin parcasi (<c>text</c>).</summary>
    public string? Delta { get; init; }

    /// <summary>Ses parcasinin MIME turu (<c>audioStart</c>).</summary>
    public string? MediaType { get; init; }

    /// <summary>Saklanan ses ekinin kimligi (<c>audioEnd</c>); saklanmadiysa yok.</summary>
    public string? AttachmentId { get; init; }

    /// <summary>Turun kesilip kesilmedigi (<c>done</c>).</summary>
    public bool? Cancelled { get; init; }

    /// <summary>Tamamlanan tur sayisi (<c>done</c>).</summary>
    public int? Turn { get; init; }

    /// <summary>Hata aciklamasi (<c>error</c>).</summary>
    public string? Message { get; init; }
}

/// <summary>Konusma protokolunun kaynak ureteci baglami.</summary>
/// <remarks>
/// <c>AgentPrism.Core</c> AOT uyumlu isaretlidir; yansimaya dayanan
/// <c>JsonSerializer</c> asiri yuklemeleri <c>IL2026</c>/<c>IL3050</c> uretir ve
/// build'i kirar. Gerekce: <c>docs/KARARLAR.md</c>, karar K-006.
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VoiceClientMessage))]
[JsonSerializable(typeof(VoiceServerMessage))]
internal sealed partial class VoiceConversationJsonContext : JsonSerializerContext;
