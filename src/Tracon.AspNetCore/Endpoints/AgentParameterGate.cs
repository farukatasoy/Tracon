using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon;

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
/// The schema is read from the definition the catalog would run: the sources are tried in the
/// catalog's own priority order and the first one that holds the name decides. A definition
/// registered in code (<c>AddAgent(AgentDefinition)</c>), a stored definition and a definition a
/// custom <see cref="IAgentDefinitionSource"/> returns each carry their schema. An agent with no
/// definition at all (a factory agent, an agent a custom <see cref="IAgentSource"/> builds) has no
/// schema - <see cref="AgentParameterValidator.ValidateValues"/> then rejects any supplied value as
/// unknown, and passes when none was supplied.
/// </para>
/// <para>
/// The gate used to look in the code registrations and then the store, never in the sources. A
/// custom source that outranked the store won the name in the catalog and lost it here: a stored
/// definition with a schema pulled the run away from the source that won.
/// </para>
/// </remarks>
internal static class AgentParameterGate
{
    /// <summary>Looks up the schema and checks the supplied values against it.</summary>
    /// <param name="optionsMonitor">The runtime settings, for <see cref="TraconOptions.MaxParameterValueLength"/>.</param>
    /// <param name="services">The request's services, for the definition lookup.</param>
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
        IOptionsMonitor<TraconOptions> optionsMonitor,
        IServiceProvider services,
        string agentName,
        int? version,
        IReadOnlyDictionary<string, string>? parameters,
        CancellationToken cancellationToken)
    {
        AgentDefinition? definition;

        try
        {
            definition = (await services
                .GetRequiredService<AgentDefinitionLookup>()
                .FindAsync(agentName, version, cancellationToken)
                .ConfigureAwait(false)).Definition;
        }
        catch (TraconException ex)
        {
            // A source that fails while it is asked for the definition is answered like one that fails while the
            // catalog resolves the agent: the lookup has already recorded the failure and normalized its text.
            return new AgentParameterGateResult(
                Results.Problem(
                    title: "Agent compilation failed",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest),
                Definition: null);
        }

        var schema = definition?.Parameters ?? [];
        var result = AgentParameterValidator.ValidateValues(
            schema,
            parameters,
            optionsMonitor.CurrentValue.MaxParameterValueLength);

        return new AgentParameterGateResult(result.IsValid ? null : Problem(result), definition);
    }

    /// <summary>The <c>400</c> for a failed parameter check, the same shape on every surface.</summary>
    /// <param name="result">The failed check.</param>
    /// <returns>The response.</returns>
    public static IResult Problem(AgentParameterValidationResult result)
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
internal readonly record struct AgentParameterGateResult(
    IResult? Problem,
    AgentDefinition? Definition);
