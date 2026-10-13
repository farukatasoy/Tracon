namespace Tracon;

/// <summary>
/// The provider name the built-in ElevenLabs client reports, and the
/// <c>ProviderSettings</c> keys it accepts on <see cref="SpeechRequest"/> and
/// <see cref="SpeechDialogueRequest"/>.
/// </summary>
/// <remarks>
/// <para>
/// These names are <strong>stable</strong>: a workflow node's stored settings and a
/// tenant's egress policy name them.
/// </para>
/// <para>
/// A key outside <see cref="SupportedSettings"/> is refused before the service is called,
/// and so is a single-voice key on a dialogue (<see cref="DialogueSupportedSettings"/>).
/// The refusal names the key, never the value.
/// </para>
/// <example>
/// A function node that voices two chunks of a long dialogue, the second conditioned
/// on the first:
/// <code>
/// tracon.AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, List&lt;ChatMessage&gt;&gt;(
///     "two-chunks",
///     provider =>
///     {
///         var speech = provider.GetRequiredService&lt;ISpeechProviderResolver&gt;();
///
///         return async (messages, context, cancellationToken) =>
///         {
///             var dialogue = await speech.GetDialogueSynthesizerAsync(cancellationToken: cancellationToken);
///             var chunk = new SpeechDialogueRequest
///             {
///                 Turns = [new SpeechDialogueTurn { Text = messages[^1].Text, VoiceId = "host-voice" }],
///             };
///
///             var first = await dialogue.SynthesizeDialogueAsync(chunk, cancellationToken);
///
///             var settings = new Dictionary&lt;string, JsonElement&gt;
///             {
///                 [ElevenLabsProviderNames.StabilitySetting] = JsonSerializer.SerializeToElement(0.5),
///             };
///
///             // The id is null when the provider reports none; a list with a null id is refused.
///             if (first.Audio.ProviderRequestId is { } id)
///             {
///                 settings[ElevenLabsProviderNames.PreviousRequestIdsSetting] = JsonSerializer.SerializeToElement(new[] { id });
///             }
///
///             var second = await dialogue.SynthesizeDialogueAsync(chunk with { ProviderSettings = settings }, cancellationToken);
///
///             return [new ChatMessage(ChatRole.Assistant, $"{first.Audio.Data.Length + second.Audio.Data.Length} bytes")];
///         };
///     });
/// </code>
/// </example>
/// </remarks>
public static class ElevenLabsProviderNames
{
    /// <summary>The provider name the built-in client reports: <c>elevenlabs</c>.</summary>
    public const string ElevenLabs = "elevenlabs";

    /// <summary>Prefix for provider-specific settings: <c>elevenlabs</c>.</summary>
    public const string SettingsPrefix = "elevenlabs";

    /// <summary>
    /// Voice stability: <c>elevenlabs.stability</c> (number from 0 to 1). Single voice and
    /// dialogue.
    /// </summary>
    /// <remarks>Lower values give a broader emotional range; higher values a steadier voice.</remarks>
    public const string StabilitySetting = "elevenlabs.stability";

    /// <summary>
    /// How closely the speech follows the voice: <c>elevenlabs.similarityBoost</c> (number
    /// from 0 to 1). Single voice (<c>similarity_boost</c>) and dialogue
    /// (<c>similarity</c>).
    /// </summary>
    /// <remarks>The provider documents that not every dialogue model uses it.</remarks>
    public const string SimilarityBoostSetting = "elevenlabs.similarityBoost";

    /// <summary>Style exaggeration: <c>elevenlabs.style</c> (number). Single voice only.</summary>
    /// <remarks>The provider sets the range; a value it does not accept fails the call.</remarks>
    public const string StyleSetting = "elevenlabs.style";

    /// <summary>Speaker boost: <c>elevenlabs.useSpeakerBoost</c> (boolean). Single voice only.</summary>
    public const string UseSpeakerBoostSetting = "elevenlabs.useSpeakerBoost";

    /// <summary>Speaking speed: <c>elevenlabs.speed</c> (number, 1 is normal). Single voice only.</summary>
    /// <remarks>The provider sets the range; a value it does not accept fails the call.</remarks>
    public const string SpeedSetting = "elevenlabs.speed";

    /// <summary>
    /// Sampling seed: <c>elevenlabs.seed</c> (integer from 0 to 4294967295). Single voice
    /// and dialogue.
    /// </summary>
    /// <remarks>
    /// The provider makes a best effort only: two requests with the same seed returned
    /// different audio on <c>eleven_v4</c> (measured 2026-10-13).
    /// </remarks>
    public const string SeedSetting = "elevenlabs.seed";

    /// <summary>
    /// The text spoken just before this request, for continuous prosody:
    /// <c>elevenlabs.previousText</c> (text). Single voice and dialogue; a dialogue takes at
    /// most 100 characters.
    /// </summary>
    public const string PreviousTextSetting = "elevenlabs.previousText";

    /// <summary>
    /// The text spoken just after this request: <c>elevenlabs.nextText</c> (text). Single
    /// voice (<c>next_text</c>) and dialogue (<c>future_text</c>, at most 100 characters).
    /// </summary>
    public const string NextTextSetting = "elevenlabs.nextText";

    /// <summary>
    /// The provider's identifiers of the requests spoken before this one, the closest last:
    /// <c>elevenlabs.previousRequestIds</c> (list of at most 3 texts). Single voice and
    /// dialogue.
    /// </summary>
    /// <remarks>
    /// Take them from <see cref="SpeechAudio.ProviderRequestId"/> of the earlier results.
    /// The provider ignores them for a request made in its zero-retention mode.
    /// </remarks>
    public const string PreviousRequestIdsSetting = "elevenlabs.previousRequestIds";

    /// <summary>The keys a single-voice <see cref="SpeechRequest"/> accepts.</summary>
    public static IReadOnlyList<string> SupportedSettings { get; } =
    [
        StabilitySetting,
        SimilarityBoostSetting,
        StyleSetting,
        UseSpeakerBoostSetting,
        SpeedSetting,
        SeedSetting,
        PreviousTextSetting,
        NextTextSetting,
        PreviousRequestIdsSetting,
    ];

    /// <summary>The keys a <see cref="SpeechDialogueRequest"/> accepts.</summary>
    /// <remarks>
    /// The dialogue service takes no style, speaker boost or speed. A key from
    /// <see cref="SupportedSettings"/> that is not here is refused on a dialogue rather than
    /// ignored: the service accepts an unknown field without an error (measured
    /// 2026-10-13), so a dropped setting would go unnoticed.
    /// </remarks>
    public static IReadOnlyList<string> DialogueSupportedSettings { get; } =
    [
        StabilitySetting,
        SimilarityBoostSetting,
        SeedSetting,
        PreviousTextSetting,
        NextTextSetting,
        PreviousRequestIdsSetting,
    ];
}
