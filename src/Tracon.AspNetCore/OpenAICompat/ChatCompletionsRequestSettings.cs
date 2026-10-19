using System.Text.Json;

namespace Tracon;

/// <summary>
/// Finds the Chat Completions request settings that the agent definition owns.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A setting is honored or refused, never ignored.</strong> The model
/// in a Chat Completions request names a Tracon agent, and that agent's
/// definition sets its sampling, its output limit, its tools and its response
/// format. A client that sends one of them expects it to apply; answering
/// without it would return, for example, free text to a client that asked for
/// JSON. The request is refused with <c>400</c> (<c>invalid_request_error</c>),
/// and the message names every such field at once.
/// </para>
/// <para>
/// This is the rule <c>/v1/responses</c> applies through Microsoft Agent
/// Framework's default request mapping, and the field list extends the one
/// MAF's own Chat Completions host refuses. A field that asks for the default
/// (<c>n: 1</c>, a <c>text</c> response format, <c>logprobs: false</c>, an
/// empty tool list, text-only modalities) or that is <see langword="null"/>
/// changes nothing and is accepted. A field that carries no setting the agent
/// uses (<c>user</c>, <c>store</c>, <c>stream_options</c>, <c>metadata</c>) is
/// accepted as well.
/// </para>
/// </remarks>
internal static class ChatCompletionsRequestSettings
{
    /// <summary>Fields the definition owns whenever they carry a value.</summary>
    private static readonly string[] OwnedFields =
    [
        "temperature",
        "top_p",
        "max_tokens",
        "max_completion_tokens",
        "frequency_penalty",
        "presence_penalty",
        "seed",
        "stop",
        "logit_bias",
        "reasoning_effort",
        "tool_choice",
        "function_call",
        "top_logprobs",
        "audio",
        "web_search_options",
    ];

    /// <summary>Returns the owned fields the request sets, in request order.</summary>
    /// <param name="body">The request body.</param>
    /// <returns>The field names; empty when the request sets none.</returns>
    public static IReadOnlyList<string> FindRefused(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        List<string>? refused = null;

        foreach (var property in body.EnumerateObject())
        {
            var value = property.Value;

            if (value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            var isRefused = property.Name switch
            {
                "n" => !(value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n == 1),
                "logprobs" => value.ValueKind != JsonValueKind.False,
                "response_format" => !IsTextFormat(value),
                "tools" or "functions" => !(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0),
                "modalities" => !IsTextOnly(value),
                _ => Array.IndexOf(OwnedFields, property.Name) >= 0,
            };

            if (isRefused)
            {
                (refused ??= []).Add(property.Name);
            }
        }

        return refused ?? (IReadOnlyList<string>)[];
    }

    /// <summary>Describes the refused fields for the error message.</summary>
    /// <param name="fields">The refused fields.</param>
    /// <returns>The message.</returns>
    public static string Describe(IReadOnlyList<string> fields)
        => $"The following request setting(s) are not supported by this agent endpoint: {string.Join(", ", fields)}. " +
           "The agent definition sets them; remove them from the request, or change the agent.";

    private static bool IsTextFormat(JsonElement value)
        => value.ValueKind == JsonValueKind.Object
           && value.TryGetProperty("type", out var type)
           && type.ValueKind == JsonValueKind.String
           && string.Equals(type.GetString(), "text", StringComparison.Ordinal);

    private static bool IsTextOnly(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !string.Equals(item.GetString(), "text", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
