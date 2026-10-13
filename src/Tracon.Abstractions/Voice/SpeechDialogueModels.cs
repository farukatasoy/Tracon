using System.Collections.ObjectModel;
using System.Text.Json;

namespace Tracon;

/// <summary>The limits of one dialogue request, as the provider states them.</summary>
/// <remarks>
/// The caller plans its requests from these values: a request over a limit is refused,
/// never split or truncated.
/// </remarks>
public sealed record SpeechDialogueCapabilities
{
    /// <summary>Gets the most distinct voices one request can use.</summary>
    public required int MaxVoices { get; init; }

    /// <summary>Gets the most characters one request can carry, summed over its turns.</summary>
    public required int MaxCharactersPerRequest { get; init; }

    /// <summary>
    /// Gets whether the provider reports each turn's time range when
    /// <see cref="SpeechDialogueRequest.IncludeTimestamps"/> is set.
    /// </summary>
    public bool SupportsSegmentTimestamps { get; init; }
}

/// <summary>One turn of a dialogue: a text and the voice that speaks it.</summary>
public sealed record SpeechDialogueTurn
{
    /// <summary>Gets the text to speak.</summary>
    /// <remarks>
    /// Sent as written. A provider's inline tags (for example ElevenLabs'
    /// <c>[laughs]</c>) pass through unchanged and count toward the character limit.
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>Gets the identifier of the voice that speaks the text.</summary>
    /// <remarks>Required: a dialogue turn has no default voice.</remarks>
    public required string VoiceId { get; init; }
}

/// <summary>A request to voice a dialogue.</summary>
public sealed record SpeechDialogueRequest
{
    /// <summary>Gets the turns, in the order they are spoken.</summary>
    public required IReadOnlyList<SpeechDialogueTurn> Turns { get; init; }

    /// <summary>
    /// Gets the dialogue model to use. When empty, the synthesizer uses its own default
    /// (the built-in client: <c>VoiceOptions.DialogueModelId</c>, then the provider's
    /// default).
    /// </summary>
    /// <remarks>
    /// The single-voice model (<c>VoiceOptions.SynthesisModelId</c>) is not used here: a
    /// model that speaks one voice need not voice a dialogue.
    /// </remarks>
    public string? ModelId { get; init; }

    /// <summary>
    /// Gets the provider's output format name (for example, <c>pcm_24000</c>); when empty,
    /// the synthesizer's default.
    /// </summary>
    /// <remarks>
    /// Not a MIME type. Raw <c>pcm_*</c> output carries no file header: the caller that
    /// joins several results adds the header itself.
    /// </remarks>
    public string? OutputFormat { get; init; }

    /// <summary>Gets the language's ISO 639-1 code. The provider detects it itself if empty.</summary>
    public string? LanguageCode { get; init; }

    /// <summary>
    /// Gets whether each turn's time range and the character timing are returned with the
    /// audio. Default <see langword="false"/>.
    /// </summary>
    public bool IncludeTimestamps { get; init; }

    /// <summary>
    /// Gets the provider-specific settings, keyed under the provider's prefix (for example
    /// <c>elevenlabs.stability</c>). Empty by default.
    /// </summary>
    /// <remarks>
    /// A synthesizer refuses an unknown key, another provider's key, a key that has no
    /// effect on a dialogue, and a value of the wrong type, before it calls the service. It
    /// never ignores one. The message names the key, never the value.
    /// </remarks>
    public IReadOnlyDictionary<string, JsonElement> ProviderSettings { get; init; }
        = ReadOnlyDictionary<string, JsonElement>.Empty;

    /// <summary>
    /// Gets the tenant's own credential for the provider, or <see langword="null"/> to use
    /// the credential the synthesizer was configured with.
    /// </summary>
    /// <remarks>
    /// Tracon sets it from the tenant's provider binding, and only for a synthesizer whose
    /// <see cref="ISpeechDialogueSynthesizer.SupportsTenantCredentials"/> is
    /// <see langword="true"/>; a value the caller sets is replaced. It lives in memory for
    /// the call and is never persisted; its text form never shows the key.
    /// </remarks>
    public ModelProviderCredential? Credential { get; init; }
}

/// <summary>The audio of a dialogue and the time range of each turn.</summary>
/// <remarks>
/// A class, not a <c>record</c>, for the reason <see cref="SpeechAudio"/> gives: a
/// generated <c>ToString</c> or equality must not walk the audio.
/// </remarks>
public sealed class SpeechDialogueAudio
{
    /// <summary>Gets the audio of the whole dialogue.</summary>
    /// <remarks>
    /// <see cref="SpeechAudio.CharactersBilled"/>, <see cref="SpeechAudio.UsageSource"/>
    /// and <see cref="SpeechAudio.ModelId"/> describe the whole request.
    /// <see cref="SpeechAudio.Alignment"/> times the turns' texts joined in order, with no
    /// separator.
    /// </remarks>
    public required SpeechAudio Audio { get; init; }

    /// <summary>
    /// Gets the time range of each spoken part, in order; <see langword="null"/> when
    /// <see cref="SpeechDialogueRequest.IncludeTimestamps"/> was not set, when the provider
    /// does not report them, or when the provider's report does not match the turns.
    /// </summary>
    /// <remarks>
    /// A report that names a turn the request did not have, or a voice the turn did not
    /// use, is dropped whole rather than mapped to the wrong turn.
    /// </remarks>
    public IReadOnlyList<SpeechDialogueSegment>? Segments { get; init; }
}

/// <summary>The time range one turn takes in a dialogue's audio.</summary>
public sealed record SpeechDialogueSegment
{
    /// <summary>Gets the zero-based index of the turn in <see cref="SpeechDialogueRequest.Turns"/>.</summary>
    public required int TurnIndex { get; init; }

    /// <summary>Gets the voice that spoke the turn.</summary>
    public required string VoiceId { get; init; }

    /// <summary>Gets when the turn starts, relative to the start of the audio.</summary>
    public required TimeSpan Start { get; init; }

    /// <summary>Gets when the turn ends, relative to the start of the audio.</summary>
    public required TimeSpan End { get; init; }
}
