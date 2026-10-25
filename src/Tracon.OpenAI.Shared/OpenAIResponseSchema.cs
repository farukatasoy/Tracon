using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The response schema as the OpenAI client of Microsoft.Extensions.AI sends it, and what differs from the
/// schema the author wrote.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Measured, not guessed</strong> (Microsoft.Extensions.AI.OpenAI 10.11.0, decompiled): the client
/// runs a response schema through its <c>StrictSchemaTransformCache</c> on <strong>every</strong> request, with
/// the strict flag on or off. The flag only decides the <c>strict</c> field of the wire format. The transform
/// is <c>internal</c> to the library, so <see cref="Strictness"/> is a copy: the same four options, and the
/// same 22 keywords moved into the schema's <c>description</c>.
/// </para>
/// <para>
/// A copy drifts when the library is upgraded. A request-level test sends real requests over a loopback socket and
/// compares this copy with the schema in the request body that the real client writes, on both API surfaces and in
/// both modes, so a change in the library turns that test red.
/// </para>
/// <para>
/// The lists are not part of the transform. They are read from the author's schema by a second walk that
/// changes nothing: the transform replaces <c>required</c> and removes the keywords, so its own pass can no
/// longer tell which property was optional.
/// </para>
/// </remarks>
internal static class OpenAIResponseSchema
{
    /// <summary>Keywords the client removes from the schema and writes into <c>description</c> as text.</summary>
    private static readonly string[] MovedKeywords =
    [
        "contentEncoding",
        "contentMediaType",
        "not",
        "minLength",
        "maxLength",
        "pattern",
        "format",
        "minimum",
        "maximum",
        "multipleOf",
        "patternProperties",
        "minItems",
        "maxItems",
        "unevaluatedProperties",
        "propertyNames",
        "minProperties",
        "maxProperties",
        "unevaluatedItems",
        "contains",
        "minContains",
        "maxContains",
        "uniqueItems",
    ];

    private static readonly string[] Alternatives = ["anyOf", "oneOf"];

    // Where a schema sits inside a schema, for the walk of the schema the client sends.
    private static readonly string[] SchemaMaps = ["properties", "$defs", "definitions"];

    private static readonly string[] SchemaLists = ["anyOf", "oneOf", "allOf"];

    private static readonly string[] SingleSchemas = ["items", "additionalProperties"];

    /// <summary>
    /// Keywords OpenAI answers with HTTP 400 (<c>'...' is not permitted</c>) when the response format is strict
    /// (measured 2026-10-25 against the live service, model gpt-5.4-mini). The same schema runs without them in
    /// the non-strict mode.
    /// </summary>
    private static readonly string[] RefusedInStrictMode =
    [
        "oneOf",
        "allOf",
        "if",
        "then",
        "dependentRequired",
        "dependentSchemas",
    ];

    private static readonly AIJsonSchemaTransformOptions Strictness = new()
    {
        DisallowAdditionalProperties = true,
        ConvertBooleanSchemas = true,
        MoveDefaultKeywordToDescription = true,
        RequireAllProperties = true,
        TransformSchemaNode = static (_, node) => MoveKeywordsToDescription(node),
    };

    /// <summary>Describes the schema the client sends for an author's schema.</summary>
    /// <param name="schema">The author's schema. A JSON object.</param>
    /// <param name="strict">Whether the binding asks the provider to enforce the schema.</param>
    /// <param name="measuredService">
    /// Whether the service behind the client is the one whose strict-mode refusals were measured (OpenAI itself).
    /// An OpenAI-compatible server or a gateway can accept what OpenAI refuses, so for those the refusal list
    /// stays empty instead of claiming a refusal nobody measured.
    /// </param>
    /// <returns>The effective schema and the differences.</returns>
    /// <exception cref="ArgumentException">The schema is not a valid schema document.</exception>
    public static ResponseSchemaShape Describe(JsonElement schema, bool strict, bool measuredService = false)
    {
        var optionalMadeRequired = new List<string>();
        var keywordsNotEnforced = new List<string>();

        // Measured: a pass with only a node callback sees the author's nodes, children first, with the
        // path of each. It returns every node as it got it, so nothing changes.
        _ = AIJsonUtilities.TransformSchema(
            schema,
            new AIJsonSchemaTransformOptions
            {
                TransformSchemaNode = (context, node) =>
                {
                    Observe(PointerOf(context), node, optionalMadeRequired, keywordsNotEnforced);
                    return node;
                },
            });

        optionalMadeRequired.Sort(StringComparer.Ordinal);
        keywordsNotEnforced.Sort(StringComparer.Ordinal);

        var effective = AIJsonUtilities.TransformSchema(schema, Strictness);
        var refused = new List<string>();

        if (measuredService)
        {
            FindRefusals("#", JsonNode.Parse(effective.GetRawText()), refused);
            refused.Sort(StringComparer.Ordinal);
        }

        return new ResponseSchemaShape
        {
            EffectiveSchema = effective,
            Strict = strict,
            OptionalMadeRequired = optionalMadeRequired,
            KeywordsNotEnforced = keywordsNotEnforced,
            StrictUnsupported = refused,
        };
    }

    /// <summary>
    /// Walks the schema the client sends and lists what OpenAI's strict mode refuses.
    /// </summary>
    /// <remarks>
    /// Walks every place a schema can sit, <c>$defs</c> included: the transform leaves those untouched, and the
    /// service reads them. An object schema is refused unless its <c>additionalProperties</c> is <c>false</c>
    /// (measured: a free-form <c>{"type":"object"}</c> is refused, an object with empty <c>properties</c> is not).
    /// </remarks>
    private static void FindRefusals(string pointer, JsonNode? node, List<string> refused)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        foreach (var keyword in RefusedInStrictMode)
        {
            if (schema.ContainsKey(keyword))
            {
                refused.Add($"{pointer}: {keyword}");
            }
        }

        if (IsObjectSchema(schema)
            && !(schema["additionalProperties"] is JsonValue closed && closed.TryGetValue<bool>(out var value) && !value))
        {
            refused.Add($"{pointer}: additionalProperties");
        }

        foreach (var container in SchemaMaps)
        {
            if (schema[container] is JsonObject map)
            {
                foreach (var entry in map)
                {
                    FindRefusals($"{pointer}/{container}/{Escape(entry.Key)}", entry.Value, refused);
                }
            }
        }

        foreach (var container in SchemaLists)
        {
            if (schema[container] is JsonArray list)
            {
                for (var index = 0; index < list.Count; index++)
                {
                    FindRefusals($"{pointer}/{container}/{index}", list[index], refused);
                }
            }
        }

        foreach (var child in SingleSchemas)
        {
            FindRefusals($"{pointer}/{child}", schema[child], refused);
        }
    }

    private static bool IsObjectSchema(JsonObject schema)
        => schema["type"] switch
        {
            JsonValue single => single.TryGetValue<string>(out var name) && string.Equals(name, "object", StringComparison.Ordinal),
            JsonArray names => names.Any(static name => name is JsonValue value
                && value.TryGetValue<string>(out var text)
                && string.Equals(text, "object", StringComparison.Ordinal)),
            _ => false,
        };

    private static void Observe(string pointer, JsonNode node, List<string> optionalMadeRequired, List<string> keywordsNotEnforced)
    {
        if (node is not JsonObject schema)
        {
            return;
        }

        foreach (var keyword in MovedKeywords)
        {
            // A keyword written as JSON null stays in the schema: the library skips it.
            if (schema[keyword] is not null)
            {
                keywordsNotEnforced.Add($"{pointer}: {keyword}");
            }
        }

        if (schema["properties"] is not JsonObject properties)
        {
            return;
        }

        var required = new HashSet<string>(StringComparer.Ordinal);

        if (schema["required"] is JsonArray list)
        {
            foreach (var item in list)
            {
                if (item is JsonValue value && value.TryGetValue<string>(out var name))
                {
                    required.Add(name);
                }
            }
        }

        foreach (var property in properties)
        {
            if (!required.Contains(property.Key) && !AcceptsNull(property.Value))
            {
                optionalMadeRequired.Add($"{pointer}/properties/{Escape(property.Key)}");
            }
        }
    }

    /// <summary>Tells whether a schema lets <c>null</c> through, so a forced property can still stay empty.</summary>
    /// <remarks>
    /// A schema that names no type, enum, constant or alternatives constrains nothing. A <c>$ref</c> is not
    /// followed: the walk cannot say what it points at, so the property is reported.
    /// </remarks>
    private static bool AcceptsNull(JsonNode? schema)
    {
        if (schema is JsonValue flag)
        {
            return flag.TryGetValue<bool>(out var allowed) && allowed;
        }

        if (schema is not JsonObject node)
        {
            return false;
        }

        if (node.ContainsKey("$ref"))
        {
            return false;
        }

        if (node.TryGetPropertyValue("const", out var constant))
        {
            return constant is null;
        }

        if (node["enum"] is JsonArray values)
        {
            return values.Any(static value => value is null);
        }

        if (node["type"] is { } type)
        {
            return type switch
            {
                JsonValue single => single.TryGetValue<string>(out var name) && string.Equals(name, "null", StringComparison.Ordinal),
                JsonArray names => names.Any(static name => name is JsonValue value
                    && value.TryGetValue<string>(out var text)
                    && string.Equals(text, "null", StringComparison.Ordinal)),
                _ => false,
            };
        }

        foreach (var alternatives in Alternatives)
        {
            if (node[alternatives] is JsonArray branches)
            {
                return branches.Any(AcceptsNull);
            }
        }

        return true;
    }

    private static string PointerOf(AIJsonSchemaTransformContext context)
    {
        var path = context.Path;

        if (path.Length == 0)
        {
            return "#";
        }

        var pointer = new StringBuilder("#");

        for (var index = 0; index < path.Length; index++)
        {
            var segment = path[index];

            // The library writes an array position as "[0]"; a pointer writes it as "0". A property whose
            // name looks the same follows a "properties" segment and keeps its name.
            var isPosition = segment.Length > 2
                && segment[0] == '['
                && segment[^1] == ']'
                && !(index > 0 && string.Equals(path[index - 1], "properties", StringComparison.Ordinal));

            pointer.Append('/').Append(Escape(isPosition ? segment[1..^1] : segment));
        }

        return pointer.ToString();
    }

    // RFC 6901: '~' and '/' are the two characters a pointer segment escapes.
    private static string Escape(string segment)
        => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    /// <summary>The library's node callback, copied statement for statement.</summary>
    private static JsonNode MoveKeywordsToDescription(JsonNode node)
    {
        if (node is not JsonObject jsonObject)
        {
            return node;
        }

        StringBuilder? moved = null;

        foreach (var keyword in MovedKeywords)
        {
            var value = jsonObject[keyword];

            if (value is null)
            {
                continue;
            }

            jsonObject.Remove(keyword);

            moved ??= new StringBuilder();

            if (moved.Length > 0)
            {
                moved.AppendLine();
            }

            moved.Append(keyword).Append(": ").Append(value);
        }

        if (moved is not null)
        {
            var description = jsonObject["description"];

            jsonObject["description"] = description is not null && description.GetValueKind() == JsonValueKind.String
                ? $"{description.GetValue<string>()}{Environment.NewLine}{moved}"
                : moved.ToString();
        }

        return node;
    }
}
