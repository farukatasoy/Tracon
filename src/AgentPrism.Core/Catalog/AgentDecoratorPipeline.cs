using Microsoft.Agents.AI;

namespace AgentPrism;

/// <summary>
/// Applies every registered <see cref="IAgentDecorator"/> to an agent, in the
/// same order <see cref="CompositeAgentCatalog"/> uses.
/// </summary>
/// <remarks>
/// A caller that compiles an agent OUTSIDE the catalog (a parameterized run,
/// which bypasses <c>CompiledAgentCache</c> and therefore the catalog's own
/// <c>ResolveAsync</c>) must still apply this same pipeline by hand - otherwise
/// the agent runs undecorated: no run recording, no telemetry, no tool-approval
/// gate.
/// </remarks>
public static class AgentDecoratorPipeline
{
    /// <summary>Applies every decorator, ordered from innermost to outermost.</summary>
    /// <param name="agent">The compiled agent.</param>
    /// <param name="descriptor">The catalog summary of the agent.</param>
    /// <param name="decorators">The registered decorators.</param>
    /// <returns>The decorated agent.</returns>
    /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
    public static AIAgent Apply(AIAgent agent, AgentDescriptor descriptor, IEnumerable<IAgentDecorator> decorators)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(decorators);

        foreach (var decorator in decorators.OrderByDescending(static decorator => decorator.Order))
        {
            agent = decorator.Decorate(agent, descriptor);
        }

        return agent;
    }
}
