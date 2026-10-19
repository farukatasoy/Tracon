namespace Tracon;

/// <summary>Settings of <see cref="OpenAISpeechBuilderExtensions.UseOpenAISpeech"/>.</summary>
/// <remarks>
/// At least one of <see cref="SynthesisModel"/> and <see cref="TranscriptionModel"/> must be set.
/// </remarks>
public sealed class OpenAISpeechOptions
{
    /// <summary>
    /// Gets or sets the text-to-speech model, for example <c>gpt-4o-mini-tts</c>. A synthesizer is
    /// registered when this is set.
    /// </summary>
    /// <remarks>
    /// The registration is bound to this model. A request that names another model is refused:
    /// the OpenAI audio client ignores a per-request model. To use a second model, register a
    /// second name with another call.
    /// </remarks>
    public string? SynthesisModel { get; set; }

    /// <summary>
    /// Gets or sets the speech-to-text model, for example <c>gpt-4o-mini-transcribe</c>. A transcriber is
    /// registered when this is set.
    /// </summary>
    /// <remarks>The same rule as <see cref="SynthesisModel"/>: the registration is bound to this model.</remarks>
    public string? TranscriptionModel { get; set; }

    /// <summary>
    /// Gets or sets the registration name of the synthesizer. A caller selects it with
    /// <c>"provider": "openai"</c> on <c>POST /api/voice/speak</c>. Default <c>openai</c>.
    /// </summary>
    public string SynthesizerName { get; set; } = "openai";

    /// <summary>
    /// Gets or sets the registration name of the transcriber. Transcriber names are separate from
    /// synthesizer names. Default <c>openai</c>.
    /// </summary>
    public string TranscriberName { get; set; } = "openai";

    /// <summary>
    /// Gets or sets the most characters one synthesis call accepts. Default <c>4096</c>, the limit
    /// the OpenAI documentation states for the speech endpoint.
    /// </summary>
    public int MaxCharactersPerRequest { get; set; } = 4096;

    /// <summary>
    /// Gets the voices <c>GET /api/voice/voices</c> lists for the synthesizer. Empty by default.
    /// </summary>
    /// <remarks>
    /// A static declaration: the speech endpoint has no voice list. The service refuses a request
    /// without a voice, so a call that names none is spoken in <c>alloy</c>.
    /// </remarks>
    public IList<VoiceDescriptor> Voices { get; } = [];

    /// <summary>
    /// Gets or sets whether the registered synthesizer and transcriber are also the unnamed default
    /// ones. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The unnamed default serves the <c>speak</c> and <c>transcribe</c> tools and the live
    /// conversation. It is registered with <c>TryAdd</c>: a default that is already there stays,
    /// so call <c>UseOpenAISpeech</c> before <c>UseVoice</c> to make OpenAI the default.
    /// </para>
    /// <para>
    /// The <c>speak</c> and <c>transcribe</c> tools send <c>Tracon:Voice:SynthesisModelId</c> and
    /// <c>TranscriptionModelId</c> to the default engine, and a registration is bound to one model.
    /// Leave both empty, or set them to the OpenAI models named here, or the tool call fails with a
    /// message that names both models.
    /// </para>
    /// </remarks>
    public bool UseAsDefault { get; set; }
}
