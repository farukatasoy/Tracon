using System.Globalization;
using System.Text.Json;

namespace Tracon;

/// <summary>The tool call a code-defined approval policy judges. Read-only.</summary>
/// <remarks>
/// Registered with <c>ITraconBuilder.AddToolApprovalPolicy(toolName, policy)</c>
/// (<c>Tracon.Core</c>). A policy runs <strong>before</strong> the data rules
/// (<see cref="ToolApprovalRule"/>) and its decision, when not
/// <see cref="ToolApprovalPolicyDecision.Undecided"/>, overrides them — a policy is a
/// security boundary written in code, and data cannot loosen it.
/// </remarks>
public sealed class ToolApprovalContext
{
    /// <summary>Gets the tenant making the call.</summary>
    public required string TenantId { get; init; }

    /// <summary>Gets the tool being called.</summary>
    public required string ToolName { get; init; }

    /// <summary>Gets the agent making the call, when known.</summary>
    public string? AgentName { get; init; }

    /// <summary>Gets the call's arguments, keyed by parameter name.</summary>
    public required IReadOnlyDictionary<string, object?> Arguments { get; init; }

    /// <summary>
    /// Reads a number at a dotted path into <see cref="Arguments"/> (for example
    /// <c>order.amount</c>). Returns <see langword="null"/> if the path does not
    /// resolve or the value is not a number.
    /// </summary>
    /// <param name="path">The dotted path. No array index is accepted.</param>
    public double? GetNumber(string path)
    {
        if (!TryResolve(path, out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
            byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal
                => Convert.ToDouble(value, CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>
    /// Reads text at a dotted path into <see cref="Arguments"/> (for example
    /// <c>customer.tier</c>). Returns <see langword="null"/> if the path does not
    /// resolve or the value is not text.
    /// </summary>
    /// <param name="path">The dotted path. No array index is accepted.</param>
    public string? GetString(string path)
    {
        if (!TryResolve(path, out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            string text => text,
            _ => null,
        };
    }

    /// <summary>Walks a dotted path over <see cref="Arguments"/>, one segment at a time.</summary>
    /// <remarks>
    /// A leaf can be a <see cref="JsonElement"/> (the shape Microsoft Agent
    /// Framework's real function-call parsing produces) or a nested dictionary
    /// (the shape test doubles construct by hand); both are walked the same way.
    /// </remarks>
    private bool TryResolve(string path, out object? value)
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

        object? current = Arguments;

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
}
