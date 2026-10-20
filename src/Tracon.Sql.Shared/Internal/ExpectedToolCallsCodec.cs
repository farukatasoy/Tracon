using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Serializes the tool calls a case expects to and from the JSON text of
/// <c>eval_cases.expected_tool_calls</c> - <c>jsonb</c> in PostgreSQL, plain text
/// in SQL Server and SQLite, the convention <c>eval_cases.parameters</c> uses.
/// </summary>
/// <remarks>
/// <para>
/// The text is an array of <c>{"name": ..., "arguments": {...}}</c> objects;
/// <c>arguments</c> is left out when the call checks only that the tool was
/// called. Hand-written rather than source-generated, for the reason
/// <see cref="JsonStringMapCodec"/> gives: a flat shape, no reflection.
/// </para>
/// <para>
/// A row written before the column existed has only <c>expected_tools</c>, a
/// comma separated list of names that no check ever read. <see cref="Deserialize"/>
/// turns it into calls without arguments, so a case is never read as expecting
/// nothing because of when it was written.
/// </para>
/// </remarks>
internal static class ExpectedToolCallsCodec
{
    /// <summary>Serializes calls to JSON text.</summary>
    /// <param name="calls">The calls. <see langword="null"/> or empty produces <see langword="null"/>.</param>
    public static string? Serialize(IReadOnlyList<EvalExpectedToolCall>? calls)
    {
        if (calls is null or { Count: 0 })
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();

            foreach (var call in calls)
            {
                writer.WriteStartObject();
                writer.WriteString("name", call.Name);

                if (call.Arguments is not null)
                {
                    writer.WriteStartObject("arguments");

                    foreach (var (name, value) in call.Arguments)
                    {
                        writer.WritePropertyName(name);

                        // A default JsonElement has no value to write and throws;
                        // null is the closest meaning it can have.
                        if (value.ValueKind == JsonValueKind.Undefined)
                        {
                            writer.WriteNullValue();
                        }
                        else
                        {
                            value.WriteTo(writer);
                        }
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Reads the calls of a case row.</summary>
    /// <param name="json">The <c>expected_tool_calls</c> column.</param>
    /// <param name="legacyNames">The <c>expected_tools</c> column of a row written before it existed.</param>
    /// <returns>The calls; an empty list when the row expects none.</returns>
    /// <remarks>
    /// The new column wins when it holds readable JSON. Unreadable text falls
    /// back to the legacy column instead of throwing: one damaged cell must not
    /// fail the listing of a suite's cases. An entry that is not an object with a
    /// non-empty <c>name</c> is skipped; an <c>arguments</c> that is not an object
    /// is read as no arguments.
    /// </remarks>
    public static IReadOnlyList<EvalExpectedToolCall> Deserialize(string? json, string? legacyNames)
    {
        if (json is { Length: > 0 } && TryParse(json, out var calls))
        {
            return calls;
        }

        return ReadLegacyNames(legacyNames);
    }

    private static bool TryParse(string json, out List<EvalExpectedToolCall> calls)
    {
        calls = [];

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("name", out var name) ||
                    name.ValueKind != JsonValueKind.String ||
                    name.GetString() is not { Length: > 0 } toolName)
                {
                    continue;
                }

                calls.Add(new EvalExpectedToolCall { Name = toolName, Arguments = ReadArguments(entry) });
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, JsonElement>? ReadArguments(JsonElement entry)
    {
        if (!entry.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // Clone: a JsonElement taken from a disposed JsonDocument is invalid, and
        // an invalid one makes the whole list that carries the case fail to serialize.
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var property in arguments.EnumerateObject())
        {
            result[property.Name] = property.Value.Clone();
        }

        return result;
    }

    private static List<EvalExpectedToolCall> ReadLegacyNames(string? names)
        => names is { Length: > 0 }
            ? [.. names
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static name => new EvalExpectedToolCall { Name = name })]
            : [];
}
