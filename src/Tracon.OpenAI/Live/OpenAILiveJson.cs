using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>Body of <c>POST /v1/live/sessions</c>.</summary>
internal sealed class OpenAILiveCreateRequest
{
    [JsonPropertyName("transport")]
    public OpenAILiveTransport Transport { get; set; } = new();

    [JsonPropertyName("session")]
    public OpenAILiveSessionBody Session { get; set; } = new();
}

/// <summary>The transport half of a live session request.</summary>
/// <remarks>
/// Only <c>webrtc</c> is accepted. Measured 2026-09-11: <c>websocket</c> and
/// <c>ws</c> both answer <c>400 "Only the webrtc transport is supported."</c>, so a
/// server-side media bridge is not available for this model.
/// </remarks>
internal sealed class OpenAILiveTransport
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "webrtc";

    [JsonPropertyName("sdp")]
    public string? Sdp { get; set; }
}

/// <summary>The session half of a live session request.</summary>
internal sealed class OpenAILiveSessionBody
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }

    [JsonPropertyName("audio")]
    public OpenAILiveAudio? Audio { get; set; }

    [JsonPropertyName("delegation")]
    public OpenAILiveDelegationConfig? Delegation { get; set; }
}

/// <summary>The audio settings of a live session.</summary>
internal sealed class OpenAILiveAudio
{
    [JsonPropertyName("output")]
    public OpenAILiveAudioOutput? Output { get; set; }
}

/// <summary>The output half of a live session's audio settings.</summary>
internal sealed class OpenAILiveAudioOutput
{
    [JsonPropertyName("voice")]
    public string? Voice { get; set; }
}

/// <summary>How the live model hands work back.</summary>
/// <remarks>Measured values: <c>client</c> and <c>responses</c>.</remarks>
internal sealed class OpenAILiveDelegationConfig
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>Response of <c>POST /v1/live/sessions</c>.</summary>
internal sealed class OpenAILiveCreateResponse
{
    [JsonPropertyName("session")]
    public OpenAILiveSessionInfo? Session { get; set; }

    [JsonPropertyName("transport")]
    public OpenAILiveTransport? Transport { get; set; }
}

/// <summary>The session object the provider reports.</summary>
internal sealed class OpenAILiveSessionInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

/// <summary>One event on the sideband socket, in either direction.</summary>
/// <remarks>
/// A single shape covers both directions: the protocol distinguishes events by
/// <c>type</c> and leaves everything else optional. The field names here were read
/// off a real session's raw dump on 2026-09-11, not from documentation.
/// </remarks>
internal sealed class OpenAILiveEventEnvelope
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }

    [JsonPropertyName("delegation_id")]
    public string? DelegationId { get; set; }

    /// <summary>The append payload field is <c>content</c>, not <c>text</c>.</summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("delta")]
    public string? Delta { get; set; }

    [JsonPropertyName("start_ms")]
    public int? StartMs { get; set; }

    [JsonPropertyName("end_ms")]
    public int? EndMs { get; set; }

    [JsonPropertyName("offset_ms")]
    public int? OffsetMs { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("session")]
    public OpenAILiveSessionInfo? Session { get; set; }

    [JsonPropertyName("delegation")]
    public OpenAILiveDelegationInfo? Delegation { get; set; }

    [JsonPropertyName("usage")]
    public OpenAILiveUsage? Usage { get; set; }

    [JsonPropertyName("error")]
    public OpenAILiveError? Error { get; set; }
}

/// <summary>The delegation object carried by <c>session.delegation.created</c>.</summary>
internal sealed class OpenAILiveDelegationInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }
}

/// <summary>The usage object the provider reports.</summary>
/// <remarks>
/// This is the authoritative billable duration. Tracon does not carry the
/// media of a live session and so cannot measure it; a wall clock would disagree with
/// the invoice.
/// </remarks>
internal sealed class OpenAILiveUsage
{
    [JsonPropertyName("seconds")]
    public decimal? Seconds { get; set; }
}

/// <summary>The error object the provider reports.</summary>
internal sealed class OpenAILiveError
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("param")]
    public string? Param { get; set; }
}

/// <summary>The serialization context of the OpenAI live surface.</summary>
/// <remarks>
/// Reflection-based <c>JsonSerializer</c> overloads break AOT compatibility
/// (<c>IL2026</c>/<c>IL3050</c>). The package is marked AOT compatible; every
/// serialization goes through this context.
/// </remarks>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAILiveCreateRequest))]
[JsonSerializable(typeof(OpenAILiveCreateResponse))]
[JsonSerializable(typeof(OpenAILiveEventEnvelope))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class OpenAILiveJsonContext : JsonSerializerContext;
