using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// Validation for the <c>CustomType</c> string that qualifies a
/// <see cref="RunEventType.Custom"/> event.
/// </summary>
/// <remarks>
/// Repeats the <see cref="JobHandlerKeys"/> pattern: a namespaced, lowercase
/// ASCII string with a reserved prefix AgentPrism itself never emits, so a
/// future built-in custom type can never collide with a consumer's own.
/// </remarks>
public static partial class RunEventCustomTypes
{
    /// <summary>The prefix reserved for AgentPrism's own use.</summary>
    /// <remarks>
    /// A consumer's <c>CustomType</c> starting with this prefix is rejected
    /// at write time — not because AgentPrism ships a built-in custom type
    /// today, but so one could be added later without ever colliding with a
    /// consumer's own.
    /// </remarks>
    public const string ReservedPrefix = "agentprism.";

    /// <summary>
    /// Checks whether <paramref name="customType"/> is a valid custom event
    /// type: 1-128 characters, lowercase ASCII letters, digits, <c>.</c>,
    /// <c>_</c>, or <c>-</c>, starting with a letter or digit.
    /// </summary>
    /// <param name="customType">The candidate type.</param>
    /// <returns><see langword="true"/> if the type is valid.</returns>
    /// <remarks>
    /// Uppercase letters are rejected, not normalized — the same reasoning as
    /// <see cref="JobHandlerKeys.IsValidKey"/>. A consumer that read back its
    /// own event compares the type ordinally, so allowing case variants would
    /// let <c>"Contoso.Preview"</c> and <c>"contoso.preview"</c> silently
    /// become two different types.
    /// </remarks>
    public static bool IsValidType(string? customType) => customType is not null && TypePattern().IsMatch(customType);

    /// <summary>
    /// Checks whether <paramref name="customType"/> falls inside the
    /// <see cref="ReservedPrefix"/> namespace.
    /// </summary>
    /// <param name="customType">The candidate type.</param>
    /// <returns><see langword="true"/> if the type is reserved for AgentPrism.</returns>
    public static bool IsReserved(string? customType)
        => customType is not null && customType.StartsWith(ReservedPrefix, StringComparison.Ordinal);

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TypePattern();
}
