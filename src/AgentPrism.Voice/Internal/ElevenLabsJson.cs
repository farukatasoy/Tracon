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

/// <summary>ElevenLabs voice list response.</summary>
internal sealed class ElevenLabsVoicesResponse
{
    [JsonPropertyName("voices")]
    public List<ElevenLabsVoice>? Voices { get; set; }
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
}

/// <summary>
/// Source-generated serialization context for ElevenLabs bodies.
/// </summary>
/// <remarks>
/// 🚨 Reflection-based <c>JsonSerializer</c> overloads break AOT compatibility
/// (<c>IL2026</c>/<c>IL3050</c>). The package is marked AOT compatible; every
/// serialization goes through this context.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ElevenLabsSynthesisRequest))]
[JsonSerializable(typeof(ElevenLabsTranscriptionResponse))]
[JsonSerializable(typeof(ElevenLabsVoicesResponse))]
[JsonSerializable(typeof(ElevenLabsVoice))]
internal sealed partial class ElevenLabsJsonContext : JsonSerializerContext;
