using System.Buffers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AgentPrism.Testing.Contracts.Tools;

/// <summary>
/// The outcome of one <see cref="SchemaArgumentGenerator"/> call: either a
/// generated argument set, or a reason this particular mutation does not
/// apply to the schema (it declares no required property, no numeric bound,
/// no pattern — or the value needed is a shape this generator does not model,
/// such as a nested object).
/// </summary>
internal readonly struct GeneratedArguments
{
    private GeneratedArguments(AIFunctionArguments? arguments, string? skipReason)
    {
        Arguments = arguments;
        SkipReason = skipReason;
    }

    /// <summary>The generated arguments, or <see langword="null"/> when skipped.</summary>
    public AIFunctionArguments? Arguments { get; }

    /// <summary>The reason this mutation was skipped, or <see langword="null"/> when it was not.</summary>
    public string? SkipReason { get; }

    /// <summary>Whether this mutation does not apply to the schema.</summary>
    public bool IsSkipped => Arguments is null;

    public static GeneratedArguments Of(AIFunctionArguments arguments) => new(arguments, null);

    public static GeneratedArguments Skipped(string reason) => new(null, reason);
}

/// <summary>
/// Reads a tool's <see cref="AIFunctionDeclaration.JsonSchema"/> and produces
/// argument sets that violate one specific constraint at a time — a missing
/// required field, a type mismatch, an out-of-range number, a pattern
/// violation, an unexpected extra property — plus the fully valid baseline
/// each mutation starts from.
/// </summary>
/// <remarks>
/// Seeded (143.2): every choice this generator makes — which property to
/// mutate when several qualify, the exact string or number it produces —
/// comes from a <see cref="Random"/> built from the given seed, so the same
/// seed reproduces the exact same argument set on every run. Supports flat
/// <c>string</c>/<c>integer</c>/<c>number</c>/<c>boolean</c>/<c>enum</c>
/// properties; a required property of any other shape (nested object, array,
/// <c>oneOf</c>/<c>anyOf</c>/<c>$ref</c>) makes the whole baseline — and
/// every mutation built from it — skip with an explicit reason instead of
/// silently producing nothing.
/// </remarks>
internal sealed class SchemaArgumentGenerator
{
    private const string ExtraPropertyName = "__contract_unexpected_property__";

    private static readonly string[] PatternProbes = ["value", "abc123", "sample-value", "TEST", "   ", "", "1234567890"];

    private readonly Random _random;

    public SchemaArgumentGenerator(int seed) => _random = new Random(seed);

    /// <summary>A fully valid argument set for <paramref name="schema"/>.</summary>
    public GeneratedArguments Baseline(JsonElement schema)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in ReadProperties(schema))
        {
            var value = TryGenerateValue(property.Schema);

            if (value.HasValue)
            {
                arguments[property.Name] = value.Value;
            }
            else if (property.IsRequired)
            {
                return GeneratedArguments.Skipped(
                    $"cannot synthesize a value for required property '{property.Name}' — its schema shape " +
                    "(a nested object, an array, or a JSON Schema keyword this generator does not model) is unsupported.");
            }
        }

        return GeneratedArguments.Of(new AIFunctionArguments(arguments, StringComparer.Ordinal));
    }

    /// <summary>The baseline with one required property removed.</summary>
    public GeneratedArguments MissingRequiredProperty(JsonElement schema)
    {
        var baseline = Baseline(schema);
        if (baseline.IsSkipped)
        {
            return baseline;
        }

        var required = ReadProperties(schema).Where(static property => property.IsRequired).ToList();
        if (required.Count == 0)
        {
            return GeneratedArguments.Skipped("schema declares no required property.");
        }

        var target = required[_random.Next(required.Count)];
        var mutated = new Dictionary<string, object?>(baseline.Arguments!, StringComparer.Ordinal);
        mutated.Remove(target.Name);

        return GeneratedArguments.Of(new AIFunctionArguments(mutated, StringComparer.Ordinal));
    }

    /// <summary>The baseline with one property's value replaced by an incompatible JSON kind.</summary>
    public GeneratedArguments TypeMismatch(JsonElement schema)
    {
        var baseline = Baseline(schema);
        if (baseline.IsSkipped)
        {
            return baseline;
        }

        var candidates = ReadProperties(schema).Where(property => baseline.Arguments!.ContainsKey(property.Name)).ToList();
        if (candidates.Count == 0)
        {
            return GeneratedArguments.Skipped("schema declares no property to corrupt.");
        }

        var target = candidates[_random.Next(candidates.Count)];
        var mutated = new Dictionary<string, object?>(baseline.Arguments!, StringComparer.Ordinal)
        {
            [target.Name] = MismatchedValue(target.Schema),
        };

        return GeneratedArguments.Of(new AIFunctionArguments(mutated, StringComparer.Ordinal));
    }

    /// <summary>The baseline with a numeric property pushed one step past its declared bound.</summary>
    public GeneratedArguments OutOfRangeNumber(JsonElement schema)
    {
        var baseline = Baseline(schema);
        if (baseline.IsSkipped)
        {
            return baseline;
        }

        var candidates = ReadProperties(schema)
            .Where(static property => IsNumeric(property.Schema)
                                       && (property.Schema.TryGetProperty("minimum", out _) || property.Schema.TryGetProperty("maximum", out _)))
            .ToList();

        if (candidates.Count == 0)
        {
            return GeneratedArguments.Skipped("schema declares no numeric property with a minimum or maximum.");
        }

        var target = candidates[_random.Next(candidates.Count)];
        var mutated = new Dictionary<string, object?>(baseline.Arguments!, StringComparer.Ordinal)
        {
            [target.Name] = OutOfRangeValue(target.Schema),
        };

        return GeneratedArguments.Of(new AIFunctionArguments(mutated, StringComparer.Ordinal));
    }

    /// <summary>The baseline with a pattern-constrained string replaced by a non-matching value.</summary>
    public GeneratedArguments PatternViolation(JsonElement schema)
    {
        var baseline = Baseline(schema);
        if (baseline.IsSkipped)
        {
            return baseline;
        }

        var candidates = ReadProperties(schema)
            .Where(static property => string.Equals(PrimaryType(property.Schema), "string", StringComparison.Ordinal)
                                       && property.Schema.TryGetProperty("pattern", out _))
            .ToList();

        if (candidates.Count == 0)
        {
            return GeneratedArguments.Skipped("schema declares no string property with a pattern.");
        }

        var target = candidates[_random.Next(candidates.Count)];
        var pattern = target.Schema.GetProperty("pattern").GetString()!;
        var violatingValue = FindNonMatch(pattern);

        if (violatingValue is null)
        {
            return GeneratedArguments.Skipped($"could not synthesize a value that violates pattern '{pattern}'.");
        }

        var mutated = new Dictionary<string, object?>(baseline.Arguments!, StringComparer.Ordinal)
        {
            [target.Name] = StringElement(violatingValue),
        };

        return GeneratedArguments.Of(new AIFunctionArguments(mutated, StringComparer.Ordinal));
    }

    /// <summary>The baseline plus one property the schema never declared.</summary>
    public GeneratedArguments ExtraProperty(JsonElement schema)
    {
        var baseline = Baseline(schema);
        if (baseline.IsSkipped)
        {
            return baseline;
        }

        var mutated = new Dictionary<string, object?>(baseline.Arguments!, StringComparer.Ordinal)
        {
            [ExtraPropertyName] = StringElement("unexpected-value"),
        };

        return GeneratedArguments.Of(new AIFunctionArguments(mutated, StringComparer.Ordinal));
    }

    /// <summary>
    /// The mutation most likely to make a naively-written validator's own
    /// internal code throw rather than gracefully return
    /// <see cref="ToolArgumentsValidationResult.Invalid"/>: a pattern
    /// violation, then a type mismatch, then a missing required field — and,
    /// since <see cref="ExtraProperty"/> never skips for a readable object
    /// schema, always something.
    /// </summary>
    public GeneratedArguments Poison(JsonElement schema)
    {
        foreach (var attempt in new Func<JsonElement, GeneratedArguments>[] { PatternViolation, TypeMismatch, MissingRequiredProperty })
        {
            var result = attempt(schema);
            if (!result.IsSkipped)
            {
                return result;
            }
        }

        return ExtraProperty(schema);
    }

    private static List<(string Name, JsonElement Schema, bool IsRequired)> ReadProperties(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"The tool's JsonSchema is not readable: expected a JSON object at the root, found {schema.ValueKind}.");
        }

        var required = new HashSet<string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in requiredElement.EnumerateArray())
            {
                if (name.ValueKind == JsonValueKind.String)
                {
                    required.Add(name.GetString()!);
                }
            }
        }

        var result = new List<(string, JsonElement, bool)>();
        if (schema.TryGetProperty("properties", out var propertiesElement) && propertiesElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in propertiesElement.EnumerateObject())
            {
                result.Add((property.Name, property.Value, required.Contains(property.Name)));
            }
        }

        return result;
    }

    private JsonElement? TryGenerateValue(JsonElement propertySchema)
    {
        if (propertySchema.TryGetProperty("enum", out var enumElement) && enumElement.ValueKind == JsonValueKind.Array && enumElement.GetArrayLength() > 0)
        {
            var options = enumElement.EnumerateArray().ToList();
            return options[_random.Next(options.Count)];
        }

        return PrimaryType(propertySchema) switch
        {
            "string" => StringElement(NextString(propertySchema)),
            "integer" => IntegerElement(NextInteger(propertySchema)),
            "number" => NumberElement(NextNumber(propertySchema)),
            "boolean" => BooleanElement(_random.Next(2) == 0),
            _ => null, // array, object, null, missing/other keyword — not modeled (K-615)
        };
    }

    private string NextString(JsonElement propertySchema)
    {
        if (propertySchema.TryGetProperty("pattern", out var patternElement) && patternElement.ValueKind == JsonValueKind.String)
        {
            return FindMatch(patternElement.GetString()!) ?? "value";
        }

        var minLength = propertySchema.TryGetProperty("minLength", out var min) && min.TryGetInt32(out var minValue) ? minValue : 0;
        var length = Math.Max(minLength, 4 + _random.Next(4));

        return new string([.. Enumerable.Range(0, length).Select(_ => (char)('a' + _random.Next(26)))]);
    }

    private long NextInteger(JsonElement propertySchema)
    {
        var (min, max) = NumericBounds(propertySchema, -1000, 1000);
        return (long)Math.Floor(min + (_random.NextDouble() * (max - min)));
    }

    private double NextNumber(JsonElement propertySchema)
    {
        var (min, max) = NumericBounds(propertySchema, -1000, 1000);
        return min + (_random.NextDouble() * (max - min));
    }

    private static (double Min, double Max) NumericBounds(JsonElement propertySchema, double defaultMin, double defaultMax)
    {
        var min = propertySchema.TryGetProperty("minimum", out var minElement) && minElement.TryGetDouble(out var minValue) ? minValue : defaultMin;
        var max = propertySchema.TryGetProperty("maximum", out var maxElement) && maxElement.TryGetDouble(out var maxValue) ? maxValue : defaultMax;

        return min <= max ? (min, max) : (max, min);
    }

    private static bool IsNumeric(JsonElement propertySchema)
        => PrimaryType(propertySchema) is "integer" or "number";

    private static JsonElement OutOfRangeValue(JsonElement propertySchema)
    {
        var isInteger = string.Equals(PrimaryType(propertySchema), "integer", StringComparison.Ordinal);

        if (propertySchema.TryGetProperty("maximum", out var maxElement) && maxElement.TryGetDouble(out var max))
        {
            return isInteger ? IntegerElement((long)max + 1) : NumberElement(max + 1);
        }

        var min = propertySchema.GetProperty("minimum").GetDouble();
        return isInteger ? IntegerElement((long)min - 1) : NumberElement(min - 1);
    }

    private static JsonElement MismatchedValue(JsonElement propertySchema)
        => PrimaryType(propertySchema) switch
        {
            "string" => IntegerElement(12345),
            "integer" or "number" => StringElement("not-a-number"),
            "boolean" => StringElement("not-a-boolean"),
            _ => IntegerElement(12345),
        };

    /// <summary>
    /// The schema's primary <c>type</c> — a plain string, or the first
    /// non-<c>"null"</c> entry of a nullable-union array
    /// (<c>["string","null"]</c>, which <c>string?</c> and similar nullable
    /// parameters produce).
    /// </summary>
    private static string? PrimaryType(JsonElement propertySchema)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement))
        {
            return null;
        }

        if (typeElement.ValueKind == JsonValueKind.String)
        {
            return typeElement.GetString();
        }

        if (typeElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in typeElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.String && !string.Equals(entry.GetString(), "null", StringComparison.Ordinal))
                {
                    return entry.GetString();
                }
            }
        }

        return null;
    }

    private static string? FindMatch(string pattern)
    {
        foreach (var probe in PatternProbes)
        {
            if (SafeIsMatch(probe, pattern))
            {
                return probe;
            }
        }

        return null;
    }

    private static string? FindNonMatch(string pattern)
    {
        foreach (var probe in PatternProbes)
        {
            if (!SafeIsMatch(probe, pattern))
            {
                return probe;
            }
        }

        return null;
    }

    private static bool SafeIsMatch(string candidate, string pattern)
    {
        try
        {
            return Regex.IsMatch(candidate, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(200));
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    // Hand-written Utf8JsonWriter construction, not JsonSerializer.SerializeToElement:
    // the latter needs reflection-based (or source-generated) metadata for even a
    // primitive value's overload resolution, which trips IL2026/IL3050 in this
    // AOT-compatible package (AgentPrism.Abstractions ships AOT-clean; this test
    // package targets the same bar).
    private static JsonElement StringElement(string value) => WriteElement(writer => writer.WriteStringValue(value));

    private static JsonElement IntegerElement(long value) => WriteElement(writer => writer.WriteNumberValue(value));

    private static JsonElement NumberElement(double value) => WriteElement(writer => writer.WriteNumberValue(value));

    private static JsonElement BooleanElement(bool value) => WriteElement(writer => writer.WriteBooleanValue(value));

    private static JsonElement WriteElement(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            write(writer);
        }

        using var document = JsonDocument.Parse(buffer.WrittenMemory);
        return document.RootElement.Clone();
    }
}
