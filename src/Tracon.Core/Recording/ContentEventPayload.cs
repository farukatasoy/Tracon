using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Builds the <see cref="RunEventCustomTypes.Content"/> event for model content the run
/// recording writes no event of its own for, and holds the payload helpers the workflow
/// event description shares.
/// </summary>
/// <remarks>
/// <para>
/// The payload is written by hand with <see cref="Utf8JsonWriter"/>: the content types
/// are polymorphic, a future one is unknown to any serializer context, and the payload
/// must stay a fixed set of keys. It never carries a body: not the bytes of a
/// <see cref="DataContent"/>, not a <see cref="UriContent"/> address (a signed URL can
/// carry a credential in its path or query), not a hosted tool's inputs or outputs. The one
/// free text it can carry is the provider's error message, and the text data of a workflow
/// event, both cut so the JSON stays whole.
/// </para>
/// <para>
/// <c>Text</c> is a short summary that carries no user data, so it survives
/// <c>RecordToolPayloads</c> being off; the payload does not (see
/// <c>RunEventWriter</c>).
/// </para>
/// <para>
/// A string that came from outside is <strong>sanitized</strong> before it is written: a
/// lone surrogate (a provider that decodes a <c>\ud83d</c> escape can produce one) becomes
/// U+FFFD here, the same way on every path, instead of wherever a writer or a database
/// driver happens to meet it. A cut never separates the halves of a surrogate pair.
/// </para>
/// </remarks>
internal static class ContentEventPayload
{
    /// <summary>
    /// The longest free text a notice payload carries (a provider error message, a workflow
    /// event's text data), whatever the writer's own limit is.
    /// </summary>
    internal const int MaxTextLength = 2048;

    /// <summary>Describes one piece of content as a reserved <c>Custom</c> event draft.</summary>
    /// <param name="content">The content no other event covers.</param>
    /// <param name="maxPayloadLength">
    /// The writer's <c>MaxPayloadLength</c>, or zero or less for none. The free text is cut so
    /// the whole payload fits it: the writer cuts a payload by the characters of its escaped
    /// JSON without regard to its shape, and a cut JSON document cannot be parsed.
    /// </param>
    /// <returns>The draft to append with the writer's reserved path.</returns>
    public static RunEventDraft Describe(AIContent content, int maxPayloadLength = 0)
    {
        ArgumentNullException.ThrowIfNull(content);

        var (text, payload) = content switch
        {
            WebSearchToolCallContent call => HostedTool("web_search", isCall: true, call.CallId),
            WebSearchToolResultContent result => HostedTool("web_search", isCall: false, result.CallId),
            CodeInterpreterToolCallContent call => HostedTool("code_interpreter", isCall: true, call.CallId),
            CodeInterpreterToolResultContent result => HostedTool("code_interpreter", isCall: false, result.CallId),
            ImageGenerationToolCallContent call => HostedTool("image_generation", isCall: true, call.CallId),
            ImageGenerationToolResultContent result => HostedTool("image_generation", isCall: false, result.CallId),
            McpServerToolCallContent call => HostedTool("mcp_server", isCall: true, call.CallId),
            McpServerToolResultContent result => HostedTool("mcp_server", isCall: false, result.CallId),
            ToolResultContent result when HostedToolContent.IsFileSearchResult(result) => HostedTool("file_search", isCall: false, result.CallId),
            ErrorContent error => DescribeError(error, maxPayloadLength),
            DataContent data => DescribeData(data),
            UriContent uri => DescribeUri(uri),
            HostedFileContent file => HostedId("hosted_file", "hosted file", file.FileId),
            HostedVectorStoreContent store => HostedId("hosted_vector_store", "hosted vector store", store.VectorStoreId),
            _ => Unrecognized(content),
        };

        return new RunEventDraft(RunEventType.Custom)
        {
            CustomType = RunEventCustomTypes.Content,
            Text = text,
            Payload = payload,
        };
    }

    /// <summary>
    /// Builds a payload whose one free text is cut until the payload fits
    /// <paramref name="maxPayloadLength"/>.
    /// </summary>
    /// <param name="maxPayloadLength">The writer's limit, or zero or less for none.</param>
    /// <param name="text">The free text, or <see langword="null"/>.</param>
    /// <param name="build">Builds the payload around the (possibly cut) text.</param>
    /// <returns>The payload.</returns>
    /// <remarks>
    /// Every character of the text is at least one character of escaped JSON, so dropping as
    /// many characters as the payload is over its limit is enough; a character outside ASCII
    /// costs up to six, which is why a fixed cap in characters is not. When even an empty
    /// text does not fit, the limit is smaller than the payload's fixed keys and the writer
    /// cuts it.
    /// </remarks>
    internal static string BuildFitting(int maxPayloadLength, string? text, Func<string?, string> build)
    {
        var clean = Sanitize(text);
        var length = Math.Min(clean?.Length ?? 0, MaxTextLength);
        var payload = build(Cut(clean, length));

        while (maxPayloadLength > 0 && payload.Length > maxPayloadLength && length > 0)
        {
            length = Math.Max(0, length - (payload.Length - maxPayloadLength));
            payload = build(Cut(clean, length));
        }

        return payload;
    }

    /// <summary>Writes one JSON object, with every string sanitized by <see cref="WriteString"/>.</summary>
    /// <param name="writeFields">Writes the fields of the object.</param>
    /// <returns>The JSON text.</returns>
    internal static string Build(Action<Utf8JsonWriter> writeFields)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeFields(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Writes a string field with each lone surrogate replaced by U+FFFD.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value, or <see langword="null"/> for JSON null.</param>
    internal static void WriteString(Utf8JsonWriter writer, string name, string? value)
        => writer.WriteString(name, Sanitize(value));

    /// <summary>Replaces each lone surrogate of <paramref name="value"/> with U+FFFD.</summary>
    /// <param name="value">The text, or <see langword="null"/>.</param>
    /// <returns>The same instance when nothing needed replacing.</returns>
    internal static string? Sanitize(string? value)
    {
        if (value is null)
        {
            return null;
        }

        StringBuilder? clean = null;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                clean?.Append(c).Append(value[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                clean ??= new StringBuilder(value.Length).Append(value, 0, i);
                clean.Append('�');
            }
            else
            {
                clean?.Append(c);
            }
        }

        return clean?.ToString() ?? value;
    }

    /// <summary>
    /// Keeps the first <paramref name="length"/> characters of <paramref name="value"/> and ends a
    /// shortened text with the truncation marker, never between the halves of a surrogate pair.
    /// </summary>
    internal static string? Cut(string? value, int length)
    {
        if (value is null || length >= value.Length)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, SurrogateSafeText.PrefixLength(value, length)), RunEventWriter.TruncationMarker);
    }

    private static (string Text, string Payload) HostedTool(string tool, bool isCall, string callId)
    {
        var type = isCall ? "hosted_tool_call" : "hosted_tool_result";

        return (
            $"{tool} {(isCall ? "call" : "result")}",
            Build(writer =>
            {
                WriteString(writer, "type", type);
                WriteString(writer, "tool", tool);
                WriteString(writer, "callId", callId);
            }));
    }

    private static (string Text, string Payload) DescribeError(ErrorContent error, int maxPayloadLength)
    {
        var code = Sanitize(error.ErrorCode);

        return (
            string.IsNullOrEmpty(code) ? "error" : $"error: {code}",
            BuildFitting(
                maxPayloadLength,
                error.Message,
                message => Build(writer =>
                {
                    WriteString(writer, "type", "error");
                    WriteString(writer, "errorCode", code);
                    WriteString(writer, "message", message);
                })));
    }

    private static (string Text, string Payload) DescribeData(DataContent data)
        => (
            $"data: {Sanitize(data.MediaType)}",
            Build(writer =>
            {
                WriteString(writer, "type", "data");
                WriteString(writer, "mediaType", data.MediaType);
                writer.WriteNumber("length", data.Data.Length);
            }));

    private static (string Text, string Payload) DescribeUri(UriContent uri)
        => (
            $"uri: {Sanitize(uri.MediaType)}",
            Build(writer =>
            {
                WriteString(writer, "type", "uri");
                WriteString(writer, "mediaType", uri.MediaType);
            }));

    private static (string Text, string Payload) HostedId(string type, string text, string id)
        => (
            text,
            Build(writer =>
            {
                WriteString(writer, "type", type);
                WriteString(writer, "id", id);
            }));

    private static (string Text, string Payload) Unrecognized(AIContent content)
    {
        var clrType = content.GetType();

        return (
            $"unrecognized content: {clrType.Name}",
            Build(writer =>
            {
                WriteString(writer, "type", "unrecognized");
                WriteString(writer, "clrType", clrType.FullName ?? clrType.Name);
            }));
    }
}
