using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPrism.Testing.Contracts.Tools;
using Microsoft.Extensions.AI;

namespace AgentPrism.Core.UnitTests.Tools;

/// <summary>
/// A representative, real <see cref="AIFunctionFactory"/>-produced schema
/// shared by <see cref="ToolArgumentValidationContractTests"/> and
/// <see cref="ToolContractSelfProofTests"/>: one required string, one bounded
/// integer, one pattern-constrained optional string.
/// </summary>
internal static class ValidatableSearchTool
{
    public static AIFunction Instance { get; } = AIFunctionFactory.Create((Func<string, int, string?, string>)Search, "contract_search", "Searches things.");

    private static string Search(
        [Description("query text")] string query,
        [Range(1, 100)] int limit = 10,
        [RegularExpression("^[a-z]+$")] string? tag = null)
        => query;
}

/// <summary>
/// A hand-rolled but genuinely correct <see cref="IToolArgumentsValidator"/>,
/// used as this contract's own proof that a validator implementing the JSON
/// Schema constraints <see cref="ValidatableSearchTool"/> declares makes
/// every <see cref="ToolArgumentValidationContract"/> scenario pass.
/// </summary>
internal sealed class ReferenceToolArgumentsValidator : IToolArgumentsValidator
{
    public ValueTask<ToolArgumentsValidationResult> ValidateAsync(
        ToolDescriptor tool, AIFunctionArguments arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var document = JsonDocument.Parse(tool.JsonSchema ?? "{}");
        var schema = document.RootElement;

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray())
            {
                if (!arguments.Keys.Contains(name.GetString()!, StringComparer.Ordinal))
                {
                    return new ValueTask<ToolArgumentsValidationResult>(
                        ToolArgumentsValidationResult.Invalid($"Missing required argument '{name.GetString()}'."));
                }
            }
        }

        if (!schema.TryGetProperty("properties", out var properties))
        {
            return new ValueTask<ToolArgumentsValidationResult>(ToolArgumentsValidationResult.Valid);
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in properties.EnumerateObject())
        {
            declared.Add(property.Name);

            if (!arguments.TryGetValue(property.Name, out var raw) || raw is not JsonElement value)
            {
                continue;
            }

            if (!MatchesDeclaredType(value, property.Value))
            {
                return new ValueTask<ToolArgumentsValidationResult>(
                    ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' has the wrong type."));
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                var number = value.GetDouble();

                if (property.Value.TryGetProperty("minimum", out var minimum) && number < minimum.GetDouble())
                {
                    return new ValueTask<ToolArgumentsValidationResult>(
                        ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' is below the minimum."));
                }

                if (property.Value.TryGetProperty("maximum", out var maximum) && number > maximum.GetDouble())
                {
                    return new ValueTask<ToolArgumentsValidationResult>(
                        ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' is above the maximum."));
                }
            }

            if (value.ValueKind == JsonValueKind.String
                && property.Value.TryGetProperty("pattern", out var pattern)
                && !Regex.IsMatch(value.GetString() ?? string.Empty, pattern.GetString()!, RegexOptions.None, TimeSpan.FromMilliseconds(200)))
            {
                return new ValueTask<ToolArgumentsValidationResult>(
                    ToolArgumentsValidationResult.Invalid($"Argument '{property.Name}' does not match the required pattern."));
            }
        }

        foreach (var key in arguments.Keys)
        {
            if (!declared.Contains(key))
            {
                return new ValueTask<ToolArgumentsValidationResult>(ToolArgumentsValidationResult.Invalid($"Unknown argument '{key}'."));
            }
        }

        return new ValueTask<ToolArgumentsValidationResult>(ToolArgumentsValidationResult.Valid);
    }

    private static bool MatchesDeclaredType(JsonElement value, JsonElement propertySchema)
    {
        if (!propertySchema.TryGetProperty("type", out var typeElement))
        {
            return true;
        }

        var declaredTypes = typeElement.ValueKind == JsonValueKind.Array
            ? typeElement.EnumerateArray().Select(static entry => entry.GetString())
            : [typeElement.GetString()];

        return declaredTypes.Any(type => type switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "number" => value.ValueKind == JsonValueKind.Number,
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "null" => value.ValueKind == JsonValueKind.Null,
            _ => true,
        });
    }
}

public sealed class ToolArgumentValidationContractTests : ToolArgumentValidationContract
{
    protected override ValueTask<AIFunction> CreateToolAsync() => new(ValidatableSearchTool.Instance);

    protected override ValueTask<IToolArgumentsValidator> CreateValidatorAsync() => new(new ReferenceToolArgumentsValidator());
}
