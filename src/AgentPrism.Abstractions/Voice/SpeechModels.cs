namespace AgentPrism;

/// <summary>A text-to-speech generation request.</summary>
public sealed record SpeechRequest
{
    /// <summary>The text to speak.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The identifier of the voice to use. <c>VoiceOptions.DefaultVoiceId</c> if empty.
    /// </summary>
    public string? VoiceId { get; init; }

    /// <summary>
    /// The synthesis model to use. <c>VoiceOptions.SynthesisModelId</c> if empty.
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// The provider's output format name (for example, <c>mp3_44100_128</c>).
    /// </summary>
    /// <remarks>
    /// 🚨 This is <strong>not</strong> a MIME type. Also, not every format can
    /// be stored as an attachment: <c>pcm_*</c> and <c>ulaw_*</c> outputs are
    /// headerless, match no magic byte, and are rejected by
    /// <c>AttachmentTypeGuard</c>. See <c>docs/28-SES-TOOLLARI.md</c>, section
    /// 28.0/G3, for the rationale.
    /// </remarks>
    public string? OutputFormat { get; init; }

    /// <summary>The language's ISO 639-1 code. The provider detects it itself if empty.</summary>
    public string? LanguageCode { get; init; }
}

/// <summary>Generated audio.</summary>
/// <remarks>
/// The type is <strong>not</strong> a <c>record</c>: since the
/// <see cref="Data"/> field can be large, the generated <c>ToString</c>/equality
/// comparison must not accidentally copy the entire content. The same
/// rationale applies to <c>AttachmentContent</c>.
/// </remarks>
public sealed class SpeechAudio
{
    /// <summary>The raw audio bytes.</summary>
    public required ReadOnlyMemory<byte> Data { get; init; }

    /// <summary>The verified MIME type (for example, <c>audio/mpeg</c>).</summary>
    public required string MediaType { get; init; }

    /// <summary>The audio's duration. <see langword="null"/> if the provider does not report it.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>The billed character count.</summary>
    public int? CharactersBilled { get; init; }

    /// <summary>
    /// Where the <see cref="CharactersBilled"/> value came from.
    /// </summary>
    /// <remarks>
    /// If the provider does not report the count, the text's length is used
    /// and the value becomes <see cref="SpeechUsageSource.Estimated"/>.
    /// Showing an estimate as a measurement fabricates a price (K-032).
    /// </remarks>
    public SpeechUsageSource UsageSource { get; init; }
}

/// <summary>The source of a speech measurement.</summary>
public enum SpeechUsageSource
{
    /// <summary>No measurement.</summary>
    Unknown = 0,

    /// <summary>Reported by the provider.</summary>
    Provider = 1,

    /// <summary>Estimated from the request.</summary>
    Estimated = 2,
}

/// <summary>The result of speech-to-text conversion.</summary>
public sealed record SpeechTranscript
{
    /// <summary>The transcribed text.</summary>
    public required string Text { get; init; }

    /// <summary>The detected language's code.</summary>
    public string? LanguageCode { get; init; }

    /// <summary>The confidence of language detection (0-1).</summary>
    public double? LanguageProbability { get; init; }

    /// <summary>
    /// The duration of the transcribed audio. Cost is computed from this.
    /// </summary>
    public TimeSpan? AudioDuration { get; init; }
}

/// <summary>Speech-to-text conversion settings.</summary>
public sealed record SpeechTranscriptionOptions
{
    /// <summary>The model to use. <c>VoiceOptions.TranscriptionModelId</c> if empty.</summary>
    public string? ModelId { get; init; }

    /// <summary>The expected language's ISO 639-1 code. The provider detects it if empty.</summary>
    public string? LanguageCode { get; init; }
}

/// <summary>The definition of an available voice.</summary>
/// <remarks>
/// The provider's <c>preview_url</c> field is DELIBERATELY not carried:
/// playing it in the UI would connect the browser to the provider's address.
/// Adding it later is not a breaking change; removing it would be.
/// </remarks>
public sealed record VoiceDescriptor
{
    /// <summary>The voice identifier. Used in agent definitions and requests.</summary>
    public required string VoiceId { get; init; }

    /// <summary>The human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>The category given by the provider (for example, <c>premade</c>).</summary>
    public string? Category { get; init; }
}

/// <summary>The voice provider's reachability status.</summary>
/// <remarks>
/// <c>ModelProviderHealth</c> is DELIBERATELY not reused: a voice provider is
/// not an <c>IModelProvider</c> and must not appear in the
/// <c>/api/models/health</c> output. If the two sources were combined into
/// one list, the circuit breaker and model catalog would behave incorrectly.
/// </remarks>
public sealed record VoiceHealth
{
    /// <summary>The provider name.</summary>
    public required string ProviderName { get; init; }

    /// <summary>Whether the provider is reachable.</summary>
    public required bool IsHealthy { get; init; }

    /// <summary>The check's duration.</summary>
    public required TimeSpan Latency { get; init; }

    /// <summary>The moment the check was performed (UTC).</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// The reason, if failed. 🚨 The text carries neither an API key nor an address.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>The number of voices seen during the check.</summary>
    public int? VoiceCount { get; init; }
}
