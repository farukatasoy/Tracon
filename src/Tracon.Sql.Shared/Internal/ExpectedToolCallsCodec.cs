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
/// <para>
/// Text that is there and cannot be read never becomes "expects nothing":
/// the <c>toolCallArgsMatch</c> check passes a case with no expected call, so a
/// damaged cell read as an empty list would turn a case that asserts something
/// into one that asserts nothing, and the suite would stay green. It reads as an
/// entry named <see cref="UnreadableName"/>, which no run can call, so the case
/// fails and says which expectation it could not read; the listing still works.
/// </para>
/// </remarks>
internal static class ExpectedToolCallsCodec
{
    /// <summary>
    /// The name of the entry that stands for text of the column that could not be
    /// read. It is not a valid tool name, so no run calls it.
    /// </summary>
    internal const string UnreadableName = "<expected_tool_calls unreadable>";

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
    /// The new column wins whenever it holds text, even an empty list. Text that
    /// is not JSON, is not an array, or holds an entry that is not an object with
    /// a non-empty <c>name</c> and an <c>arguments</c> that is an object or
    /// <c>null</c> does not throw: the readable entries are returned and one
    /// <see cref="UnreadableName"/> entry is added, so the case fails instead of
    /// asserting less than it was written to assert. The legacy column is read
    /// only when the new one is empty.
    /// </remarks>
    public static IReadOnlyList<EvalExpectedToolCall> Deserialize(string? json, string? legacyNames)
        => json is { Length: > 0 } ? Parse(json) : ReadLegacyNames(legacyNames);

    private static List<EvalExpectedToolCall> Parse(string json)
    {
        var calls = new List<EvalExpectedToolCall>();
        var damaged = false;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                damaged = true;
            }
            else
            {
                foreach (var entry in document.RootElement.EnumerateArray())
                {
                    if (TryReadEntry(entry, out var call))
                    {
                        calls.Add(call);
                    }
                    else
                    {
                        damaged = true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            damaged = true;
        }

        if (damaged)
        {
            calls.Add(new EvalExpectedToolCall { Name = UnreadableName });
        }

        return calls;
    }

    private static bool TryReadEntry(JsonElement entry, out EvalExpectedToolCall call)
    {
        call = null!;

        if (entry.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty("name", out var name) ||
            name.ValueKind != JsonValueKind.String ||
            name.GetString() is not { Length: > 0 } toolName)
        {
            return false;
        }

        if (!entry.TryGetProperty("arguments", out var arguments) || arguments.ValueKind == JsonValueKind.Null)
        {
            call = new EvalExpectedToolCall { Name = toolName };
            return true;
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        call = new EvalExpectedToolCall { Name = toolName, Arguments = ReadArguments(arguments) };
        return true;
    }

    private static Dictionary<string, JsonElement> ReadArguments(JsonElement arguments)
    {
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
