using Microsoft.Agents.AI;

namespace Tracon;

/// <summary>
/// A decorator applied to every agent resolved from the catalog. Tracon adds run
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
    /// Gets the order of application. A lower value wraps <em>outside</em>, a higher value
    /// wraps <em>inside</em>. Run recording uses 0, which makes it the outermost decorator.
    /// </summary>
    /// <remarks>
    /// The direction follows from how the pipeline applies decorators: it sorts by
    /// descending order and each step wraps the agent the previous step produced, so the
    /// highest value is applied first and every later decorator wraps it. Place a decorator
    /// that has to observe or veto everything below it at a <em>low</em> value, and one
    /// that has to sit close to the agent's own calls at a <em>high</em> value - which is
    /// why run recording is 0 and the tool-approval gate is 20.
    /// </remarks>
    int Order { get; }

    /// <summary>Decorates the agent.</summary>
    /// <param name="agent">The agent to decorate.</param>
    /// <param name="descriptor">The catalog summary of the agent.</param>
    /// <returns>The decorated agent.</returns>
    AIAgent Decorate(AIAgent agent, AgentDescriptor descriptor);
}
