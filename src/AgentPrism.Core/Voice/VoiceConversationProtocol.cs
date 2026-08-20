using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>
/// The frame names and the audio formats of the conversation WebSocket.
/// </summary>
/// <remarks>
/// <para>
/// The names stay <strong>aligned</strong> with the existing SSE contract; the user
/// interface does not carry two different mental models. These are a
/// <strong>stable</strong> contract: to change them breaks clients.
/// </para>
/// <para>
/// Text frames are JSON and binary frames are raw audio. The direction follows from
/// the <em>name</em> of the frame, not from its type.
/// </para>
/// </remarks>
public static class VoiceConversationProtocol
{
    /// <summary>The name of the WebSocket sub-protocol. The handshake echoes it back.</summary>
    public const string SubProtocol = "agentprism.voice.v1";

    /// <summary>
    /// The sub-protocol prefix that carries the bearer token.
    /// </summary>
    /// <remarks>
    /// The token <strong>is not put in the query string</strong>: the address is
    /// written to server logs, to reverse proxy logs and to the browser history. A
    /// browser cannot add a custom header to a WebSocket handshake; the standard way
    /// out is the <c>Sec-WebSocket-Protocol</c> header.
    /// </remarks>
    public const string TokenSubProtocolPrefix = "agentprism.token.";

    // --- client -> server ---

    /// <summary>Starts the conversation.</summary>
    public const string ClientStart = "start";

    /// <summary>Speech ended; answer now.</summary>
    public const string ClientCommit = "commit";

    /// <summary>Interruption (barge-in): stop the generation.</summary>
    public const string ClientCancel = "cancel";

    /// <summary>Closes the connection.</summary>
    public const string ClientStop = "stop";

    // --- server -> client ---

    /// <summary>The server is ready for the conversation.</summary>
    public const string ServerReady = "ready";

    /// <summary>The transcribed speech text.</summary>
    public const string ServerTranscript = "transcript";

    /// <summary>The run started; the frame carries its identifier.</summary>
    public const string ServerRunStarted = "runStarted";

    /// <summary>A text response (caption).</summary>
    public const string ServerText = "text";

    /// <summary>The binary frames that follow are audio chunks.</summary>
    public const string ServerAudioStart = "audioStart";

    /// <summary>The audio chunks ended.</summary>
    public const string ServerAudioEnd = "audioEnd";

    /// <summary>An error.</summary>
    public const string ServerError = "error";

    /// <summary>The turn ended.</summary>
    public const string ServerDone = "done";
}

/// <summary>The audio formats that the client can send.</summary>
/// <remarks>
/// Both are supported. The default is <see cref="WebmOpus"/>: every browser can
/// produce it with <c>MediaRecorder</c>, its bandwidth is low, and the concatenated
/// chunks form a valid file.
/// </remarks>
public static class VoiceAudioFormats
{
    /// <summary>The <c>MediaRecorder</c> output: Opus in a WebM container.</summary>
    public const string WebmOpus = "webm-opus";

    /// <summary>
    /// The <c>AudioWorklet</c> output: raw 16-bit little-endian PCM, mono.
    /// </summary>
    /// <remarks>
    /// Raw PCM <strong>has no header</strong> and is not a valid file on its own.
    /// Before the audio goes to the transcription provider the server writes a WAV
    /// header.
    /// </remarks>
    public const string Pcm16 = "pcm16";

    /// <summary>Determines whether the format is known.</summary>
    /// <param name="format">The format name; an empty value means the default.</param>
    /// <returns><see langword="true"/> when the format is known.</returns>
    public static bool IsKnown(string? format)
        => string.IsNullOrWhiteSpace(format)
           || string.Equals(format, WebmOpus, StringComparison.OrdinalIgnoreCase)
           || string.Equals(format, Pcm16, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A control message that comes from the client.</summary>
/// <remarks>
/// One type covers every message: most of the fields are optional and only the
/// <c>start</c> message fills them. A separate type per message would require the
/// type to be chosen before the message is parsed.
/// </remarks>
internal sealed record VoiceClientMessage
{
    /// <summary>Gets the message name.</summary>
    public string? Type { get; init; }

    /// <summary>Gets the name of the agent to talk to (<c>start</c>).</summary>
    public string? Agent { get; init; }

    /// <summary>Gets the identifier of the voice to use (<c>start</c>).</summary>
    public string? VoiceId { get; init; }

    /// <summary>Gets the format of the audio that the client sends (<c>start</c>).</summary>
    public string? InputFormat { get; init; }
}

/// <summary>An event frame that goes from the server to the client.</summary>
/// <remarks>
/// The rationale is the same as for <see cref="VoiceClientMessage"/>: one type with
/// optional fields. <see langword="null"/> fields are not written to the JSON.
/// </remarks>
internal sealed record VoiceServerMessage
{
    /// <summary>Gets the event name.</summary>
    public required string Type { get; init; }

    /// <summary>Gets the agent that is spoken to (<c>ready</c>).</summary>
    public string? Agent { get; init; }

    /// <summary>Gets the identifier of the agent session (<c>ready</c>).</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets a value that indicates whether the audio is stored (<c>ready</c>). The
    /// user interface <strong>shows</strong> this to the user; no recording happens
    /// silently.
    /// </summary>
    public bool? PersistAudio { get; init; }

    /// <summary>Gets the transcribed text (<c>transcript</c>).</summary>
    public string? Text { get; init; }

    /// <summary>Gets a value that indicates whether the transcript is final (<c>transcript</c>).</summary>
    public bool? Final { get; init; }

    /// <summary>Gets the run identifier (<c>runStarted</c>).</summary>
    public string? RunId { get; init; }

    /// <summary>Gets the text chunk (<c>text</c>).</summary>
    public string? Delta { get; init; }

    /// <summary>Gets the MIME type of the audio chunks (<c>audioStart</c>).</summary>
    public string? MediaType { get; init; }

    /// <summary>Gets the identifier of the stored audio attachment (<c>audioEnd</c>); absent when nothing was stored.</summary>
    public string? AttachmentId { get; init; }

    /// <summary>Gets a value that indicates whether the turn was interrupted (<c>done</c>).</summary>
    public bool? Cancelled { get; init; }

    /// <summary>Gets the number of completed turns (<c>done</c>).</summary>
    public int? Turn { get; init; }

    /// <summary>Gets the error description (<c>error</c>).</summary>
    public string? Message { get; init; }
}

/// <summary>The source generator context of the conversation protocol.</summary>
/// <remarks>
/// <c>AgentPrism.Core</c> is marked AOT compatible; the reflection-based
/// <c>JsonSerializer</c> overloads produce <c>IL2026</c>/<c>IL3050</c> and break the
/// build.
/// </remarks>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VoiceClientMessage))]
[JsonSerializable(typeof(VoiceServerMessage))]
internal sealed partial class VoiceConversationJsonContext : JsonSerializerContext;
