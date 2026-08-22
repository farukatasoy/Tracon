using System.Text;
using System.Text.RegularExpressions;

namespace AgentPrism;

/// <summary>
/// Substitutes <c>{{name}}</c> placeholders in an instructions text with
/// concrete values.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is value substitution only, not a template engine.</strong>
/// There is no expression, condition, loop, or field access
/// (<c>{{a.b}}</c>) - deliberately: a template language is a security surface
/// once it is in the library, and every consumer eventually wants their own dialect.
/// </para>
/// <para>
/// <strong>Substitution is single-pass.</strong> <see cref="Regex.Replace(string, MatchEvaluator)"/>
/// scans the original text once; the text a match is replaced with is never
/// re-scanned for further placeholders. A value that itself contains
/// <c>{{name}}</c> is therefore inserted literally, not substituted again -
/// this is what keeps binding from recursing.
/// </para>
/// <para>
/// <strong>JSON-safe escaping is context-aware, not unconditional.</strong> A
/// placeholder written as a full JSON string value in the template - that is,
/// immediately preceded and followed by a <c>"</c> character, as in
/// <c>"{{customer}}"</c> - has its value JSON-escaped (quote, backslash,
/// control characters) so the produced text stays valid JSON. A placeholder
/// anywhere else in the text is substituted as-is: escaping it unconditionally
/// would litter plain-text instructions with stray backslashes.
/// </para>
/// </remarks>
public static class InstructionParameterBinder
{
    /// <summary>
    /// Matches a placeholder in the form <c>{{name}}</c>, where <c>name</c> is a
    /// valid identifier: a letter or underscore, then letters, digits, or underscores.
    /// </summary>
    internal static readonly Regex PlaceholderPattern = new(
        @"\{\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>Substitutes every placeholder in <paramref name="instructions"/> with a concrete value.</summary>
    /// <param name="instructions">The template text. <see langword="null"/> or empty is returned unchanged.</param>
    /// <param name="schema">The parameter schema the placeholders must be declared in.</param>
    /// <param name="values">
    /// The values supplied for this run. A schema entry missing from this
    /// dictionary falls back to its <see cref="AgentParameter.DefaultValue"/>,
    /// then to an empty string.
    /// </param>
    /// <returns>The text with every declared placeholder substituted.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A placeholder whose name is <strong>not</strong> declared in
    /// <paramref name="schema"/> is left untouched, literal braces and all -
    /// this method never fails. <see cref="AgentParameterValidator"/> is what
    /// rejects an undeclared placeholder, and it runs at compile time, before
    /// a value is ever bound.
    /// </remarks>
    public static string? Bind(
        string? instructions,
        IReadOnlyList<AgentParameter> schema,
        IReadOnlyDictionary<string, string>? values)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (string.IsNullOrEmpty(instructions) || schema.Count == 0)
        {
            return instructions;
        }

        var declared = new Dictionary<string, AgentParameter>(schema.Count, StringComparer.Ordinal);

        foreach (var parameter in schema)
        {
            declared[parameter.Name] = parameter;
        }

        return PlaceholderPattern.Replace(instructions, match => ResolveMatch(instructions, match, declared, values));
    }

    private static string ResolveMatch(
        string instructions,
        Match match,
        Dictionary<string, AgentParameter> declared,
        IReadOnlyDictionary<string, string>? values)
    {
        var name = match.Groups["name"].Value;

        if (!declared.TryGetValue(name, out var parameter))
        {
            return match.Value;
        }

        var raw = values is not null && values.TryGetValue(name, out var value)
            ? value
            : parameter.DefaultValue ?? string.Empty;

        var quotedInTemplate =
            match.Index > 0 && instructions[match.Index - 1] == '"' &&
            match.Index + match.Length < instructions.Length && instructions[match.Index + match.Length] == '"';

        return quotedInTemplate ? EscapeJsonStringBody(raw) : raw;
    }

    /// <summary>
    /// Escapes a value for placement <strong>inside</strong> an already-quoted
    /// JSON string - the surrounding quotes are the template's, not added here.
    /// </summary>
    private static string EscapeJsonStringBody(string value)
    {
        var builder = new StringBuilder(value.Length + 8);

        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                default:
                    if (character < 0x20)
                    {
                        builder.Append("\\u").Append(((int)character).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
