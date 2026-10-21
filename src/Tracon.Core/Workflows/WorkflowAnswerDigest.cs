using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Turns an answer to a workflow's pending human request into the SHA-256 that tells two
/// answers apart.
/// </summary>
/// <remarks>
/// <para>
/// The hash is taken over a canonical JSON object of four members, <c>approved</c>,
/// <c>data</c>, <c>requestId</c> and <c>text</c>, written in that (ordinal) order with no
/// whitespace. A fifth, <c>remember</c>, is written between <c>data</c> and <c>requestId</c> only
/// when it is <see langword="true"/>: an answer that saves a rule is another answer than one that does
/// not, and every hash stored before the member existed stays what it was.
/// </para>
/// <para>
/// <c>data</c> is rewritten by the same rule at every depth: object members in ordinal order,
/// strings re-encoded from their value, numbers kept as they were written
/// (<c>1.0</c> and <c>1</c> are different answers). <c>text</c> is taken as it is, never
/// trimmed. The checkpoint and the definition version an answer names are not part of it:
/// they say where the answer is applied, not what it is.
/// </para>
/// <para>
/// The hash is for equality, not secrecy: an answer with few possible values can be found
/// by hashing each candidate. Neither the answer nor the canonical text is kept anywhere.
/// </para>
/// </remarks>
internal static class WorkflowAnswerDigest
{
    /// <summary>Computes the digest of an answer.</summary>
    /// <param name="requestId">The request the answer is for.</param>
    /// <param name="approved">The yes/no answer, when one was given.</param>
    /// <param name="text">The text answer, when one was given.</param>
    /// <param name="json">The JSON text of the free-form answer, when one was given.</param>
    /// <param name="remember">Whether the answer also saves a tool approval as a rule.</param>
    /// <returns>The SHA-256 of the canonical answer, as 64 lowercase hexadecimal characters.</returns>
    /// <exception cref="ArgumentException"><paramref name="requestId"/> is empty.</exception>
    public static string Compute(string requestId, bool? approved, string? text, string? json, bool remember = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();

            if (approved is { } value)
            {
                writer.WriteBoolean("approved", value);
            }
            else
            {
                writer.WriteNull("approved");
            }

            writer.WritePropertyName("data");
            WriteData(writer, json);

            if (remember)
            {
                writer.WriteBoolean("remember", true);
            }

            writer.WriteString("requestId", requestId);

            if (text is null)
            {
                writer.WriteNull("text");
            }
            else
            {
                writer.WriteString("text", text);
            }

            writer.WriteEndObject();
        }

        // Lowercase by contract (AuditChainHasher's form); ToHexStringLower is not on net8.0.
        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant();
    }

    private static void WriteData(Utf8JsonWriter writer, string? json)
    {
        if (json is null)
        {
            writer.WriteNullValue();

            return;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Not JSON (a caller of the runner can pass any text): the text itself is the
            // answer, written as a string so it cannot be mistaken for a parsed value.
            writer.WriteStringValue(json);

            return;
        }

        using (document)
        {
            WriteCanonical(writer, document.RootElement);
        }
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in element.EnumerateObject().OrderBy(static p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }
}
