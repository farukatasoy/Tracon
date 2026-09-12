namespace Tracon;

/// <summary>Options of the real-time voice conversation layer.</summary>
/// <remarks>
/// <para>
/// This capability <strong>changes the hosting model</strong>. A conversation
/// connection stays open for minutes and binds to one server instance (a sticky
/// session). The capability is therefore optional: no WebSocket endpoint opens and
/// no behaviour changes until <c>UseVoiceConversation()</c> is called.
/// </para>
/// <para>
/// The type is <strong>not</strong> a <c>record</c>: option classes must
/// not produce a <c>ToString</c> that can be written to a log.
/// </para>
/// </remarks>
public sealed class VoiceConversationOptions
{
    /// <summary>The default name of the configuration section.</summary>
    public const string SectionName = "Tracon:Voice:Conversation";

    /// <summary>
    /// Gets or sets the maximum number of conversation connections that one tenant
    /// can hold open at the same time. The default is 5.
    /// </summary>
    /// <remarks>
    /// An open connection is not a server thread, but it holds a socket, a buffer and
    /// an agent session. An unlimited number of connections would let a single tenant
    /// fill the server.
    /// </remarks>
    public int MaxConcurrentConnectionsPerTenant { get; set; } = 5;

    /// <summary>
    /// Gets or sets the longest time that a connection can stay open. The default is
    /// 30 minutes.
    /// </summary>
    /// <remarks>
    /// When the time expires the connection closes cleanly and the client reconnects.
    /// An endless connection means that a leaking resource is never noticed.
    /// </remarks>
    public TimeSpan MaxConnectionDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Gets or sets the longest time that can pass without a frame. The default is
    /// 2 minutes.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Gets or sets the longest duration of a single utterance. The default is
    /// 60 seconds.
    /// </summary>
    /// <remarks>
    /// End-of-speech detection (VAD) is <strong>on the client</strong>; the server
    /// does no signal processing. This limit is a <em>safety net</em>: when the VAD of
    /// the client never fires the utterance closes on its own and goes to
    /// transcription.
    /// </remarks>
    public TimeSpan MaxUtteranceDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets how many bytes a single utterance can hold. The default is 8 MB.
    /// </summary>
    /// <remarks>
    /// Next to the duration limit there is also a byte limit: when the client sends
    /// arbitrary data instead of audio the duration may never expire.
    /// </remarks>
    public int MaxUtteranceBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// Gets or sets a value that indicates whether the conversation audio is written
    /// to the <c>attachments</c> table. The default is <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <strong>Voice is personal data.</strong> That it is not stored by default is
    /// deliberate. When it is enabled the retention policy applies and the
    /// user interface <strong>shows</strong> the user that the audio is recorded — no
    /// recording happens silently.
    /// </remarks>
    public bool PersistAudio { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the voice that speaks the responses. When it is
    /// empty the default voice of the provider is used
    /// (<c>Tracon:Voice:DefaultVoiceId</c>).
    /// </summary>
    public string? VoiceId { get; set; }

    /// <summary>
    /// Gets or sets the MIME type of the synthesized audio. The default is
    /// <c>audio/mpeg</c>.
    /// </summary>
    /// <remarks>
    /// The value must be the same as the <em>configured output format</em> of the
    /// speech provider (<c>Tracon:Voice:OutputFormat</c>). Streaming synthesis
    /// returns raw bytes only and does not report the type; the client has to know the
    /// type to decode the audio. A wrong value produces a silent decode failure in the
    /// browser.
    /// </remarks>
    public string OutputMediaType { get; set; } = "audio/mpeg";

    /// <summary>
    /// Gets or sets the sample rate (Hz) of clients that send raw PCM. The default is
    /// 16,000.
    /// </summary>
    /// <remarks>
    /// Raw PCM has no header; before the audio goes to the transcription provider the
    /// server writes a WAV header, and that header carries this value. A wrong value
    /// makes the audio decode at the wrong rate.
    /// </remarks>
    public int InputSampleRate { get; set; } = 16_000;

    /// <summary>
    /// Gets or sets how many characters of one response are spoken. The default is
    /// 5,000.
    /// </summary>
    /// <remarks>
    /// When the limit is exceeded the remaining text is <strong>not spoken</strong>,
    /// but it still streams as captions: the user sees the whole answer and does not
    /// get an answer that was silently truncated.
    /// </remarks>
    public int MaxSpokenCharactersPerTurn { get; set; } = 5_000;
}
