using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>Produces the canonical, inspectable text form of a tool result.</summary>
internal static class ToolResultText
{
    internal const string UnsupportedResultText = "{\"error\":\"tool_result_unsupported\"}";

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
            case AIContent: text = null; return false;
            default: text = null; return false;
        }
    }
}
