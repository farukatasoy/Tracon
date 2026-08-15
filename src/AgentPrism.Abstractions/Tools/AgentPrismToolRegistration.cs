using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// A single tool registered with dependency injection. AgentPrism builds the
/// tool registry from these registrations.
/// </summary>
/// <remarks>
/// The consumer can also register tools from their own DI modules:
/// <code>
/// services.AddSingleton(new AgentPrismToolRegistration(myFunction));
/// </code>
/// </remarks>
public sealed class AgentPrismToolRegistration
{
    /// <summary>Creates a new tool registration.</summary>
    /// <param name="function">The tool to register.</param>
    /// <param name="requiresApproval">Whether explicit approval is required before the call.</param>
    /// <param name="source">
    /// The tool's source. <see langword="null"/> for tools defined in code;
    /// the server name for tools coming from a remote MCP server.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="function"/> is <see langword="null"/>.</exception>
    public AgentPrismToolRegistration(AIFunction function, bool requiresApproval = false, string? source = null)
    {
        ArgumentNullException.ThrowIfNull(function);

        Function = function;
        RequiresApproval = requiresApproval;
        Source = source;
    }

    /// <summary>The registered tool.</summary>
    public AIFunction Function { get; }

    /// <summary>Whether explicit approval is required before the call.</summary>
    public bool RequiresApproval { get; }

    /// <summary>The tool's source. <see langword="null"/> for tools defined in code.</summary>
    public string? Source { get; }
}
