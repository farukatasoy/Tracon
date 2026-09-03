using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>Body of an ElevenLabs text-to-speech request.</summary>
internal sealed class ElevenLabsSynthesisRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; set; }
}

/// <summary>ElevenLabs <c>.../with-timestamps</c> response.</summary>
/// <remarks>
/// Verified against the provider's published OpenAPI document (2026-08-19):
/// <c>AudioWithTimestampsResponseModel</c>. <see cref="Alignment"/> carries the
/// timing for the text AS SENT; <see cref="NormalizedAlignment"/> carries timing
/// for the provider's own normalized text (numbers spelled out, etc.) and would
/// not line up with the caller's original string - only <see cref="Alignment"/> is used.
/// </remarks>
internal sealed class ElevenLabsAudioWithTimestampsResponse
{
    [JsonPropertyName("audio_base64")]
    public string? AudioBase64 { get; set; }

    [JsonPropertyName("alignment")]
    public ElevenLabsCharacterAlignment? Alignment { get; set; }

    [JsonPropertyName("normalized_alignment")]
    public ElevenLabsCharacterAlignment? NormalizedAlignment { get; set; }
}

/// <summary>ElevenLabs <c>CharacterAlignmentResponseModel</c>: parallel arrays, one entry per character.</summary>
internal sealed class ElevenLabsCharacterAlignment
{
    [JsonPropertyName("characters")]
    public List<string>? Characters { get; set; }

    [JsonPropertyName("character_start_times_seconds")]
    public List<double>? CharacterStartTimesSeconds { get; set; }

    [JsonPropertyName("character_end_times_seconds")]
    public List<double>? CharacterEndTimesSeconds { get; set; }
}

/// <summary>ElevenLabs speech-to-text response.</summary>
internal sealed class ElevenLabsTranscriptionResponse
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("language_probability")]
    public double? LanguageProbability { get; set; }

    [JsonPropertyName("audio_duration_secs")]
    public double? AudioDurationSeconds { get; set; }
}

/// <summary>ElevenLabs <c>/v2/voices</c> response.</summary>
internal sealed class ElevenLabsVoicesResponse
{
    [JsonPropertyName("voices")]
    public List<ElevenLabsVoice>? Voices { get; set; }

    /// <summary>
    /// Whether another page follows. The endpoint defaults to <c>page_size=10</c>
    /// when unspecified, so a client that ignores this field silently truncates
    /// any catalog larger than one page.
    /// </summary>
    [JsonPropertyName("has_more")]
    public bool HasMore { get; set; }

    /// <summary>The token to pass as <c>next_page_token</c> to fetch the next page.</summary>
    [JsonPropertyName("next_page_token")]
    public string? NextPageToken { get; set; }
}

/// <summary>A single voice in the list.</summary>
internal sealed class ElevenLabsVoice
{
    [JsonPropertyName("voice_id")]
    public string? VoiceId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    /// <summary>
    /// Provider-reported scalar labels (for example <c>gender</c>, <c>accent</c>,
    /// <c>age</c>, <c>use_case</c>). Modeled as <see cref="JsonElement"/> values,
    /// not <see cref="string"/>: the provider's schema promises scalars, but a
    /// malformed or future non-string value must not crash the whole list's
    /// deserialization. <see cref="VoiceAttributeMapper"/> does the safe narrowing.
    /// </summary>
    [JsonPropertyName("labels")]
    public Dictionary<string, JsonElement>? Labels { get; set; }

    /// <summary>
    /// The languages this voice is verified for, one entry per model. Deliberately
    /// NOT part of <see cref="Labels"/> — ElevenLabs' <c>VoiceResponseModel</c>
    /// carries language here, not in <c>labels</c> (measured 2026-09-03).
    /// </summary>
    [JsonPropertyName("verified_languages")]
    public List<ElevenLabsVerifiedLanguage>? VerifiedLanguages { get; set; }
}

/// <summary>One language ElevenLabs has verified a voice for, tied to a specific model.</summary>
internal sealed class ElevenLabsVerifiedLanguage
{
    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

/// <summary>
/// Source-generated serialization context for ElevenLabs bodies.
/// </summary>
/// <remarks>
/// Reflection-based <c>JsonSerializer</c> overloads break AOT compatibility
/// (<c>IL2026</c>/<c>IL3050</c>). The package is marked AOT compatible; every
/// serialization goes through this context.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ElevenLabsSynthesisRequest))]
[JsonSerializable(typeof(ElevenLabsAudioWithTimestampsResponse))]
[JsonSerializable(typeof(ElevenLabsCharacterAlignment))]
[JsonSerializable(typeof(ElevenLabsTranscriptionResponse))]
[JsonSerializable(typeof(ElevenLabsVoicesResponse))]
[JsonSerializable(typeof(ElevenLabsVoice))]
[JsonSerializable(typeof(ElevenLabsVerifiedLanguage))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class ElevenLabsJsonContext : JsonSerializerContext;
