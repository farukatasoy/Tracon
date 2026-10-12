namespace Tracon;

/// <summary>
/// Turns the values a caller supplied into the FINAL value set of a run: what
/// the instructions were actually bound with, and what the run records.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="InstructionParameterBinder"/> binds a supplied value first, then
/// the schema default, then an empty string. This type applies the first two
/// steps to a dictionary, so a run can record exactly what it was told (a
/// replay or a resume then repeats it) instead of the partial set the caller
/// happened to send.
/// </para>
/// <para>
/// A parameter with neither a value nor a default is NOT in the final set: it
/// binds the empty string, and a replay that finds it absent binds the same.
/// </para>
/// </remarks>
internal static class ParameterValueSet
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    /// <summary>
    /// Resolves the final value set for a schema.
    /// </summary>
    /// <param name="schema">The schema the values are for.</param>
    /// <param name="supplied">The values the caller supplied. <see langword="null"/> means none.</param>
    /// <returns>
    /// Every declared parameter that has a supplied value or a default, with the
    /// supplied value winning. A supplied name the schema does not declare is
    /// not carried: <see cref="AgentParameterValidator.ValidateValues"/> rejects
    /// it before a run reaches here.
    /// </returns>
    public static IReadOnlyDictionary<string, string> Resolve(
        IReadOnlyList<AgentParameter> schema,
        IReadOnlyDictionary<string, string>? supplied)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (schema.Count == 0)
        {
            return Empty;
        }

        var final = new Dictionary<string, string>(schema.Count, StringComparer.Ordinal);

        foreach (var parameter in schema)
        {
            if (supplied is not null && supplied.TryGetValue(parameter.Name, out var value))
            {
                final[parameter.Name] = value;
            }
            else if (parameter.DefaultValue is { } fallback)
            {
                final[parameter.Name] = fallback;
            }
        }

        return final;
    }

    /// <summary>
    /// Keeps only the values whose names a schema declares.
    /// </summary>
    /// <param name="schema">The schema to filter for.</param>
    /// <param name="values">The values to filter. <see langword="null"/> means none.</param>
    /// <returns>The values the schema declares; empty when none match.</returns>
    /// <remarks>
    /// A workflow step receives the workflow's values filtered by its OWN
    /// schema: the workflow's contract is wider than any one step's.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Intersect(
        IReadOnlyList<AgentParameter> schema,
        IReadOnlyDictionary<string, string>? values)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (values is null or { Count: 0 } || schema.Count == 0)
        {
            return Empty;
        }

        var selected = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var parameter in schema)
        {
            if (values.TryGetValue(parameter.Name, out var value))
            {
                selected[parameter.Name] = value;
            }
        }

        return selected;
    }

    /// <summary>
    /// Describes a failed validation in one sentence per violation.
    /// </summary>
    /// <param name="result">The failed validation.</param>
    /// <returns>The text; never contains a value, only names.</returns>
    /// <remarks>
    /// Names only, never values: this text reaches a run's error, a log line and
    /// a job's failure reason, and a value is user content.
    /// </remarks>
    public static string Describe(AgentParameterValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Join(
            " ",
            result.Errors.Select(static error => error.Code switch
            {
                AgentParameterValidator.MissingParameterCode => $"Missing required parameter '{error.ParameterName}'.",
                AgentParameterValidator.ValueTooLongCode => $"Parameter '{error.ParameterName}' exceeds the maximum value length.",
                _ => $"Unknown parameter '{error.ParameterName}'.",
            }));
    }
}
