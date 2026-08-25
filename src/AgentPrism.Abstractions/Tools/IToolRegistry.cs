using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// The registry of tools registered in code.
/// </summary>
/// <remarks>
/// AgentPrism's implementation and its invocation pipeline are a security boundary.
/// Do not implement or replace this service. Replacing it removes authorization,
/// timeout, approval, and output-truncation wrappers. Register a tool through an
/// <c>AddTool*</c> API instead. The registry is an immutable startup snapshot.
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
