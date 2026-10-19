using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Reads the <c>messages</c> array of a Chat Completions request into the
/// <see cref="ChatMessage"/> list the agent runs on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>A part is handed over or refused, never dropped.</strong> The
/// reader once kept only <c>text</c> parts: an image or audio part vanished and
/// the model answered as if the user had sent text alone. A part the model can
/// take becomes its Microsoft.Extensions.AI content; any other part answers
/// <c>400</c> with its location and its name.
/// </para>
/// <list type="bullet">
/// <item><description><c>text</c> and <c>refusal</c> → <see cref="TextContent"/>.</description></item>
/// <item><description><c>image_url</c>: a <c>data:</c> URL → <see cref="DataContent"/>, an
/// <c>http(s)</c> URL → <see cref="UriContent"/>; <c>detail</c> travels as the content's
/// <c>detail</c> property, which the OpenAI chat client reads.</description></item>
/// <item><description><c>input_audio</c> (<c>wav</c>, <c>mp3</c>) → <see cref="DataContent"/>.</description></item>
/// <item><description><c>file</c> with <c>file_data</c> → <see cref="DataContent"/> named after
/// <c>filename</c>; a <c>file_id</c> is refused, Tracon keeps no OpenAI file store.</description></item>
/// </list>
/// <para>
/// <strong>Tool history is refused.</strong> Tracon runs the agent's tools on the
/// server, so a client has no tool call to report. An assistant message
/// with <c>tool_calls</c> or <c>function_call</c>, and a <c>tool</c> or
/// <c>function</c> message, answer <c>400</c>; <c>/v1/responses</c> refuses its
/// <c>function_call</c> items too. Before, the assistant message was dropped and the
/// tool result went on as plain text without its call id — a history OpenAI
/// itself refuses.
/// </para>
/// <para>
/// The Microsoft.Extensions.AI OpenAI converter (<c>AsChatMessages</c>) was
/// measured and not used: reading the OpenAI message types needs
/// <c>OpenAIContext</c> (OPENAI001) for a trim-safe read, and the converter drops
/// <c>input_audio</c> without a word as well (2026-10-19).
/// </para>
/// </remarks>
internal static class ChatCompletionsMessageReader
{
    private const string ToolHistoryAdvice =
        "Tracon runs the agent's tools on the server, so a client cannot send tool calls or their results. " +
        "Send the earlier turns as user and assistant text.";

    /// <summary>Reads the <c>messages</c> array of a request body.</summary>
    /// <param name="body">The request body.</param>
    /// <param name="messages">The messages, in order; empty on failure.</param>
    /// <param name="error">Why the body was refused; empty on success.</param>
    /// <returns><see langword="true"/> if every message was read.</returns>
    public static bool TryRead(JsonElement body, out List<ChatMessage> messages, out string error)
    {
        messages = [];

        if (body.ValueKind is not JsonValueKind.Object ||
            !body.TryGetProperty("messages", out var raw) ||
            raw.ValueKind is not JsonValueKind.Array)
        {
            error = "'messages' is required and must be an array.";
            return false;
        }

        var index = 0;

        foreach (var item in raw.EnumerateArray())
        {
            if (!TryReadMessage(item, index, out var message, out error))
            {
                messages = [];
                return false;
            }

            if (message is not null)
            {
                messages.Add(message);
            }

            index++;
        }

        if (messages.Count == 0)
        {
            error = "'messages' cannot be empty.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadMessage(JsonElement item, int index, out ChatMessage? message, out string error)
    {
        message = null;
        var at = string.Create(CultureInfo.InvariantCulture, $"messages[{index}]");

        if (item.ValueKind is not JsonValueKind.Object ||
            !item.TryGetProperty("role", out var roleElement) ||
            roleElement.ValueKind is not JsonValueKind.String)
        {
            error = $"{at}: every message must have a string 'role'.";
            return false;
        }

        var role = roleElement.GetString()!;

        if (role is "tool" or "function")
        {
            error = $"{at} has the role '{role}'. {ToolHistoryAdvice}";
            return false;
        }

        if (string.Equals(role, "assistant", StringComparison.Ordinal))
        {
            if (item.TryGetProperty("tool_calls", out var toolCalls) &&
                toolCalls.ValueKind is JsonValueKind.Array &&
                toolCalls.GetArrayLength() > 0)
            {
                error = $"{at} is an assistant message with 'tool_calls'. {ToolHistoryAdvice}";
                return false;
            }

            if (item.TryGetProperty("function_call", out var functionCall) &&
                functionCall.ValueKind is JsonValueKind.Object)
            {
                error = $"{at} is an assistant message with 'function_call'. {ToolHistoryAdvice}";
                return false;
            }
        }

        if (!TryReadRole(role, out var chatRole))
        {
            error = $"{at} has the role '{role}', which is not a Chat Completions role. " +
                    "Use 'system', 'developer', 'user' or 'assistant'.";
            return false;
        }

        if (!item.TryGetProperty("content", out var content) || content.ValueKind is JsonValueKind.Null)
        {
            // An assistant turn may carry no content (OpenAI allows it next to a
            // refusal); there is nothing to hand over. Every other role must speak.
            if (chatRole == ChatRole.Assistant)
            {
                error = string.Empty;
                return true;
            }

            error = $"{at} ({role}) has no 'content'.";
            return false;
        }

        if (content.ValueKind is JsonValueKind.String)
        {
            message = new ChatMessage(chatRole, content.GetString());
            error = string.Empty;
            return true;
        }

        if (content.ValueKind is not JsonValueKind.Array)
        {
            error = $"{at}.content must be a string or an array of content parts.";
            return false;
        }

        var contents = new List<AIContent>(content.GetArrayLength());
        var partIndex = 0;

        foreach (var part in content.EnumerateArray())
        {
            var partAt = string.Create(CultureInfo.InvariantCulture, $"{at}.content[{partIndex}]");

            if (!TryReadPart(part, partAt, out var aiContent, out error))
            {
                return false;
            }

            contents.Add(aiContent);
            partIndex++;
        }

        if (contents.Count == 0)
        {
            error = $"{at}.content is an empty array.";
            return false;
        }

        message = new ChatMessage(chatRole, contents);
        error = string.Empty;
        return true;
    }

    private static bool TryReadRole(string role, out ChatRole chatRole)
    {
        switch (role)
        {
            case "system":
            // OpenAI uses the "developer" role as the new name replacing "system".
            case "developer":
                chatRole = ChatRole.System;
                return true;
            case "user":
                chatRole = ChatRole.User;
                return true;
            case "assistant":
                chatRole = ChatRole.Assistant;
                return true;
            default:
                chatRole = default;
                return false;
        }
    }

    private static bool TryReadPart(JsonElement part, string at, out AIContent content, out string error)
    {
        content = null!;

        if (part.ValueKind is not JsonValueKind.Object ||
            !part.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind is not JsonValueKind.String)
        {
            error = $"{at}: every content part must have a string 'type'.";
            return false;
        }

        var type = typeElement.GetString()!;

        switch (type)
        {
            case "text":
            case "refusal":
                if (!TryReadString(part, type, out var text))
                {
                    error = $"{at} is a '{type}' part without a string '{type}'.";
                    return false;
                }

                content = new TextContent(text);
                error = string.Empty;
                return true;

            case "image_url":
                return TryReadImage(part, at, out content, out error);

            case "input_audio":
                return TryReadAudio(part, at, out content, out error);

            case "file":
                return TryReadFile(part, at, out content, out error);

            default:
                error = $"{at} has the type '{type}', which Tracon cannot hand to the model. " +
                        "Supported parts: 'text', 'image_url', 'input_audio', 'file'.";
                return false;
        }
    }

    private static bool TryReadImage(JsonElement part, string at, out AIContent content, out string error)
    {
        content = null!;

        if (!part.TryGetProperty("image_url", out var image) ||
            image.ValueKind is not JsonValueKind.Object ||
            !TryReadString(image, "url", out var url))
        {
            error = $"{at} is an 'image_url' part without a string 'image_url.url'.";
            return false;
        }

        if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryCreateData(url, out var data) || !data.HasTopLevelMediaType("image"))
            {
                error = $"{at}: 'image_url.url' is not a base64 data URL with an image media type.";
                return false;
            }

            content = data;
        }
        else if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                 (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)))
        {
            content = new UriContent(uri, "image/*");
        }
        else
        {
            error = $"{at}: 'image_url.url' must be an http(s) URL or a data URL.";
            return false;
        }

        if (TryReadString(image, "detail", out var detail))
        {
            content.AdditionalProperties = new() { ["detail"] = detail };
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadAudio(JsonElement part, string at, out AIContent content, out string error)
    {
        content = null!;

        if (!part.TryGetProperty("input_audio", out var audio) ||
            audio.ValueKind is not JsonValueKind.Object ||
            !TryReadString(audio, "data", out var data) ||
            !TryReadString(audio, "format", out var format))
        {
            error = $"{at} is an 'input_audio' part without string 'input_audio.data' and 'input_audio.format'.";
            return false;
        }

        string mediaType;

        switch (format)
        {
            case "wav":
                mediaType = "audio/wav";
                break;
            case "mp3":
                mediaType = "audio/mpeg";
                break;
            default:
                error = $"{at}: the 'input_audio' format '{format}' is not supported; use 'wav' or 'mp3'.";
                return false;
        }

        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            error = $"{at}: 'input_audio.data' is not base64.";
            return false;
        }

        content = new DataContent(bytes, mediaType);
        error = string.Empty;
        return true;
    }

    private static bool TryReadFile(JsonElement part, string at, out AIContent content, out string error)
    {
        content = null!;

        if (!part.TryGetProperty("file", out var file) || file.ValueKind is not JsonValueKind.Object)
        {
            error = $"{at} is a 'file' part without a 'file' object.";
            return false;
        }

        if (file.TryGetProperty("file_id", out _))
        {
            error = $"{at} names a 'file_id'. Tracon keeps no OpenAI file store; send the file inline as 'file_data'.";
            return false;
        }

        if (!TryReadString(file, "file_data", out var fileData) || !TryCreateData(fileData, out var data))
        {
            error = $"{at}: 'file.file_data' must be a base64 data URL.";
            return false;
        }

        if (TryReadString(file, "filename", out var filename))
        {
            data.Name = filename;
        }

        content = data;
        error = string.Empty;
        return true;
    }

    private static bool TryCreateData(string dataUrl, out DataContent data)
    {
        try
        {
            data = new DataContent(dataUrl);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            data = null!;
            return false;
        }
    }

    private static bool TryReadString(JsonElement element, string property, out string value)
    {
        if (element.TryGetProperty(property, out var raw) && raw.ValueKind is JsonValueKind.String)
        {
            value = raw.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
