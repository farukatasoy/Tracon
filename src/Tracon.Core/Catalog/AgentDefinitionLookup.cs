namespace Tracon;

/// <summary>
/// Finds the definition the catalog runs for an agent name: a definition
/// registered in code wins over a stored one, as it does in the catalog.
/// </summary>
/// <remarks>
/// The parameter gate of <c>POST /api/agents/{name}/run</c> and the eval job
/// both read the definition behind an agent to bind its parameter values.
/// Reading only the store missed a definition registered with
/// <c>AddAgent(AgentDefinition)</c>: its parameters were refused as unknown on
/// <c>/run</c>, and an eval case's values were dropped.
/// </remarks>
internal static class AgentDefinitionLookup
{
    /// <summary>Finds the definition behind an agent name.</summary>
    /// <param name="codeRegistrations">The agents registered in code.</param>
    /// <param name="store">The definition store.</param>
    /// <param name="agentName">The agent's name.</param>
    /// <param name="version">
    /// A pinned version. A version always names a stored definition: code has
    /// no versions, and the catalog refuses a pinned run of a code agent.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The definition (<see langword="null"/> for a factory agent, an agent
    /// source's agent, or an unknown name) and where it comes from.
    /// </returns>
    public static async ValueTask<(AgentDefinition? Definition, AgentDefinitionOrigin Origin)> FindAsync(
        IEnumerable<CodeAgentRegistration> codeRegistrations,
        IAgentDefinitionStore store,
        string agentName,
        int? version,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codeRegistrations);
        ArgumentNullException.ThrowIfNull(store);

        if (version is null &&
            codeRegistrations.FirstOrDefault(
                registration => string.Equals(registration.Name, agentName, StringComparison.Ordinal)) is { } code)
        {
            return (code.Definition, AgentDefinitionOrigin.Code);
        }

        var definition = version is { } pinned
            ? await store.GetVersionAsync(agentName, pinned, cancellationToken).ConfigureAwait(false)
            : await store.GetAsync(agentName, cancellationToken).ConfigureAwait(false);

        return (definition, AgentDefinitionOrigin.Database);
    }

    /// <summary>The source name a definition of <paramref name="origin"/> lists under.</summary>
    /// <param name="origin">Where the definition comes from.</param>
    /// <returns>The source name.</returns>
    public static string SourceName(AgentDefinitionOrigin origin)
        => origin == AgentDefinitionOrigin.Code ? "code" : "database";
}
