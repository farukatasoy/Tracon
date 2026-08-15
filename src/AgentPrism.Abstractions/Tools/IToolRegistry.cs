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
    bool TryGet(string name, [NotNullWhen(true)] out AIFunction? tool);
}
