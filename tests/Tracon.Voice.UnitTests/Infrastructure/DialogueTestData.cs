using System.Net;
using System.Text.Json;

namespace Tracon.Voice.UnitTests.Infrastructure;

/// <summary>Shared builders for the dialogue tests. No test reaches the real provider.</summary>
internal static class DialogueTestData
{
    /// <summary>A valid MP3 header: ID3 tag + at least one byte.</summary>
    public static readonly byte[] Mp3Bytes = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    public static ElevenLabsSpeechClient CreateClient(
        StubHttpMessageHandler handler,
        Action<VoiceOptions>? configure = null)
    {
        var options = new VoiceOptions
        {
            ApiKey = "FAKE-KEY-DIALOGUE",
            DefaultVoiceId = "voice-default",
        };

        configure?.Invoke(options);

        return new ElevenLabsSpeechClient(options, new HttpClient(handler));
    }

    /// <summary>A two-turn request between two voices.</summary>
    public static SpeechDialogueRequest TwoTurns(bool includeTimestamps = false)
        => new()
        {
            Turns =
            [
                new SpeechDialogueTurn { Text = "Merhaba.", VoiceId = "voice-a" },
                new SpeechDialogueTurn { Text = "Selam.", VoiceId = "voice-b" },
            ],
            IncludeTimestamps = includeTimestamps,
        };

    /// <summary>A settings dictionary from key/value pairs; the values go through the JSON serializer.</summary>
    public static IReadOnlyDictionary<string, JsonElement> Settings(params (string Key, object? Value)[] pairs)
        => pairs.ToDictionary(
            static pair => pair.Key,
            static pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal);

    /// <summary>A timestamped dialogue response with the given segments JSON.</summary>
    public static HttpResponseMessage TimestampedResponse(string voiceSegmentsJson, string? characterCost = null, string? requestId = null)
    {
        var audioBase64 = Convert.ToBase64String(Mp3Bytes);
        var response = StubHttpMessageHandler.Json(
            $$"""
            {
              "audio_base64": "{{audioBase64}}",
              "alignment": {
                "characters": ["M", "S"],
                "character_start_times_seconds": [0.0, 0.96],
                "character_end_times_seconds": [0.5, 1.2]
              },
              "voice_segments": {{voiceSegmentsJson}}
            }
            """);

        AddHeaders(response, characterCost, requestId);

        return response;
    }

    /// <summary>The two segments a live two-turn request returned (2026-10-13), as JSON.</summary>
    public const string TwoSegments =
        """
        [
          {"voice_id": "voice-a", "start_time_seconds": 0.0, "end_time_seconds": 0.96, "character_start_index": 0, "character_end_index": 8, "dialogue_input_index": 0},
          {"voice_id": "voice-b", "start_time_seconds": 0.96, "end_time_seconds": 1.68, "character_start_index": 8, "character_end_index": 14, "dialogue_input_index": 1}
        ]
        """;

    /// <summary>A raw audio response, as the plain dialogue endpoint answers.</summary>
    public static HttpResponseMessage RawResponse(string? characterCost = null, string? requestId = null)
    {
        var response = StubHttpMessageHandler.Binary(Mp3Bytes);

        AddHeaders(response, characterCost, requestId);

        return response;
    }

    /// <summary>A failed response whose body echoes the request, as the provider's does.</summary>
    public static HttpResponseMessage Failure(HttpStatusCode status, string echoedBody)
        => new(status) { Content = new StringContent(echoedBody) };

    private static void AddHeaders(HttpResponseMessage response, string? characterCost, string? requestId)
    {
        if (characterCost is not null)
        {
            response.Headers.Add("character-cost", characterCost);
        }

        if (requestId is not null)
        {
            response.Headers.Add("request-id", requestId);
        }
    }
}
