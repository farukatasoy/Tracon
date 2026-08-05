using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>ElevenLabs metinden ses istegi govdesi.</summary>
internal sealed class ElevenLabsSynthesisRequest
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string? ModelId { get; set; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; set; }
}

/// <summary>ElevenLabs sesten metin yaniti.</summary>
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

/// <summary>ElevenLabs ses listesi yaniti.</summary>
internal sealed class ElevenLabsVoicesResponse
{
    [JsonPropertyName("voices")]
    public List<ElevenLabsVoice>? Voices { get; set; }
}

/// <summary>Listedeki tek bir ses.</summary>
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
/// ElevenLabs govdeleri icin kaynak uretilmis seri hale getirme baglami.
/// </summary>
/// <remarks>
/// 🚨 Yansimaya dayali <c>JsonSerializer</c> asiri yuklemeleri AOT uyumlulugunu
/// bozar (<c>IL2026</c>/<c>IL3050</c>). Paket AOT uyumlu isaretlidir; her
/// seri hale getirme bu baglamdan gecer.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ElevenLabsSynthesisRequest))]
[JsonSerializable(typeof(ElevenLabsTranscriptionResponse))]
[JsonSerializable(typeof(ElevenLabsVoicesResponse))]
[JsonSerializable(typeof(ElevenLabsVoice))]
internal sealed partial class ElevenLabsJsonContext : JsonSerializerContext;
