using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>Produces the canonical, inspectable text form of a tool result.</summary>
/// <remarks>
/// <para>
/// The text is the one a provider adapter actually sends: a
/// <see langword="string"/> travels as itself, and every other supported shape
/// is reduced to the exact characters that reach the wire. Everything that
/// reads a tool result — the output budget, the run event payload, the tool
/// invocation record, replay and the content guards — reads it through here,
/// so they all see the same bytes the model sees.
/// </para>
/// <para>
/// A result this type cannot normalize returns <see langword="false"/>, and
/// every caller fails closed on it rather than guessing: never
/// <see cref="object.ToString()"/>, which for an ordinary CLR object returns a
/// bare type name instead of the JSON a provider sends.
/// </para>
/// </remarks>
internal static class ToolResultText
{
    internal const string UnsupportedResultText = "{\"error\":\"tool_result_unsupported\"}";

    /// <summary>
    /// Whether the result is a protocol-level content result that a provider
    /// adapter renders as real content blocks.
    /// </summary>
    /// <remarks>
    /// Such a result keeps its runtime shape when it is let through untouched:
    /// <c>Tracon.Anthropic</c>'s adapter turns it into Anthropic content blocks
    /// and would lose an image if it were flattened to text for no reason. It
    /// is still measured like anything else — <see cref="TryGetText"/> reads it
    /// — so an over-budget one is replaced rather than passed on.
    /// <see cref="IReadOnlyList{T}"/>, not <see cref="IEnumerable{T}"/>: reading
    /// the text and then handing the same object on enumerates it twice, and a
    /// one-shot sequence would come back empty the second time.
    /// </remarks>
    internal static bool IsProtocolResult(object? result)
        => result is AIContent or IReadOnlyList<AIContent>;

    internal static bool TryGetText(object? result, out string? text)
    {
        switch (result)
        {
            case null: text = "null"; return true;
            case string value: text = value; return true;
            case JsonElement value: text = value.GetRawText(); return true;
            case bool value: text = value ? "true" : "false"; return true;
            case byte value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case sbyte value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case short value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case ushort value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case int value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case uint value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case long value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case ulong value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case float value: text = value.ToString("R", CultureInfo.InvariantCulture); return true;
            case double value: text = value.ToString("R", CultureInfo.InvariantCulture); return true;
            case decimal value: text = value.ToString(CultureInfo.InvariantCulture); return true;
            case Guid value: text = value.ToString("D", CultureInfo.InvariantCulture); return true;
            case DateTime value: text = value.ToString("O", CultureInfo.InvariantCulture); return true;
            case DateTimeOffset value: text = value.ToString("O", CultureInfo.InvariantCulture); return true;
            case Enum value: text = value.ToString(); return true;
            case AIContent or IReadOnlyList<AIContent>: return TryGetProtocolText(result, out text);
            default: text = null; return false;
        }
    }

    /// <summary>
    /// Reads the text of a protocol-level content result — an MCP tool's own
    /// return shape (<c>McpClientTool</c> answers with one
    /// <see cref="AIContent"/>, a list of them, or a <see cref="JsonElement"/>).
    /// </summary>
    /// <remarks>
    /// This is the serialization the provider adapters themselves perform on
    /// <see cref="FunctionResultContent.Result"/>, so the characters counted
    /// here are the characters the model is billed for. It stays AOT clean:
    /// <see cref="AIJsonUtilities.DefaultOptions"/> resolves every shipped
    /// <see cref="AIContent"/> type through a source-generated context, and the
    /// <see cref="System.Text.Json.Serialization.Metadata.JsonTypeInfo"/>
    /// overload asks for no reflection of its own (measured: zero IL2026/IL3050
    /// in <c>Tracon.Core</c>).
    /// </remarks>
    private static bool TryGetProtocolText(object result, out string? text)
    {
        try
        {
            text = JsonSerializer.Serialize(result, AIJsonUtilities.DefaultOptions.GetTypeInfo(typeof(object)));

            return true;
        }
        catch (NotSupportedException)
        {
            // A content type outside the shipped set, carrying a payload no
            // resolver can describe. Fail closed like any other unreadable
            // result rather than let an unmeasured, uninspected one through.
            text = null;

            return false;
        }
    }
}
