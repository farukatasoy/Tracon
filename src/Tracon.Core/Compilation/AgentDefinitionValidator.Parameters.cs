using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>The parameter placeholder step of <see cref="AgentDefinitionValidator"/>.</summary>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The code of the warning about a declared parameter that the instructions write in single braces.</summary>
    internal const string SingleBracePlaceholderCode = ValidationCodes.SingleBracePlaceholder;

    // One brace on each side, the way a template of another system writes a value: {customer}. The look-around keeps a
    // double-brace placeholder ({{customer}}) and a longer run of braces out of the match.
    private static readonly Regex SingleBracePattern = new(
        @"(?<!\{)\{[ \t]*(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*\}(?!\})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Warns when the instructions write a declared parameter in single braces, a form Tracon does not replace.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <param name="messages">The report's messages.</param>
    /// <remarks>
    /// <para>
    /// The placeholder is <c>{{name}}</c>. A prompt moved from a system that writes <c>{name}</c> compiles, runs and
    /// sends the braces to the model: nothing else shows the mistake. Only a name the definition declares as a
    /// parameter is reported, so a JSON example or a template of another kind in the same text stays quiet. It is a
    /// warning, because the text may mean the braces.
    /// </para>
    /// </remarks>
    private static void CheckParameterPlaceholders(AgentDefinition definition, List<ValidationMessage> messages)
    {
        if (definition.Parameters.Count == 0)
        {
            return;
        }

        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in definition.Parameters)
        {
            declared.Add(parameter.Name);
        }

        CheckText(definition, definition.Instructions, "instructions", declared, messages);

        if (definition.InstructionsByCulture is { Count: > 0 } byCulture)
        {
            // The compiler picks the text of the caller's culture, so a variant carries the same mistake.
            foreach (var (culture, text) in byCulture)
            {
                CheckText(definition, text, $"instructionsByCulture[{culture}]", declared, messages);
            }
        }
    }

    private static void CheckText(
        AgentDefinition definition,
        string? text,
        string path,
        HashSet<string> declared,
        List<ValidationMessage> messages)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in SingleBracePattern.Matches(text))
        {
            var name = match.Groups["name"].Value;

            if (declared.Contains(name) && reported.Add(name))
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = SingleBracePlaceholderCode,
                    Message = $"The instructions of agent '{definition.Name}' ({path}) contain '{{{name}}}', which Tracon does not replace. " +
                              $"The placeholder for the parameter '{name}' is '{{{{{name}}}}}'.",
                    Path = path,
                });
            }
        }
    }
}
