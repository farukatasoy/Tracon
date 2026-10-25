namespace Tracon;

/// <summary>Settings of <see cref="GoogleSpeechBuilderExtensions.UseGoogleSpeech"/>.</summary>
/// <remarks>
/// <see cref="Model"/> is the only required setting. Tracon carries no Gemini model or voice list in
/// code: a new model or voice needs no Tracon release.
/// </remarks>
public sealed class GoogleSpeechOptions
{
    /// <summary>
    /// Gets or sets the Gemini text-to-speech model, for example <c>gemini-3.8-flash-tts</c>. Required.
    /// </summary>
    /// <remarks>
    /// A request that names its own model (<see cref="SpeechRequest.ModelId"/>) uses that model.
    /// The model decides the audio container: some answer a WAV file, others raw 16-bit PCM.
    /// Tracon reads the answer's MIME type and hands the caller the same <c>wav</c> or <c>pcm</c>
    /// whichever the model sent.
    /// </remarks>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the registration name of the synthesizer. A caller selects it with
    /// <c>"provider": "gemini"</c> on <c>POST /api/voice/speak</c>. Default <c>gemini</c>.
    /// </summary>
    /// <remarks>
    /// The name selects the registration. The tenant's egress policy and provider bindings use the
    /// provider name <c>google</c> (<see cref="GoogleProviderNames.Google"/>), the name the chat provider
    /// has, so one rule covers chat, images and speech.
    /// </remarks>
    public string Name { get; set; } = "gemini";

    /// <summary>
    /// Gets or sets the most characters one <see cref="ISpeechSynthesizer.SynthesizeAsync"/> call
    /// accepts. Default <c>5000</c>.
    /// </summary>
    /// <remarks>
    /// Tracon's own guard, the same default as the built-in ElevenLabs client. It is not a Google limit:
    /// Gemini bounds the request by tokens, not characters.
    /// </remarks>
    public int MaxCharactersPerRequest { get; set; } = 5000;

    /// <summary>
    /// Gets or sets the most characters one dialogue request carries, summed over its turns.
    /// Default <c>2000</c>.
    /// </summary>
    /// <remarks>Tracon's own guard, like <see cref="MaxCharactersPerRequest"/>.</remarks>
    public int MaxDialogueCharactersPerRequest { get; set; } = 2000;

    /// <summary>
    /// Gets or sets how a dialogue names its speakers in the request. Default
    /// <see cref="GoogleDialogueSpeakerMode.PartMetadata"/>.
    /// </summary>
    /// <remarks>
    /// Measured against the live API (2026-10-25): the speaker format depends on the model generation, and
    /// a model refuses the other one with <c>400</c>. Set the mode that matches <see cref="Model"/>.
    /// </remarks>
    public GoogleDialogueSpeakerMode DialogueSpeakerMode { get; set; } = GoogleDialogueSpeakerMode.PartMetadata;

    /// <summary>
    /// Gets the voices <c>GET /api/voice/voices</c> lists for the synthesizer. Empty by default.
    /// </summary>
    /// <remarks>
    /// A static declaration: the Gemini API has no voice list. The name of a prebuilt voice, for example
    /// <c>Kore</c>, is the <see cref="VoiceDescriptor.VoiceId"/> a request names.
    /// </remarks>
    public IList<VoiceDescriptor> Voices { get; } = [];

    /// <summary>
    /// Gets or sets whether the registered synthesizer is also the unnamed default one. Default
    /// <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unnamed default serves the <c>speak</c> tool and the live conversation. It is registered with
    /// <c>TryAdd</c>: a default that is already there stays, so call <c>UseGoogleSpeech</c> before
    /// <c>UseVoice</c> to make Gemini the default.
    /// </para>
    /// <para>
    /// The <c>speak</c> tool sends <c>Tracon:Voice:SynthesisModelId</c> and <c>Tracon:Voice:OutputFormat</c> to
    /// the default engine. Leave the model empty, or set it to a Gemini text-to-speech model, or Google refuses
    /// it. The format defaults to <c>mp3_44100_128</c>, which Gemini cannot produce: set it to <c>wav</c>, or the
    /// tool fails before any call.
    /// </para>
    /// <para>
    /// Keep Gemini a named provider for the live conversation socket. A stream is raw PCM, and the shipped
    /// conversation panel decodes whole audio files.
    /// </para>
    /// </remarks>
    public bool UseAsDefault { get; set; }
}

/// <summary>How a Gemini dialogue request names its speakers.</summary>
/// <remarks>
/// The two formats exist because the model generations disagree (measured 2026-10-25): the
/// <c>gemini-3.8</c> models want the speaker on every text part and refuse a speaker label in the text;
/// <c>gemini-2.5-flash-preview-tts</c> and <c>gemini-3.1-flash-tts-preview</c> want the label in the text
/// and refuse the part metadata.
/// </remarks>
public enum GoogleDialogueSpeakerMode
{
    /// <summary>
    /// Each turn is a text part with its speaker in the part's speech metadata. The format of the
    /// <c>gemini-3.8</c> models.
    /// </summary>
    PartMetadata = 0,

    /// <summary>
    /// The turns are lines of one text, each starting with its speaker label (<c>Speaker1: …</c>). The
    /// format of the <c>gemini-2.5</c> and <c>gemini-3.1</c> preview models.
    /// </summary>
    /// <remarks>
    /// A turn whose text has a line that starts with a speaker label would change the voice, so Tracon
    /// refuses it before the request is sent.
    /// </remarks>
    TextLabels = 1,
}
