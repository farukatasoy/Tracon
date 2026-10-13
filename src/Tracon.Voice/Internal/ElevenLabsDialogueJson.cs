using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Body of an ElevenLabs text-to-dialogue request.</summary>
/// <remarks>
/// Verified against the provider's published OpenAPI document (2026-10-13):
/// <c>Body_Text_to_dialogue__multi_voice__v1_text_to_dialogue_post</c> and
/// <c>Body_text_to_dialogue_full_with_timestamps</c> share these fields. The "next" text
/// is <c>future_text</c> here, not the single-voice endpoint's <c>next_text</c>.
/// </remarks>
internal sealed class ElevenLabsDialogueRequest
{
    [JsonPropertyName("inputs")]
    public List<ElevenLabsDialogueInput> Inputs { get; set; } = [];

    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; set; }

    /// <summary>Omitted when the request sets neither field.</summary>
    [JsonPropertyName("settings")]
    public ElevenLabsDialogueSettings? Settings { get; set; }

    [JsonPropertyName("seed")]
    public long? Seed { get; set; }

    [JsonPropertyName("previous_text")]
    public string? PreviousText { get; set; }

    [JsonPropertyName("future_text")]
    public string? FutureText { get; set; }

    [JsonPropertyName("previous_request_ids")]
    public List<string>? PreviousRequestIds { get; set; }
}

/// <summary>One <c>DialogueInput</c>: a text and the voice that speaks it.</summary>
internal sealed class ElevenLabsDialogueInput
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("voice_id")]
    public string VoiceId { get; set; } = string.Empty;
}

/// <summary>
/// ElevenLabs <c>ToDialogueSettingsResponseModel</c>: the only two settings the dialogue
/// service documents.
/// </summary>
internal sealed class ElevenLabsDialogueSettings
{
    [JsonPropertyName("stability")]
    public double? Stability { get; set; }

    [JsonPropertyName("similarity")]
    public double? Similarity { get; set; }
}

/// <summary>ElevenLabs <c>AudioWithTimestampsAndVoiceSegmentsResponseModel</c>.</summary>
/// <remarks>
/// <see cref="Alignment"/> times the inputs' texts joined in order with no separator
/// (measured 2026-10-13: two inputs "Merhaba." and "Selam." gave 14 characters).
/// </remarks>
internal sealed class ElevenLabsDialogueWithTimestampsResponse
{
    [JsonPropertyName("audio_base64")]
    public string? AudioBase64 { get; set; }

    [JsonPropertyName("alignment")]
    public ElevenLabsCharacterAlignment? Alignment { get; set; }

    [JsonPropertyName("voice_segments")]
    public List<ElevenLabsVoiceSegment>? VoiceSegments { get; set; }
}

/// <summary>ElevenLabs <c>VoiceSegment</c>: the time range of one part of one input.</summary>
/// <remarks>
/// The provider documents a segment as "a part of" an input, so one input may have more
/// than one segment; a two-input request returned one segment per input (measured
/// 2026-10-13).
/// </remarks>
internal sealed class ElevenLabsVoiceSegment
{
    [JsonPropertyName("voice_id")]
    public string? VoiceId { get; set; }

    [JsonPropertyName("start_time_seconds")]
    public double? StartTimeSeconds { get; set; }

    [JsonPropertyName("end_time_seconds")]
    public double? EndTimeSeconds { get; set; }

    [JsonPropertyName("dialogue_input_index")]
    public int? DialogueInputIndex { get; set; }
}
