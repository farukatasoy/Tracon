using System.Globalization;
using System.Text.Json;

namespace AgentPrism;

/// <summary>
/// The single place that evaluates a <see cref="ToolArgumentCondition"/> list against a
/// tool call's arguments.
/// </summary>
/// <remarks>
/// <para>
/// Fails closed. An unresolved path, a type mismatch between the argument and the
/// condition's value, or an operator that does not apply to the resolved value's
/// JSON kind all make the CONDITION not match — never an exception, never a silent
/// coercion. A rule whose conditions do not all match does not auto-approve the
/// call (<see cref="ToolApprovalRuleEvaluator"/> then asks the user).
/// </para>
/// <para>
/// Comparison never converts between JSON kinds: a text <c>"100"</c> does not equal
/// the number <c>100</c>. Silent coercion would let a caller dodge a numeric rule by
/// sending its value as text.
/// </para>
/// </remarks>
internal static class ToolArgumentConditionMatcher
{
    /// <summary>Evaluates whether every condition matches (<c>AND</c>). An empty list always matches.</summary>
    /// <param name="conditions">The rule's conditions.</param>
    /// <param name="arguments">The call's arguments.</param>
    public static bool Matches(IReadOnlyList<ToolArgumentCondition> conditions, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (var condition in conditions)
        {
            if (!MatchesOne(condition, arguments))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesOne(ToolArgumentCondition condition, IReadOnlyDictionary<string, object?> arguments)
    {
        if (!TryResolvePath(condition.Path, arguments, out var actual))
        {
            return false;
        }

        return condition.Operator switch
        {
            ToolArgumentOperator.Equals => CompareEquality(actual, condition.Value, negate: false),
            ToolArgumentOperator.NotEquals => CompareEquality(actual, condition.Value, negate: true),
            ToolArgumentOperator.GreaterThan => CompareNumber(actual, condition.Value, static (a, b) => a > b),
            ToolArgumentOperator.GreaterThanOrEqual => CompareNumber(actual, condition.Value, static (a, b) => a >= b),
            ToolArgumentOperator.LessThan => CompareNumber(actual, condition.Value, static (a, b) => a < b),
            ToolArgumentOperator.LessThanOrEqual => CompareNumber(actual, condition.Value, static (a, b) => a <= b),
            ToolArgumentOperator.In => MatchesList(actual, condition.Value, negate: false),
            ToolArgumentOperator.NotIn => MatchesList(actual, condition.Value, negate: true),
            _ => false,
        };
    }

    /// <summary>Walks a dotted path over the call's arguments, one segment at a time. No array index is accepted.</summary>
    /// <remarks>
    /// A leaf can be a <see cref="JsonElement"/> (the shape Microsoft Agent
    /// Framework's real function-call parsing produces) or a nested dictionary
    /// (the shape test doubles construct by hand); both are walked the same way.
    /// </remarks>
    private static bool TryResolvePath(string path, IReadOnlyDictionary<string, object?> arguments, out object? value)
    {
        value = null;

        if (string.IsNullOrEmpty(path) || path.Length > ToolArgumentConditionLimits.MaxPathLength)
        {
            return false;
        }

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length == 0 || segments.Length > ToolArgumentConditionLimits.MaxPathSegments)
        {
            return false;
        }

        object? current = arguments;

        foreach (var segment in segments)
        {
            switch (current)
            {
                case IReadOnlyDictionary<string, object?> dictionary:
                    if (!dictionary.TryGetValue(segment, out current))
                    {
                        return false;
                    }

                    break;

                case IDictionary<string, object?> mutableDictionary:
                    if (!mutableDictionary.TryGetValue(segment, out current))
                    {
                        return false;
                    }

                    break;

                case JsonElement { ValueKind: JsonValueKind.Object } element:
                    if (!element.TryGetProperty(segment, out var property))
                    {
                        return false;
                    }

                    current = property;
                    break;

                default:
                    return false;
            }
        }

        value = current;

        return true;
    }

    private static bool CompareEquality(object? actual, JsonElement expected, bool negate)
    {
        if (!TryGetScalar(actual, out var actualKind, out var actualString, out var actualNumber, out var actualBool))
        {
            return false;
        }

        if (!TryGetScalarFromJsonElement(expected, out var expectedKind, out var expectedString, out var expectedNumber, out var expectedBool))
        {
            return false;
        }

        if (actualKind != expectedKind)
        {
            return false;
        }

        var equal = actualKind switch
        {
            ScalarKind.String => string.Equals(actualString, expectedString, StringComparison.Ordinal),
            ScalarKind.Number => actualNumber.Equals(expectedNumber),
            ScalarKind.Boolean => actualBool == expectedBool,
            _ => false,
        };

        return negate ? !equal : equal;
    }

    private static bool CompareNumber(object? actual, JsonElement expected, Func<double, double, bool> compare)
    {
        if (!TryGetScalar(actual, out var actualKind, out _, out var actualNumber, out _) || actualKind != ScalarKind.Number)
        {
            return false;
        }

        if (!TryGetScalarFromJsonElement(expected, out var expectedKind, out _, out var expectedNumber, out _) || expectedKind != ScalarKind.Number)
        {
            return false;
        }

        return compare(actualNumber, expectedNumber);
    }

    private static bool MatchesList(object? actual, JsonElement expected, bool negate)
    {
        if (expected.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (!TryGetScalar(actual, out var actualKind, out var actualString, out var actualNumber, out var actualBool))
        {
            return false;
        }

        var count = 0;
        var isMember = false;

        foreach (var element in expected.EnumerateArray())
        {
            if (++count > ToolArgumentConditionLimits.MaxListLength)
            {
                return false;
            }

            if (!TryGetScalarFromJsonElement(element, out var elementKind, out var elementString, out var elementNumber, out var elementBool)
                || elementKind != actualKind)
            {
                continue;
            }

            var equal = actualKind switch
            {
                ScalarKind.String => string.Equals(actualString, elementString, StringComparison.Ordinal),
                ScalarKind.Number => actualNumber.Equals(elementNumber),
                ScalarKind.Boolean => actualBool == elementBool,
                _ => false,
            };

            if (equal)
            {
                isMember = true;
                break;
            }
        }

        return negate ? !isMember : isMember;
    }

    private enum ScalarKind
    {
        String,
        Number,
        Boolean,
    }

    /// <summary>
    /// Reads a runtime argument value as a scalar. Handles both the
    /// <see cref="JsonElement"/> shape real calls carry and the raw CLR primitives
    /// test doubles construct by hand.
    /// </summary>
    private static bool TryGetScalar(object? value, out ScalarKind kind, out string? stringValue, out double numberValue, out bool boolValue)
    {
        switch (value)
        {
            case JsonElement element:
                return TryGetScalarFromJsonElement(element, out kind, out stringValue, out numberValue, out boolValue);

            case string text:
                kind = ScalarKind.String;
                stringValue = text;
                numberValue = 0;
                boolValue = false;
                return true;

            case bool flag:
                kind = ScalarKind.Boolean;
                stringValue = null;
                numberValue = 0;
                boolValue = flag;
                return true;

            case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                kind = ScalarKind.Number;
                stringValue = null;
                numberValue = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                boolValue = false;
                return true;

            default:
                kind = default;
                stringValue = null;
                numberValue = 0;
                boolValue = false;
                return false;
        }
    }

    private static bool TryGetScalarFromJsonElement(JsonElement element, out ScalarKind kind, out string? stringValue, out double numberValue, out bool boolValue)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                kind = ScalarKind.String;
                stringValue = element.GetString();
                numberValue = 0;
                boolValue = false;
                return true;

            case JsonValueKind.Number:
                kind = ScalarKind.Number;
                stringValue = null;
                numberValue = element.GetDouble();
                boolValue = false;
                return true;

            case JsonValueKind.True or JsonValueKind.False:
                kind = ScalarKind.Boolean;
                stringValue = null;
                numberValue = 0;
                boolValue = element.ValueKind == JsonValueKind.True;
                return true;

            default:
                kind = default;
                stringValue = null;
                numberValue = 0;
                boolValue = false;
                return false;
        }
    }
}
