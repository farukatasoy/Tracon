using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// The factory that builds an <see cref="AIContextProvider"/> for
/// <see cref="AgentDefinition.McpResourceUris"/> (Mode A).
/// </summary>
/// <remarks>
/// <para>
/// The abstraction lives in <c>AgentPrism.Abstractions</c> because
/// <c>AgentDefinitionCompiler</c> (<c>AgentPrism.Core</c>) wants to add MCP
/// resources to the context but does not depend on the <c>AgentPrism.Mcp</c>
/// package: MCP stays an optional package. Same rationale as
/// <see cref="IMcpToolRefresher"/>.
/// </para>
/// </remarks>
public interface IMcpResourceContextProviderFactory
{
    /// <summary>
    /// Builds a context provider from the given resource references.
    /// </summary>
    /// <param name="resourceReferences">
    /// References in <c>"{server}:{uri}"</c> form (<see cref="AgentDefinition.McpResourceUris"/>).
    /// </param>
    /// <param name="tenantId">The tenant identifier. Resources are read only from this tenant's servers.</param>
    /// <returns>The context provider.</returns>
    AIContextProvider Create(IReadOnlyList<string> resourceReferences, string tenantId);
}
