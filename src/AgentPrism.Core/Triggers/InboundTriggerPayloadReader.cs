using System.Text.Json;

namespace AgentPrism;

/// <summary>Turns an inbound trigger's parsed request body into a run message.</summary>
/// <remarks>
/// <see cref="InboundTriggerPayloadMode.Path"/> walks a dotted path exactly
/// like <see cref="ToolArgumentConditionMatcher"/>'s condition path (phase
/// 63) — one segment at a time, object properties only, no array index — so
/// AgentPrism does not carry two different path languages. K2 (no template
/// language) still holds: there is no expression evaluation here, only
/// property selection.
/// </remarks>
internal static class InboundTriggerPayloadReader
{
    /// <summary>Extracts the run message from a trigger's parsed request body.</summary>
    /// <param name="body">The parsed JSON body.</param>
    /// <param name="mode">The extraction mode.</param>
    /// <param name="path">The dotted path; only read when <paramref name="mode"/> is <see cref="InboundTriggerPayloadMode.Path"/>.</param>
    /// <param name="message">The extracted message; empty when extraction fails.</param>
    /// <returns><see langword="true"/> if a message was extracted.</returns>
    public static bool TryExtractMessage(
        JsonElement body,
        InboundTriggerPayloadMode mode,
        string? path,
        out string message)
    {
        switch (mode)
        {
            case InboundTriggerPayloadMode.WholeBody:
                message = body.GetRawText();
                return true;

            case InboundTriggerPayloadMode.Path:
                return TryResolvePath(body, path, out message);

            default:
                message = string.Empty;
                return false;
        }
    }

    private static bool TryResolvePath(JsonElement root, string? path, out string message)
    {
        message = string.Empty;

        if (string.IsNullOrEmpty(path) || path.Length > ToolArgumentConditionLimits.MaxPathLength)
        {
            return false;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Length > ToolArgumentConditionLimits.MaxPathSegments)
        {
            return false;
        }

        var current = root;

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
            {
                return false;
            }

            current = next;
        }

        message = current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : current.GetRawText();

        return true;
    }
}
