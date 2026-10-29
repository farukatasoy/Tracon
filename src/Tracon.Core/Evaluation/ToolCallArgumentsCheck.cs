using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The <c>toolCallArgsMatch</c> check: every tool call a case expects was made,
/// and carried the expected arguments.
/// </summary>
/// <remarks>
/// <para>
/// The meaning is that of MAF's <c>EvalChecks.ToolCallArgsMatch</c>: for each
/// expected call, a call to a tool of the same name (letter case does not
/// matter) is looked for; without expected arguments the call is enough, and
/// with them one such call must carry every listed argument while it may carry
/// others. The reason text has the same shape, so a result reads the same
/// whichever check produced it.
/// </para>
/// <para>
/// The difference is how a value is compared. MAF unwraps the called value to a
/// <see cref="string"/>, <see cref="double"/> or <see cref="bool"/> and
/// compares with <see cref="object.Equals(object)"/>, so an expected <c>2</c>
/// never equals the called <c>2</c> and an object argument is equal only when its
/// text is: key order and white space decide. This check compares JSON values
/// by meaning (<see cref="JsonValueEquals"/>).
/// </para>
/// <para>
/// An <see cref="EvalItem"/> cannot carry a <see cref="JsonElement"/> that
/// MAF's own check could also read, so the run attaches the case's exact
/// expectations to the item (<see cref="Attach"/>) and MAF's form is filled in
/// next to them. An item without the exact form - one a consumer built, or one a
/// factory copied - is read from MAF's form, where an object or array value arrives
/// as its JSON text. There the expected text is compared with the raw text of an
/// object or array the call carried, as MAF does: key order and white space decide.
/// </para>
/// </remarks>
internal static class ToolCallArgumentsCheck
{
    /// <summary>The check name in a result; the one MAF's check uses.</summary>
    internal const string CheckName = "tool_call_args_match";

    private const int MaxJsonDepth = 64;

    // The evaluator hands the SAME EvalItem instance to every check (MAF's
    // LocalEvaluator passes the item it was given), so the key survives until
    // the check runs, and the table lets the item go with the case.
    private static readonly ConditionalWeakTable<EvalItem, IReadOnlyList<EvalExpectedToolCall>> Exact = new();

    /// <summary>Creates the check.</summary>
    public static EvalCheck Create() => Evaluate;

    /// <summary>
    /// Gives <paramref name="item"/> the expected calls of its case, in the exact
    /// form this check reads and in the form MAF's own check reads.
    /// </summary>
    /// <param name="item">The item the case run produced.</param>
    /// <param name="expected">The calls the case expects.</param>
    public static void Attach(EvalItem item, IReadOnlyList<EvalExpectedToolCall> expected)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(expected);

        Exact.AddOrUpdate(item, expected);
        item.ExpectedToolCalls = [.. expected.Select(ToMafCall)];
    }

    /// <summary>Runs the check against one item.</summary>
    /// <param name="item">The item to judge.</param>
    public static EvalCheckResult Evaluate(EvalItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var hasExactForm = Exact.TryGetValue(item, out var exact);
        var expected = hasExactForm
            ? exact!
            : FromMafCalls(item.ExpectedToolCalls);

        if (expected.Count == 0)
        {
            return new EvalCheckResult(true, "No expected tool calls specified.", CheckName);
        }

        var called = CalledTools(item);
        var matched = 0;
        var lines = new List<string>(expected.Count);

        foreach (var wanted in expected)
        {
            var sameName = called
                .Where(call => string.Equals(call.Name, wanted.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sameName.Count == 0)
            {
                lines.Add($"  {wanted.Name}: not called");
            }
            else if (wanted.Arguments is null or { Count: 0 })
            {
                matched++;
                lines.Add($"  {wanted.Name}: called (args not checked)");
            }
            else if (sameName.Exists(call => CarriesAll(call.Arguments, wanted.Arguments, comparesStructuresAsText: !hasExactForm)))
            {
                matched++;
                lines.Add($"  {wanted.Name}: args match");
            }
            else
            {
                lines.Add($"  {wanted.Name}: args mismatch");
            }
        }

        var reason = string.Create(
            CultureInfo.InvariantCulture,
            $"Tool call args match: {matched}/{expected.Count}\n{string.Join("\n", lines)}");

        return new EvalCheckResult(matched == expected.Count, reason, CheckName);
    }

    /// <summary>Compares two JSON values by meaning.</summary>
    /// <remarks>
    /// A number equals a number of the same value whatever its spelling
    /// (<c>2</c> and <c>2.0</c>); an object equals an object with the same
    /// properties in any order; an array equals an array element by element.
    /// White space between tokens plays no part.
    /// </remarks>
    /// <param name="expected">The value a case expects.</param>
    /// <param name="actual">The value a call carried.</param>
    internal static bool JsonValueEquals(JsonElement expected, JsonElement actual)
    {
        var kind = expected.ValueKind;

        // `true` and `false` are two kinds; both sides still have to be the same one.
        if (kind != actual.ValueKind)
        {
            return false;
        }

        switch (kind)
        {
            case JsonValueKind.String:
                return string.Equals(expected.GetString(), actual.GetString(), StringComparison.Ordinal);

            case JsonValueKind.Number:
                return NumbersEqual(expected, actual);

            case JsonValueKind.Array:
                return ArraysEqual(expected, actual);

            case JsonValueKind.Object:
                return ObjectsEqual(expected, actual);

            default:
                // Null, true, false (and Undefined, which never leaves a reader).
                return true;
        }
    }

    private static bool NumbersEqual(JsonElement expected, JsonElement actual)
        => expected.TryGetDecimal(out var left) && actual.TryGetDecimal(out var right)
            ? left == right
            : expected.GetDouble().Equals(actual.GetDouble());

    private static bool ArraysEqual(JsonElement expected, JsonElement actual)
    {
        if (expected.GetArrayLength() != actual.GetArrayLength())
        {
            return false;
        }

        var index = 0;

        foreach (var item in expected.EnumerateArray())
        {
            if (!JsonValueEquals(item, actual[index++]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ObjectsEqual(JsonElement expected, JsonElement actual)
    {
        var expectedCount = 0;

        foreach (var property in expected.EnumerateObject())
        {
            expectedCount++;

            if (!actual.TryGetProperty(property.Name, out var other) || !JsonValueEquals(property.Value, other))
            {
                return false;
            }
        }

        return expectedCount == actual.EnumerateObject().Count();
    }

    private static bool CarriesAll(
        Dictionary<string, JsonElement>? carried,
        IReadOnlyDictionary<string, JsonElement> wanted,
        bool comparesStructuresAsText)
    {
        foreach (var (name, value) in wanted)
        {
            JsonElement actual = default;
            var present = carried is not null && carried.TryGetValue(name, out actual);

            // A null argument never reaches the check from a model that omits
            // it, and MAF drops one that is sent; an expected null therefore
            // means "absent or null".
            var matches = value.ValueKind == JsonValueKind.Null
                ? !present || actual.ValueKind == JsonValueKind.Null
                : present && ArgumentEquals(value, actual, comparesStructuresAsText);

            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    // MAF's form turns an expected object or array into its JSON text, so the string that arrives
    // here is the text of a structure while the call carries the structure itself.
    private static bool ArgumentEquals(JsonElement expected, JsonElement actual, bool comparesStructuresAsText)
        => comparesStructuresAsText
           && expected.ValueKind == JsonValueKind.String
           && actual.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            ? string.Equals(expected.GetString(), actual.GetRawText(), StringComparison.Ordinal)
            : JsonValueEquals(expected, actual);

    private static List<(string Name, Dictionary<string, JsonElement>? Arguments)> CalledTools(EvalItem item)
    {
        var called = new List<(string, Dictionary<string, JsonElement>?)>();

        foreach (var message in item.Conversation)
        {
            foreach (var content in message.Contents)
            {
                if (content is not FunctionCallContent call)
                {
                    continue;
                }

                Dictionary<string, JsonElement>? arguments = null;

                if (call.Arguments is not null)
                {
                    arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

                    foreach (var (name, value) in call.Arguments)
                    {
                        if (value is not null)
                        {
                            arguments[name] = ToElement(value);
                        }
                    }
                }

                called.Add((call.Name, arguments));
            }
        }

        return called;
    }

    // MAF's form: a text, a double, a bool, and the JSON text for anything else.
    private static ExpectedToolCall ToMafCall(EvalExpectedToolCall call)
    {
        if (call.Arguments is null)
        {
            return new ExpectedToolCall(call.Name, null!);
        }

        var arguments = new Dictionary<string, object>(call.Arguments.Count, StringComparer.Ordinal);

        foreach (var (name, value) in call.Arguments)
        {
            arguments[name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()!,
                JsonValueKind.Number => value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => value.ToString(),
            };
        }

        return new ExpectedToolCall(call.Name, arguments);
    }

    private static List<EvalExpectedToolCall> FromMafCalls(IReadOnlyList<ExpectedToolCall>? calls)
    {
        var result = new List<EvalExpectedToolCall>(calls?.Count ?? 0);

        if (calls is null)
        {
            return result;
        }

        foreach (var call in calls)
        {
            result.Add(new EvalExpectedToolCall
            {
                Name = call.Name,
                Arguments = call.Arguments?.ToDictionary(
                    static pair => pair.Key,
                    static pair => ToElement(pair.Value),
                    StringComparer.Ordinal),
            });
        }

        return result;
    }

    /// <summary>
    /// Turns a value a call carried into JSON without reflection: a
    /// <see cref="JsonElement"/> stays, a primitive keeps its type, a dictionary
    /// becomes an object, a sequence becomes an array, anything else becomes its
    /// text.
    /// </summary>
    private static JsonElement ToElement(object? value)
    {
        if (value is JsonElement { ValueKind: not JsonValueKind.Undefined } element)
        {
            return element.Clone();
        }

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteValue(writer, value, 0);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);

        return document.RootElement.Clone();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value, int depth)
    {
        if (depth > MaxJsonDepth)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonElement { ValueKind: JsonValueKind.Undefined }:
                writer.WriteNullValue();
                break;
            case JsonElement element:
                element.WriteTo(writer);
                break;
            case JsonNode node:
                node.WriteTo(writer);
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int or long or short or sbyte or byte or ushort or uint:
                writer.WriteNumberValue(Convert.ToInt64(value, CultureInfo.InvariantCulture));
                break;
            case ulong big:
                writer.WriteNumberValue(big);
                break;
            case decimal exact:
                writer.WriteNumberValue(exact);
                break;
            case double or float:
                var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);

                if (double.IsFinite(number))
                {
                    writer.WriteNumberValue(number);
                }
                else
                {
                    writer.WriteStringValue(number.ToString(CultureInfo.InvariantCulture));
                }

                break;
            case DateTime moment:
                writer.WriteStringValue(moment);
                break;
            case DateTimeOffset moment:
                writer.WriteStringValue(moment);
                break;
            case IEnumerable<KeyValuePair<string, object?>> pairs:
                writer.WriteStartObject();

                foreach (var (name, member) in pairs)
                {
                    writer.WritePropertyName(name);
                    WriteValue(writer, member, depth + 1);
                }

                writer.WriteEndObject();
                break;
            case IEnumerable items:
                writer.WriteStartArray();

                foreach (var item in items)
                {
                    WriteValue(writer, item, depth + 1);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
