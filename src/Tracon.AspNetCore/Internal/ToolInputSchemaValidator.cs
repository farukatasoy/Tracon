using System.Globalization;
using System.Text.Json;

namespace Tracon;

/// <summary>
/// Checks the values a user entered against the flat schema an MCP server asked them for.
/// </summary>
/// <remarks>
/// <para>
/// MCP elicitation schemas are flat on purpose: an object whose properties are a string (with optional
/// length and format), a number or integer (with optional range), a boolean, a single choice (<c>enum</c>
/// or <c>oneOf</c>/<c>anyOf</c> of <c>const</c>) or a multiple choice (an array of those). The check is
/// there so a wrong value is a <c>400</c> the user can correct, not a tool call the server then fails.
/// A value the schema does not declare is refused, not dropped: a typo in a field name would otherwise
/// disappear and the tool would run without it.
/// </para>
/// <para>
/// A property whose declaration this check does not understand accepts any value of its declared type; the
/// server still decides what it will take. The schema comes from the session history, where Tracon wrote it
/// when the call was suspended, but it was sent by a server and is read as untrusted.
/// </para>
/// </remarks>
internal static class ToolInputSchemaValidator
{
    /// <summary>The largest one field value may be, in characters of its JSON text.</summary>
    public const int MaxFieldLength = 8 * 1024;

    /// <summary>The largest all field values together may be, in characters of their JSON text.</summary>
    public const int MaxTotalLength = 64 * 1024;

    /// <summary>Checks the values against the schema.</summary>
    /// <param name="schema">The <c>requestedSchema</c> the server sent.</param>
    /// <param name="content">The values the user entered; <see langword="null"/> for none.</param>
    /// <returns>The first problem found, or <see langword="null"/> when the values fit.</returns>
    public static string? Validate(JsonElement schema, IReadOnlyDictionary<string, JsonElement>? content)
    {
        var total = 0;

        if (content is not null)
        {
            foreach (var (name, value) in content)
            {
                var length = value.GetRawText().Length;

                if (length > MaxFieldLength)
                {
                    return $"The value of '{name}' is larger than {MaxFieldLength} characters.";
                }

                total += length;

                if (total > MaxTotalLength)
                {
                    return $"The values are larger than {MaxTotalLength} characters together.";
                }
            }
        }

        var properties = schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("properties", out var declared) &&
                         declared.ValueKind == JsonValueKind.Object
            ? declared
            : default;

        if (content is not null)
        {
            foreach (var name in content.Keys)
            {
                if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out _))
                {
                    return $"'{name}' is not a field of this request.";
                }
            }
        }

        if (schema.ValueKind == JsonValueKind.Object &&
            schema.TryGetProperty("required", out var required) &&
            required.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in required.EnumerateArray())
            {
                if (item.GetString() is { } name && (content is null || !content.ContainsKey(name)))
                {
                    return $"The field '{name}' is required.";
                }
            }
        }

        if (content is null || properties.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var (name, value) in content)
        {
            if (CheckField(properties.GetProperty(name), value) is { } problem)
            {
                return $"The value of '{name}' {problem}";
            }
        }

        return null;
    }

    private static string? CheckField(JsonElement declaration, JsonElement value)
    {
        var type = declaration.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        switch (type)
        {
            case "string":
                return CheckString(declaration, value);

            case "boolean":
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "must be true or false.";

            case "number":
            case "integer":
                return CheckNumber(declaration, value, integer: string.Equals(type, "integer", StringComparison.Ordinal));

            case "array":
                return CheckArray(declaration, value);

            default:
                return null;
        }
    }

    private static string? CheckString(JsonElement declaration, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return "must be text.";
        }

        var text = value.GetString()!;

        if (declaration.TryGetProperty("minLength", out var min) && min.TryGetInt32(out var minLength) && text.Length < minLength)
        {
            return $"must have at least {minLength} characters.";
        }

        if (declaration.TryGetProperty("maxLength", out var max) && max.TryGetInt32(out var maxLength) && text.Length > maxLength)
        {
            return $"must have at most {maxLength} characters.";
        }

        var options = Options(declaration);

        return options is not null && !options.Contains(text, StringComparer.Ordinal)
            ? $"must be one of: {string.Join(", ", options)}."
            : null;
    }

    private static string? CheckNumber(JsonElement declaration, JsonElement value, bool integer)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            return integer ? "must be a whole number." : "must be a number.";
        }

        if (integer && decimal.Truncate(number) != number)
        {
            return "must be a whole number.";
        }

        if (declaration.TryGetProperty("minimum", out var min) && min.TryGetDecimal(out var minimum) && number < minimum)
        {
            return $"must be at least {minimum.ToString(CultureInfo.InvariantCulture)}.";
        }

        if (declaration.TryGetProperty("maximum", out var max) && max.TryGetDecimal(out var maximum) && number > maximum)
        {
            return $"must be at most {maximum.ToString(CultureInfo.InvariantCulture)}.";
        }

        return null;
    }

    private static string? CheckArray(JsonElement declaration, JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return "must be a list.";
        }

        var count = value.GetArrayLength();

        if (declaration.TryGetProperty("minItems", out var min) && min.TryGetInt32(out var minItems) && count < minItems)
        {
            return $"must have at least {minItems} choices.";
        }

        if (declaration.TryGetProperty("maxItems", out var max) && max.TryGetInt32(out var maxItems) && count > maxItems)
        {
            return $"must have at most {maxItems} choices.";
        }

        var options = declaration.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object
            ? Options(items)
            : null;

        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.String)
            {
                return "must be a list of text.";
            }

            if (options is not null && !options.Contains(entry.GetString()!, StringComparer.Ordinal))
            {
                return $"must only hold: {string.Join(", ", options)}.";
            }
        }

        return null;
    }

    /// <summary>The values a single or multiple choice allows, or <see langword="null"/> when it allows any.</summary>
    private static List<string>? Options(JsonElement declaration)
    {
        if (declaration.TryGetProperty("enum", out var enumeration) && enumeration.ValueKind == JsonValueKind.Array)
        {
            return [.. enumeration.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String).Select(static item => item.GetString()!)];
        }

        foreach (var key in (ReadOnlySpan<string>)["oneOf", "anyOf"])
        {
            if (declaration.TryGetProperty(key, out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                return
                [
                    .. choices.EnumerateArray()
                        .Where(static choice => choice.ValueKind == JsonValueKind.Object &&
                                                choice.TryGetProperty("const", out var constant) &&
                                                constant.ValueKind == JsonValueKind.String)
                        .Select(static choice => choice.GetProperty("const").GetString()!),
                ];
            }
        }

        return null;
    }
}
