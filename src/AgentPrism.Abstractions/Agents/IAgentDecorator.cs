using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// A decorator applied to every agent resolved from the catalog. AgentPrism adds run
/// recording through this mechanism; a consumer can register its own decorator the
/// same way.
/// </summary>
/// <remarks>
/// <para>
/// A decorator is <em>not</em> Microsoft Agent Framework middleware. The MAF middleware
/// chain is specific to an agent, and <c>HarnessAgent</c> adds its own inner decorators.
/// An outer decorator works the same way on every agent kind, the harness included.
/// </para>
/// </remarks>
public interface IAgentDecorator
{
    /// <summary>
    /// Gets the order of application. A lower value wraps <em>inside</em>, a higher value
    /// wraps <em>outside</em>. Run recording uses 0, which makes it the outermost decorator.
    /// </summary>
    int Order { get; }

    /// <summary>Decorates the agent.</summary>
    /// <param name="agent">The agent to decorate.</param>
    /// <param name="descriptor">The catalog summary of the agent.</param>
    /// <returns>The decorated agent.</returns>
    AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor);
}
