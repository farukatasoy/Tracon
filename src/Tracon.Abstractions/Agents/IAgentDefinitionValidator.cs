namespace Tracon;

/// <summary>
/// Checks an agent definition the way <c>POST /api/agents/validate</c> does, without saving it and without calling
/// a model.
/// </summary>
/// <remarks>
/// <para>
/// The store does not check what it keeps: <see cref="IAgentDefinitionStore.SaveAsync"/> saves a definition that
/// cannot be compiled (an Anthropic model with a <c>Temperature</c>, a tool nobody registered) and the catalog fails
/// on it when the agent is first resolved. The HTTP writers and the import run this check before they save. A host
/// that writes definitions from its own code (a seed, a migration, an import tool) calls it before
/// <see cref="IAgentDefinitionStore.SaveAsync"/> to learn the same thing at the moment it matters.
/// </para>
/// <para>
/// The check reads the registries of the running host (model providers, tools, skills, other agents) and runs every
/// check independently, so one call reports every finding. <see cref="AgentValidationReport.Valid"/> is
/// <see langword="false"/> when a finding has <see cref="ValidationSeverity.Error"/>; a warning leaves it
/// <see langword="true"/>. A check that cannot finish because a resource is unreachable (an MCP server) sets
/// <see cref="AgentValidationReport.Inconclusive"/> and does not fail the call.
/// </para>
/// <para>
/// The implementation is registered by <c>AddTracon()</c> as a singleton. A call reads the ambient tenant for the
/// model credential and egress policy, like the compile path does, so call it inside the tenant the definition
/// belongs to. It writes nothing, opens no <c>runs</c> row and spends no token.
/// </para>
/// <para>
/// No delivery guarantee applies: the call runs in the caller's own flow and nothing is queued, retried or published.
/// </para>
/// <example>
/// <code>
/// app.MapPost("/agents/seed", async (AgentDefinition definition, IAgentDefinitionValidator validator, IAgentDefinitionStore store, CancellationToken cancellationToken) =&gt;
/// {
///     var report = await validator.ValidateAsync(definition, cancellationToken);
///
///     if (report.Valid)
///     {
///         await store.SaveAsync(definition, cancellationToken);
///     }
///
///     return report;
/// });
/// </code>
/// </example>
/// </remarks>
public interface IAgentDefinitionValidator
{
    /// <summary>Validates a definition without saving it or calling a model.</summary>
    /// <param name="definition">The definition to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report with every finding.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    ValueTask<AgentValidationReport> ValidateAsync(AgentDefinition definition, CancellationToken cancellationToken = default);
}
