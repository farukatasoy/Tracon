using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Looks up an agent's parameter schema and checks a request's supplied
/// values against it, producing a <c>400</c> when a required value is missing
/// or a supplied value is unknown.
/// </summary>
/// <remarks>
/// <para>
/// <c>POST /api/agents/{name}/run</c> and <c>POST /api/agents/{name}/estimate</c>
/// both call this <strong>same</strong> gate, so a missing or unknown
/// parameter produces the identical error on both endpoints
/// (<c>AgentParameterValidator.ValidateValues</c> underneath is the single
/// validator; this type only adds the HTTP shape).
/// </para>
/// <para>
/// A code-defined agent, or one with no stored definition, has no schema at
/// all - <see cref="AgentParameterValidator.ValidateValues"/> then rejects
/// any supplied value as unknown, and passes when none was supplied. This is
/// the existing, unchanged behavior for the overwhelming majority of agents
/// that never opt into this feature.
/// </para>
/// </remarks>
internal static class AgentParameterGate
{
    /// <summary>Looks up the schema and checks the supplied values against it.</summary>
    /// <param name="optionsMonitor">The runtime settings, for <see cref="AgentPrismOptions.MaxParameterValueLength"/>.</param>
    /// <param name="definitionStore">The definition store.</param>
    /// <param name="agentName">The agent to check parameters for.</param>
    /// <param name="version">
    /// The specific definition version to check against (an A/B experiment
    /// assignment). <see langword="null"/> checks the current version.
    /// </param>
    /// <param name="parameters">The values the request supplied.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The resolved definition (<see langword="null"/> for a code-defined or
    /// unknown agent) and, when validation fails, the <c>400</c> response to return.
    /// </returns>
    public static async ValueTask<AgentParameterGateResult> CheckAsync(
        IOptionsMonitor<AgentPrismOptions> optionsMonitor,
        IAgentDefinitionStore definitionStore,
        string agentName,
        int? version,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken cancellationToken)
    {
        var definition = version is { } requestedVersion
            ? await definitionStore.GetVersionAsync(agentName, requestedVersion, cancellationToken).ConfigureAwait(false)
            : await definitionStore.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

        var schema = definition?.Parameters ?? [];
        var result = AgentParameterValidator.ValidateValues(
            schema,
            parameters,
            optionsMonitor.CurrentValue.MaxParameterValueLength);

        return new AgentParameterGateResult(result.IsValid ? null : BuildProblem(result), definition);
    }

    private static IResult BuildProblem(AgentParameterValidationResult result)
    {
        var missing = new List<string>();
        var unknown = new List<string>();
        var tooLong = new List<string>();

        foreach (var error in result.Errors)
        {
            var target = error.Code switch
            {
                AgentParameterValidator.MissingParameterCode => missing,
                AgentParameterValidator.ValueTooLongCode => tooLong,
                _ => unknown,
            };

            target.Add(error.ParameterName);
        }

        var detailParts = new List<string>(missing.Count + unknown.Count + tooLong.Count);
        detailParts.AddRange(missing.Select(static name => $"Missing required parameter '{name}'."));
        detailParts.AddRange(unknown.Select(static name => $"Unknown parameter '{name}'."));
        detailParts.AddRange(tooLong.Select(static name => $"Parameter '{name}' exceeds the maximum value length."));

        return Results.Problem(
            title: "Invalid run parameters",
            detail: string.Join(" ", detailParts),
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["missingParameters"] = missing,
                ["unknownParameters"] = unknown,
                ["tooLongParameters"] = tooLong,
            });
    }
}

/// <summary>The outcome of <see cref="AgentParameterGate.CheckAsync"/>.</summary>
/// <param name="Problem">The <c>400</c> response to return; <see langword="null"/> when validation passed.</param>
/// <param name="Definition">The resolved definition, when one exists.</param>
internal readonly record struct AgentParameterGateResult(IResult? Problem, AgentDefinition? Definition);
