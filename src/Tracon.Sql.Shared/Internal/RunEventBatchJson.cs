using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Writes a group of run events as the one JSON array that <see cref="SqlQueriesBase.InsertRunEvents"/> opens.
/// </summary>
/// <remarks>
/// <para>
/// A group travels as one parameter, so a statement of two events and one of two thousand are the same text with the same
/// plan, and no group meets a limit on the number of parameters. Each object carries the columns of one row of
/// <c>run_events</c> except <c>run_id</c>, which is a parameter of its own: a group is the events of one run.
/// </para>
/// <para>
/// The text columns are written as the single-event path binds them: a protected column goes through
/// <see cref="ProtectedValue.Write"/>, so a group is stored with the same protection as an event on its own. A U+0000 in
/// a text is not handled here: it becomes the escape <c>\u0000</c>, and the dialect that binds the array replaces it in
/// the JSON text (<see cref="SqlDialect.AddJsonb"/>).
/// </para>
/// </remarks>
internal static class RunEventBatchJson
{
    /// <summary>The text form every store keeps an instant in: UTC, seven fractional digits.</summary>
    private const string InstantFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    private const char Replacement = '�';

    // Relaxed: the escaper's default would write every character above U+007F as an escape and make the document
    // several times longer for a text in another language. Every character it leaves alone is valid in a JSON string.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = true,
    };

    /// <summary>Writes the JSON array of a group of events.</summary>
    /// <param name="context">The store context, for the protected columns.</param>
    /// <param name="events">The events, in the order they are to be written.</param>
    /// <returns>The JSON array.</returns>
    public static string Write(SqlStoreContext context, IReadOnlyList<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(events);

        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, events.Count * 160));

        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            writer.WriteStartArray();

            for (var i = 0; i < events.Count; i++)
            {
                var runEvent = events[i];

                writer.WriteStartObject();
                writer.WriteNumber("seq", runEvent.Sequence);
                writer.WriteNumber("type", (short)runEvent.Type);
                WriteText(writer, "text", ProtectedValue.Write(context, ProtectedColumn.RunEventText, runEvent.Text));
                WriteText(writer, "tool_name", runEvent.ToolName);
                WriteText(writer, "tool_call_id", runEvent.ToolCallId);
                WriteText(writer, "payload", ProtectedValue.Write(context, ProtectedColumn.RunEventPayload, runEvent.Payload));
                WriteText(writer, "custom_type", runEvent.CustomType);
                writer.WriteString("created_at", runEvent.Timestamp.ToUniversalTime().ToString(InstantFormat, CultureInfo.InvariantCulture));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteText(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);

            return;
        }

        writer.WriteString(name, WellFormed(value));
    }

    /// <summary>
    /// Gives a text that holds half of a surrogate pair the form a store can hold: the half becomes U+FFFD.
    /// </summary>
    /// <remarks>
    /// <see cref="Utf8JsonWriter"/> refuses a text that is not valid UTF-16, and a group is one document: one such text
    /// would otherwise cost every event beside it. The writer of a run cuts text without splitting a pair, so the text
    /// that reaches here with a lone half came from the model or a tool whole.
    /// </remarks>
    private static string WellFormed(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var current = value[i];

            if (char.IsHighSurrogate(current) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;

                continue;
            }

            if (char.IsSurrogate(current))
            {
                return Repair(value, i);
            }
        }

        return value;
    }

    private static string Repair(string value, int firstLone)
    {
        var builder = new StringBuilder(value.Length);
        builder.Append(value, 0, firstLone);

        for (var i = firstLone; i < value.Length; i++)
        {
            var current = value[i];

            if (char.IsHighSurrogate(current) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                builder.Append(current).Append(value[i + 1]);
                i++;
            }
            else
            {
                builder.Append(char.IsSurrogate(current) ? Replacement : current);
            }
        }

        return builder.ToString();
    }
}
