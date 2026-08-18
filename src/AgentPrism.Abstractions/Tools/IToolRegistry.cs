using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// The registry of tools registered in code.
/// </summary>
/// <remarks>
/// This registry is one of AgentPrism's <strong>security boundaries</strong>.
/// An agent definition can only point to a registered tool by name. Tool
/// <em>code</em> cannot be written from the UI; only a selection is made from
/// registered tools. This keeps anyone with UI access from running code on the server.
/// </remarks>
public interface IToolRegistry
{
    /// <summary>Returns the definitions of all registered tools, ordered by name.</summary>
    /// <returns>The tool definitions.</returns>
    IReadOnlyList<ToolDescriptor> List();

    /// <summary>Fetches the tool with the given name.</summary>
    /// <param name="name">The tool name. Comparison is case-sensitive.</param>
    /// <param name="tool">The tool found.</param>
    /// <returns><see langword="true"/> if the tool is registered.</returns>
    /// <remarks>
    /// The result is an <see cref="AIFunctionDeclaration"/>, not an
    /// <see cref="AIFunction"/>: a client-side tool (<c>AddClientTool</c>) is
    /// registered as a declaration-only <c>AIFunctionDeclaration</c>, which
    /// is not an <see cref="AIFunction"/> and cannot be invoked. Every
    /// registered tool — server-side or client-side — is an
    /// <see cref="AIFunctionDeclaration"/>, so <see cref="AIFunctionDeclaration.JsonSchema"/>
    /// is always available. Callers pass the result straight to
    /// <c>ChatOptions.Tools</c>, which accepts the broader <c>AITool</c>.
    /// </remarks>
    bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool);
}
