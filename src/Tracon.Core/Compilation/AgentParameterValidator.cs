using System.Text;
using System.Text.RegularExpressions;

namespace Tracon;

/// <summary>
/// Validates an <see cref="AgentDefinition"/>'s parameter schema, and validates
/// a run's supplied values against it.
/// </summary>
/// <remarks>
/// <para>
/// Two independent checks live here, run at two different times:
/// </para>
/// <list type="number">
/// <item>
/// <description>
/// <see cref="ValidateSchema"/> - compile-time self-consistency of the schema
/// against the instructions text. Runs inside <see cref="AgentDefinitionCompiler"/>,
/// so <c>POST /api/agents</c>, <c>PUT /api/agents/{name}</c>, and
/// <c>POST /api/agents/validate</c> all inherit it through the compile step
/// they already run.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="ValidateValues"/> - run-time check of a request's parameter
/// values against the schema. <c>POST /api/agents/{name}/run</c> and
/// <c>POST /api/agents/{name}/estimate</c> both call this <strong>same</strong>
/// method, so a missing or unknown parameter produces the identical error on
/// both endpoints.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class AgentParameterValidator
{
    /// <summary>Error code: a required parameter has neither a supplied value nor a default.</summary>
    public const string MissingParameterCode = "missing_parameter";

    /// <summary>Error code: a supplied value's name is not declared in the schema.</summary>
    public const string UnknownParameterCode = "unknown_parameter";

    /// <summary>Error code: a supplied value exceeds the configured maximum length.</summary>
    public const string ValueTooLongCode = "value_too_long";

    private static readonly Regex IdentifierPattern = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Checks a definition's parameter schema against its own instructions text.
    /// </summary>
    /// <param name="definition">The definition being compiled.</param>
    /// <returns>
    /// One message per violation; empty when the schema is self-consistent. A
    /// definition with an empty <see cref="AgentDefinition.Parameters"/> list
    /// is never checked against its text - <c>{{...}}</c> is plain text for an
    /// agent that declares no parameters at all, and stays that way (no
    /// surprise for the agents that never opt into this feature).
    /// </returns>
    public static IReadOnlyList<string> ValidateSchema(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Parameters.Count == 0)
        {
            return [];
        }

        List<string>? errors = null;
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in definition.Parameters)
        {
            if (!IdentifierPattern.IsMatch(parameter.Name))
            {
                (errors ??= []).Add(
                    $"Parameter name '{parameter.Name}' is not a valid identifier - it must start with a " +
                    "letter or underscore, followed by letters, digits, or underscores.");
            }

            if (!declaredNames.Add(parameter.Name))
            {
                (errors ??= []).Add($"Parameter '{parameter.Name}' is declared more than once.");
            }
        }

        CheckPlaceholders(definition.Instructions, "Instructions", declaredNames, ref errors);

        if (definition.InstructionsByCulture is { Count: > 0 } byCulture)
        {
            foreach (var (culture, text) in byCulture)
            {
                CheckPlaceholders(text, $"InstructionsByCulture['{culture}']", declaredNames, ref errors);
            }
        }

        return errors ?? [];
    }

    /// <summary>
    /// Checks a run's supplied parameter values against a definition's schema.
    /// </summary>
    /// <param name="schema">The target agent's parameter schema.</param>
    /// <param name="values">The values a run request supplied. <see langword="null"/> means none.</param>
    /// <param name="maxValueLength">
    /// The largest UTF-8 byte count a supplied value may have. <see langword="null"/> means no limit
    /// (the default, kept for a caller that predates this check).
    /// </param>
    /// <returns>The validation outcome.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <see langword="null"/>.</exception>
    public static AgentParameterValidationResult ValidateValues(
        IReadOnlyList<AgentParameter> schema,
        IReadOnlyDictionary<string, string>? values,
        int? maxValueLength = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        List<AgentParameterValidationError>? errors = null;
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var parameter in schema)
        {
            declaredNames.Add(parameter.Name);

            if (values is not null && values.TryGetValue(parameter.Name, out var suppliedValue))
            {
                if (maxValueLength is { } limit && Encoding.UTF8.GetByteCount(suppliedValue) > limit)
                {
                    (errors ??= []).Add(new AgentParameterValidationError
                    {
                        Code = ValueTooLongCode,
                        ParameterName = parameter.Name,
                    });
                }
            }
            else if (parameter.DefaultValue is null && parameter.Required)
            {
                (errors ??= []).Add(new AgentParameterValidationError
                {
                    Code = MissingParameterCode,
                    ParameterName = parameter.Name,
                });
            }
        }

        if (values is not null)
        {
            foreach (var name in values.Keys)
            {
                // 🚨 An extra value is never silently dropped: a typo in a
                // parameter name would otherwise disappear without a trace
                // in production (docs/86, section 86.3).
                if (!declaredNames.Contains(name))
                {
                    (errors ??= []).Add(new AgentParameterValidationError
                    {
                        Code = UnknownParameterCode,
                        ParameterName = name,
                    });
                }
            }
        }

        return new AgentParameterValidationResult
        {
            IsValid = errors is null,
            Errors = errors ?? [],
        };
    }

    private static void CheckPlaceholders(
        string? text,
        string source,
        HashSet<string> declaredNames,
        ref List<string>? errors)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (Match match in InstructionParameterBinder.PlaceholderPattern.Matches(text))
        {
            var name = match.Groups["name"].Value;

            if (!declaredNames.Contains(name))
            {
                (errors ??= []).Add($"{source} references undeclared parameter '{{{{{name}}}}}'.");
            }
        }
    }
}

/// <summary>The outcome of <see cref="AgentParameterValidator.ValidateValues"/>.</summary>
public sealed record AgentParameterValidationResult
{
    /// <summary>Gets whether every required parameter has a value and no supplied value is unknown.</summary>
    public required bool IsValid { get; init; }

    /// <summary>Gets the individual violations. Empty when <see cref="IsValid"/> is <see langword="true"/>.</summary>
    public IReadOnlyList<AgentParameterValidationError> Errors { get; init; } = [];
}

/// <summary>A single parameter value violation.</summary>
public sealed record AgentParameterValidationError
{
    /// <summary>
    /// Gets the error code: <see cref="AgentParameterValidator.MissingParameterCode"/>,
    /// <see cref="AgentParameterValidator.UnknownParameterCode"/>, or
    /// <see cref="AgentParameterValidator.ValueTooLongCode"/>.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>Gets the parameter name the violation is about.</summary>
    public required string ParameterName { get; init; }
}
